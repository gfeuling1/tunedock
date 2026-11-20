using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;

namespace TuneDock.Views;

internal static class InputHelper
{
    /// <summary>
    /// True if a mouse event came from a button or link, which handle their own clicks.
    /// Anything else on the card counts as "the card" (click to open / drag to move).
    /// </summary>
    public static bool IsFromInteractiveElement(object? source)
    {
        var current = source as DependencyObject;
        while (current is not null)
        {
            if (current is ButtonBase or Hyperlink) return true;
            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }
        return false;
    }
}
