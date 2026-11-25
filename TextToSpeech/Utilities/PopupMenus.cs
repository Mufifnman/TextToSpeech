using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace TextToSpeech
{
    /// <summary>Shared helpers for the small pop-up menus opened from buttons (voice, filters, OCR engine).</summary>
    internal static class PopupMenus
    {
        /// <summary>Opens <paramref name="menu"/> next to <paramref name="target"/>.</summary>
        public static void Open(ContextMenu menu, UIElement target, PlacementMode placement)
        {
            menu.PlacementTarget = target;
            menu.Placement = placement;
            menu.IsOpen = true;
        }

        /// <summary>A bold, unclickable title at the top of a menu so people know what they are choosing.</summary>
        public static void AddHeading(ContextMenu menu, string title)
        {
            menu.Items.Add(new MenuItem { Header = title, IsEnabled = false, FontWeight = FontWeights.SemiBold });
            menu.Items.Add(new Separator());
        }
    }
}
