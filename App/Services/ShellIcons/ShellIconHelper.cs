using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using SwiftList.App.Services.Plugin;
namespace SwiftList.App.Services.ShellIcons;

public static class ShellIconHelper
{
    private static readonly ConcurrentDictionary<string, ImageSource> _iconCache = new(StringComparer.OrdinalIgnoreCase);

    public static void ClearCache() => _iconCache.Clear();

    private const int MaxIconCacheEntries = 2000;

    // Coarse upper bound so a long session that touches many distinct file icons can't grow the cache
    // without limit. Entries are frozen bitmaps that reload cheaply, so clearing wholesale is fine.
    private static void EnforceCacheLimit()
    {
        if (_iconCache.Count > MaxIconCacheEntries)
            _iconCache.Clear();
    }

    public static ImageSource? GetIconFromCacheOnly(string path, bool isDir, out bool needsLoad)
    {
        needsLoad = false;
        EnforceCacheLimit();
        if (path == "__NO_RESULTS__") return null;
        if (path == "__SHOW_MORE__") return VectorIconFactory.ShowMore();

        var ext = isDir ? "::directory::" : Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext))
        {
            ext = "::unknown::";
        }

        var isVirtualItem = path.StartsWith("::") || path.StartsWith("shell:");
        var hasThumbnailProvider = !isDir && PluginManager.Instance.ThumbnailProviders.Any(p => p.CanProvideThumbnail(path, isDir));
        // Determine if it is a unique icon type
        var isUniqueIconType = (!isDir && (
            ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".ico", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".msc", StringComparison.OrdinalIgnoreCase)
        )) || isDir || isVirtualItem || hasThumbnailProvider;

        var cacheKey = isUniqueIconType ? path : ext;

        if (_iconCache.TryGetValue(cacheKey, out var cachedIcon))
        {
            return cachedIcon;
        }

        if (isUniqueIconType)
        {
            needsLoad = true;

            if (!isDir && hasThumbnailProvider)
            {
                // Return specific file type icon as placeholder instead of generic unknown icon
                var extPlaceholderKey = $"::placeholder:{ext}::";
                if (_iconCache.TryGetValue(extPlaceholderKey, out var extPlaceholder))
                {
                    return extPlaceholder;
                }
                var fetchedExtPlaceholder = GetIconForPath("dummy" + ext, false);
                if (fetchedExtPlaceholder != null)
                {
                    _iconCache[extPlaceholderKey] = fetchedExtPlaceholder;
                    return fetchedExtPlaceholder;
                }
            }

            // Return generic placeholder icon instantly
            var placeholderKey = isDir ? "::directory::" : "::unknown::";
            if (_iconCache.TryGetValue(placeholderKey, out var placeholder))
            {
                return placeholder;
            }

            // Fetch placeholder icon synchronously via USEFILEATTRIBUTES fast path
            var fetchedPlaceholder = GetIconForPath(isDir ? "dummy_folder" : "dummy_unknown", isDir);
            if (fetchedPlaceholder != null)
            {
                _iconCache[placeholderKey] = fetchedPlaceholder;
            }
            return fetchedPlaceholder;
        }
        else
        {
            // Non-unique types can be resolved synchronously (fast path, no disk access)
            return GetIconForPath(path, isDir);
        }
    }

    public static ImageSource? GetIconForPath(this string path, bool isDir)
    {
        EnforceCacheLimit();
        if (path == "__NO_RESULTS__")
            return null;

        if (path == "__SHOW_MORE__")
        {
            return VectorIconFactory.ShowMore();
        }

        var ext = isDir ? "::directory::" : Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext))
        {
            ext = "::unknown::";
        }

        // EXE, LNK, ICO, MSC, etc. have unique icons per file.
        // We use FullPath as cacheKey for these to avoid caching them under a single generic ".exe" key.
        // Also treat existing directories as unique icon types to extract their customized folder icons.
        var checkPath = path;
        var isVirtualItem = checkPath.StartsWith("::") || checkPath.StartsWith("shell:");
        var hasThumbnailProvider = !isDir && PluginManager.Instance.ThumbnailProviders.Any(p => p.CanProvideThumbnail(path, isDir));
        var isUniqueIconType = (!isDir && (
            ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".ico", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".msc", StringComparison.OrdinalIgnoreCase)
        )) || (isDir && Directory.Exists(checkPath)) || isVirtualItem || hasThumbnailProvider;

        var cacheKey = isUniqueIconType ? path : ext;

        if (_iconCache.TryGetValue(cacheKey, out var cachedIcon))
        {
            return cachedIcon;
        }

        // Check custom plugin thumbnail providers first
        var thumbnailProvider = PluginManager.Instance.ThumbnailProviders.FirstOrDefault(p => p.CanProvideThumbnail(path, isDir));
        if (thumbnailProvider != null)
        {
            try
            {
                var thumb = thumbnailProvider.GetThumbnail(path, ShellImageListInterop.PreferredPixels());
                if (thumb != null)
                {
                    _iconCache[cacheKey] = thumb;
                    return thumb;
                }
            }
            catch (Exception ex)
            {
                Core.Logger.Log($"[ShellIconHelper] Thumbnail provider '{thumbnailProvider.Name}' failed: {ex.Message}", Core.LogLevel.Error);
            }
        }

        try
        {
            var shfi = new ShellIconNativeMethods.SHFILEINFOW();

            if (!isDir && ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase) && File.Exists(checkPath))
            {
                var shortcutIcon = ShellIconShortcutResolver.TryGetShortcutTargetIcon(checkPath);
                if (shortcutIcon != null)
                {
                    _iconCache[cacheKey] = shortcutIcon;
                    return shortcutIcon;
                }
            }

            if (!isDir && ext.Equals(".msc", StringComparison.OrdinalIgnoreCase) && File.Exists(checkPath))
            {
                var mscIcon = ShellIconShortcutResolver.TryGetMscIcon(checkPath);
                if (mscIcon != null)
                {
                    _iconCache[cacheKey] = mscIcon;
                    return mscIcon;
                }
            }

            if (isVirtualItem || (isUniqueIconType && isDir && Directory.Exists(checkPath)))
            {
                // Safely load system shell icon by path instead of dangerous PIDL extraction
                // which triggers crashy third-party Shell extensions (e.g. CIconAndThumbnailOplockWrapper)
                var hiRes = ShellImageListInterop.TryGetIcon(checkPath, 0, 0);
                if (hiRes != null)
                {
                    _iconCache[cacheKey] = hiRes;
                    return hiRes;
                }
            }

            if (!isDir && (ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) || ext.Equals(".ico", StringComparison.OrdinalIgnoreCase)) && File.Exists(checkPath))
            {
                var exeIcon = ShellImageListInterop.ExtractHiRes(checkPath, 0);
                if (exeIcon != null)
                {
                    _iconCache[cacheKey] = exeIcon;
                    return exeIcon;
                }
            }

            if (isUniqueIconType && (File.Exists(checkPath) || Directory.Exists(checkPath)))
            {
                // For existing EXE/LNK/ICO (or folder fallback), load the actual unique embedded icon from the file path
                var hiRes = ShellImageListInterop.TryGetIcon(checkPath, 0, 0);
                if (hiRes != null)
                {
                    _iconCache[cacheKey] = hiRes;
                    return hiRes;
                }

                // Fallback using USEFILEATTRIBUTES to avoid crashy physical-path SHGetFileInfoW
                var fallbackPath = isDir ? "dummy_folder" : ext;
                var fallbackAttr = isDir ? ShellIconNativeMethods.FILE_ATTRIBUTE_DIRECTORY : ShellIconNativeMethods.FILE_ATTRIBUTE_NORMAL;
                var fallbackHiRes = ShellImageListInterop.TryGetIcon(fallbackPath, fallbackAttr, ShellIconNativeMethods.SHGFI_USEFILEATTRIBUTES);
                if (fallbackHiRes != null)
                {
                    _iconCache[cacheKey] = fallbackHiRes;
                    return fallbackHiRes;
                }
            }
            else
            {
                // Generic fallback for common extensions (highly performant, zero disk I/O)
                var flags = ShellIconNativeMethods.SHGFI_ICON | ShellIconNativeMethods.SHGFI_LARGEICON | ShellIconNativeMethods.SHGFI_USEFILEATTRIBUTES;
                var attributes = isDir ? ShellIconNativeMethods.FILE_ATTRIBUTE_DIRECTORY : ShellIconNativeMethods.FILE_ATTRIBUTE_NORMAL;
                var lookupPath = isDir ? "dummy_folder" : ext;

                var hiRes = ShellImageListInterop.TryGetIcon(lookupPath, attributes, ShellIconNativeMethods.SHGFI_USEFILEATTRIBUTES);
                if (hiRes != null)
                {
                    _iconCache[cacheKey] = hiRes;
                    return hiRes;
                }

                var res = ShellIconNativeMethods.SHGetFileInfoW(lookupPath, attributes, ref shfi, (uint)Marshal.SizeOf(shfi), flags);
                if (res != IntPtr.Zero && shfi.hIcon != IntPtr.Zero)
                {
                    try
                    {
                        var bitmapSource = Imaging.CreateBitmapSourceFromHIcon(
                            shfi.hIcon,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());
                        bitmapSource.Freeze();
                        _iconCache[cacheKey] = bitmapSource;
                        return bitmapSource;
                    }
                    finally
                    {
                        ShellIconNativeMethods.DestroyIcon(shfi.hIcon);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Core.Logger.Log($"[ShellIconHelper] Failed to get shell icon for {path}: {ex.Message}", Core.LogLevel.Warn);
        }

        return null;
    }

    public static ImageSource CreateVectorIcon(string pathData, string colorHexOrKey) => VectorIconFactory.Create(pathData, colorHexOrKey);
}
