using System.Runtime.InteropServices;
using System.Security.Principal;
using DeskWidgets.Core;
using DeskWidgets.Core.Diagnostics;
using Microsoft.Win32;

namespace DeskWidgets.Hosting;

/// <summary>
/// "Start with Windows" as a per-user Task Scheduler logon task, the way the
/// DesktopRightAlign tool starts: the user's own session, no password, no
/// elevation, allowed on battery, never timed out, 15 s after sign-in. It
/// replaces the HKCU Run entry, which Explorer silently skipped at sign-in on
/// 2026-10-13. A leftover Run entry still counts as "on" until the next toggle
/// turns it into the task (or removes it).
/// </summary>
internal static class StartupRegistration
{
    private const string TaskName = "DeskWidgets";
    private const string Arguments = "--autostart";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "DeskWidgets";

    /// <summary>False in a test instance (--data-dir): it must never read or change the user's logon task.</summary>
    public static bool Available => !DataRoot.IsTestInstance;

    public static bool IsEnabled => Available && (TaskEnabled() || HasRunEntry());

    public static void SetEnabled(bool enabled)
    {
        if (!Available)
            throw new InvalidOperationException("Start with Windows is not available in a test instance (--data-dir).");
        if (enabled)
            RegisterTask();
        else
            DisableTask();
        RemoveRunEntry();
    }

    private static string Executable => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "DeskWidgets.exe");

    private static string UserSid
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent(); // holds a token handle until disposed
            return identity.User!.Value;
        }
    }

    private static bool TaskEnabled()
    {
        try
        {
            using var scheduler = new Scheduler();
            dynamic? task = scheduler.FindTask();
            return task is not null && (bool)task.Enabled
                && string.Equals(ActionPath(scheduler, task), Executable, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    private static void RegisterTask()
    {
        using var scheduler = new Scheduler();
        object? existing = scheduler.FindTask();
        if (existing is not null && !IsOurs(scheduler, existing))
            throw new InvalidOperationException($"A scheduled task named '{TaskName}' belongs to another program.");

        dynamic definition = scheduler.Keep(scheduler.Service.NewTask(0));
        dynamic info = scheduler.Keep(definition.RegistrationInfo);
        info.Description = "Starts Desk Widgets when you sign in. Turn it off in the tray menu.";
        info.Author = "DeskWidgets";
        dynamic principal = scheduler.Keep(definition.Principal);
        principal.UserId = UserSid;
        principal.LogonType = 3; // TASK_LOGON_INTERACTIVE_TOKEN: the user's own desktop session, no password
        principal.RunLevel = 0;  // TASK_RUNLEVEL_LUA: no administrator rights
        dynamic settings = scheduler.Keep(definition.Settings);
        settings.Enabled = true;
        settings.AllowDemandStart = true;
        settings.StartWhenAvailable = true;
        settings.DisallowStartIfOnBatteries = false;
        settings.StopIfGoingOnBatteries = false;
        settings.RunOnlyIfIdle = false;
        settings.RunOnlyIfNetworkAvailable = false;
        settings.ExecutionTimeLimit = "PT0S"; // a tray app must not be stopped after the default 72 hours
        settings.MultipleInstances = 2;       // TASK_INSTANCES_IGNORE_NEW
        settings.RestartInterval = "PT1M";
        settings.RestartCount = 3;
        dynamic triggers = scheduler.Keep(definition.Triggers);
        dynamic trigger = scheduler.Keep(triggers.Create(9)); // TASK_TRIGGER_LOGON
        trigger.Enabled = true;
        trigger.UserId = UserSid;
        trigger.Delay = "PT15S"; // let the desktop come up first
        dynamic actions = scheduler.Keep(definition.Actions);
        dynamic action = scheduler.Keep(actions.Create(0)); // TASK_ACTION_EXEC
        action.Path = Executable;
        action.Arguments = Arguments;
        action.WorkingDirectory = AppContext.BaseDirectory;
        scheduler.Keep(scheduler.Folder.RegisterTaskDefinition(TaskName, definition, 6 /* TASK_CREATE_OR_UPDATE */, UserSid, null, 3, null));

        if (!TaskEnabled())
            throw new InvalidOperationException("The sign-in task did not read back as registered.");
        Log.Info($"startup: sign-in task registered for {Executable}");
    }

    private static void DisableTask()
    {
        using var scheduler = new Scheduler();
        object? task = scheduler.FindTask();
        if (task is null || !IsOurs(scheduler, task))
            return;
        ((dynamic)task).Enabled = false;
        Log.Info("startup: sign-in task disabled");
    }

    /// <summary>A task is ours when its one action starts a DeskWidgets.exe (from any build folder).</summary>
    private static bool IsOurs(Scheduler scheduler, object task)
        => string.Equals(Path.GetFileName(ActionPath(scheduler, task)), "DeskWidgets.exe", StringComparison.OrdinalIgnoreCase);

    private static string? ActionPath(Scheduler scheduler, object task)
    {
        dynamic definition = scheduler.Keep(((dynamic)task).Definition);
        dynamic actions = scheduler.Keep(definition.Actions);
        if ((int)actions.Count != 1)
            return null;
        dynamic action = scheduler.Keep(actions.Item(1));
        return (int)action.Type == 0 /* TASK_ACTION_EXEC */ ? (string)action.Path : null;
    }

    private static bool HasRunEntry()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(RunValue) is string;
    }

    private static void RemoveRunEntry()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key is null || key.GetValue(RunValue) is null)
            return;
        key.DeleteValue(RunValue, throwOnMissingValue: false);
        Log.Info("startup: removed the old Run entry");
    }

    /// <summary>The Task Scheduler COM objects of one operation, all released at the end.</summary>
    private sealed class Scheduler : IDisposable
    {
        private readonly List<object> _objects = [];

        public Scheduler()
        {
            try
            {
                Service = Keep(Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service", throwOnError: true)!)!);
                Service.Connect();
                Folder = Keep(Service.GetFolder(@"\"));
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public dynamic Service { get; } = null!;

        public dynamic Folder { get; } = null!;

        public dynamic Keep(object value)
        {
            _objects.Add(value);
            return value;
        }

        public object? FindTask()
        {
            try
            {
                return Keep(Folder.GetTask(TaskName));
            }
            catch (Exception ex) when (ex.HResult == unchecked((int)0x80070002)) // no such task
            {
                return null;
            }
        }

        public void Dispose()
        {
            var released = new HashSet<object>(ReferenceEqualityComparer.Instance);
            foreach (object value in Enumerable.Reverse(_objects))
            {
                if (released.Add(value) && Marshal.IsComObject(value))
                    Marshal.FinalReleaseComObject(value);
            }
            _objects.Clear();
        }
    }
}
