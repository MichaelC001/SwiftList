using System.Globalization;
using System.IO;
using System.Windows.Data;

namespace SwiftList.App.Views.QuickPanel;

/// <summary>The folder a result sits in, used as the panel's grouping key.</summary>
/// <remarks>
/// Computed from FullPath rather than read from ParentDir, which the panel has already repurposed to
/// carry the modified time. Grouping on a display string would also have been the wrong thing even if
/// it were free: two files in the same folder must land in the same group, and only the path guarantees
/// that.
/// </remarks>
public sealed class ResultDirectoryConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var path = value as string;
        if (string.IsNullOrEmpty(path)) return string.Empty;

        try
        {
            // A drive root has no parent, so it stands as its own group rather than collapsing into the
            // empty-string one alongside every other unparented path.
            return Path.GetDirectoryName(path) ?? path;
        }
        catch
        {
            // Malformed paths group under themselves instead of taking the whole panel down.
            return path;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>The last segment of a folder path, for the group header's leading name.</summary>
/// <remarks>
/// Falls back to the whole path when there is no last segment to take, which is what a drive root looks
/// like: "D:\" has no leaf, and showing the root itself reads better than an empty heading.
/// </remarks>
public sealed class FolderLeafNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var path = value as string;
        if (string.IsNullOrEmpty(path)) return string.Empty;

        try
        {
            var leaf = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return string.IsNullOrEmpty(leaf) ? path : leaf;
        }
        catch
        {
            return path;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
