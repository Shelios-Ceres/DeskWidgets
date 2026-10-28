namespace DeskWidgets.Widgets.Folder;

/// <summary>
/// The icon caches' one rule: at <c>max</c> entries a cache starts over. Their keys hold the picture itself, and a picture that is
/// replaced (a file rewritten, an icon fetched again after the icon service let it go) would otherwise stay in the cache for good.
/// </summary>
internal static class BoundedCache
{
    public static TValue Put<TKey, TValue>(Dictionary<TKey, TValue> cache, TKey key, TValue value, int max)
        where TKey : notnull
    {
        if (cache.Count >= max)
            cache.Clear();
        cache[key] = value;
        return value;
    }
}
