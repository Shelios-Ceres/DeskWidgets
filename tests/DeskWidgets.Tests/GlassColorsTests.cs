using DeskWidgets.Widgets;

namespace DeskWidgets.Tests;

/// <summary>
/// Pins the Glass colour roles to the literals the clock and battery widgets
/// carried before they shared one palette (docs/THEMES.md section 8), so that
/// moving the colours into <see cref="GlassColors"/> changed nothing visible.
/// The one deliberate change since: Primary and Secondary text became solid
/// black (light) and white (dark) on 2026-10-27, and the old Secondary lives
/// on as Inactive.
/// </summary>
public class GlassColorsTests
{
    [Fact]
    public void Light_KeepsEveryRoleOfTheOldLiterals()
    {
        var c = GlassColors.Light;

        Assert.Equal(0xFF000000u, c.Primary);     // clock time, world clock names, battery numbers: solid black since 2026-10-27
        Assert.Equal(0xFF000000u, c.Secondary);   // clock date, world clock time and date, battery names: solid black since 2026-10-27
        Assert.Equal(0x993C3C43u, c.Inactive);    // the old Secondary: a disconnected device, an unknown level, a missing app
        Assert.Equal(0x24000000u, c.Track);
        Assert.Equal(0x14000000u, c.Placeholder);
        Assert.Equal(0xFF34C759u, c.Green);
        Assert.Equal(0xFFFFCC00u, c.Yellow);
        Assert.Equal(0xFFFF3B30u, c.Red);
        Assert.Equal(0xFFFF453Au, c.Accent);      // clock weekday
        Assert.Equal(0xFFFF9500u, c.Orange);      // world clock second hand
        Assert.Equal(0xFFFFFFFFu, c.DayFace);
        Assert.Equal(0xFF1C1C1Eu, c.DayInk);
        Assert.Equal(0x731C1C1Eu, c.DayFaint);
        Assert.Equal(0xFF1C1C1Eu, c.NightFace);
        Assert.Equal(0xFFFFFFFFu, c.NightInk);
        Assert.Equal(0x73FFFFFFu, c.NightFaint);
        Assert.Equal(0xFFFFFFFFu, c.OnAccent);
    }

    [Fact]
    public void Dark_KeepsEveryRoleOfTheOldLiterals()
    {
        var c = GlassColors.Dark;

        Assert.Equal(0xFFFFFFFFu, c.Primary);
        Assert.Equal(0xFFFFFFFFu, c.Secondary);   // solid white since 2026-10-27
        Assert.Equal(0x99EBEBF5u, c.Inactive);
        Assert.Equal(0x3DFFFFFFu, c.Track);
        Assert.Equal(0x1FFFFFFFu, c.Placeholder);
        Assert.Equal(0xFF30D158u, c.Green);
        Assert.Equal(0xFFFFD60Au, c.Yellow);
        Assert.Equal(0xFFFF453Au, c.Red);
        Assert.Equal(0xFFFF453Au, c.Accent);
        Assert.Equal(0xFFFF9500u, c.Orange);
        Assert.Equal(0xFFFFFFFFu, c.DayFace);
        Assert.Equal(0xFF1C1C1Eu, c.DayInk);
        Assert.Equal(0x731C1C1Eu, c.DayFaint);
        Assert.Equal(0xFF1C1C1Eu, c.NightFace);
        Assert.Equal(0xFFFFFFFFu, c.NightInk);
        Assert.Equal(0x73FFFFFFu, c.NightFaint);
        Assert.Equal(0xFF000000u, c.OnAccent);
    }

    [Fact]
    public void TheBigFolderRoles_AreThoseOfTheGlassHandoff()
    {
        var light = GlassColors.Light;
        Assert.Equal(0x14000000u, light.Hover);
        Assert.Equal(0x24000000u, light.Pressed);
        Assert.Equal(0xFF007AFFu, light.Select);
        Assert.Equal(0xFFFFFFFFu, light.OnSelect);
        Assert.Equal(0x38007AFFu, light.DropTint);   // Select at 22 %
        Assert.Equal(0xFFFFFFFFu, light.TilePlate);

        var dark = GlassColors.Dark;
        Assert.Equal(0x1FFFFFFFu, dark.Hover);
        Assert.Equal(0x3DFFFFFFu, dark.Pressed);
        Assert.Equal(0xFF0A84FFu, dark.Select);
        Assert.Equal(0xFFFFFFFFu, dark.OnSelect);
        Assert.Equal(0x4D0A84FFu, dark.DropTint);    // Select at 30 %
        Assert.Equal(0xFFE9E9EEu, dark.TilePlate);
    }

    [Fact]
    public void TheDialRolesDoNotDependOnTheSystemTheme()
    {
        var light = GlassColors.Light;
        var dark = GlassColors.Dark;

        Assert.Equal(
            new[] { light.DayFace, light.DayInk, light.DayFaint, light.NightFace, light.NightInk, light.NightFaint, light.Orange, light.Accent },
            new[] { dark.DayFace, dark.DayInk, dark.DayFaint, dark.NightFace, dark.NightInk, dark.NightFaint, dark.Orange, dark.Accent });
    }
}
