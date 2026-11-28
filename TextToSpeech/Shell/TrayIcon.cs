using System.Drawing;
using System.Windows.Controls;
using H.NotifyIcon;

namespace TextToSpeech.Shell
{
    /// <summary>What the tray menu can do. The window supplies these.</summary>
    internal sealed record TrayActions(
        Action ShowWindow,
        Action PlayPause,
        Action Skip,
        Action Back,
        Action Stop,
        Action<bool> SetShowPopup,
        Action<bool> SetStartWithWindows,
        Action Exit);

    /// <summary>The notification-area icon: click to show the window, right-click for the menu.</summary>
    internal sealed class TrayIcon : IDisposable
    {
        private readonly TaskbarIcon icon;
        private readonly MenuItem popupItem;
        private readonly MenuItem startupItem;
        private readonly MenuItem playPauseItem;

        public TrayIcon(TrayActions actions, bool showPopup, bool startWithWindows)
        {
            var menu = new ContextMenu();
            menu.Items.Add(Item("Show window", actions.ShowWindow));
            menu.Items.Add(new Separator());
            playPauseItem = Item("Pause / Resume", actions.PlayPause);
            menu.Items.Add(playPauseItem);
            menu.Items.Add(Item("Back", actions.Back));
            menu.Items.Add(Item("Skip", actions.Skip));
            menu.Items.Add(Item("Stop", actions.Stop));
            menu.Items.Add(new Separator());
            popupItem = new MenuItem { Header = "Show reading popup", IsCheckable = true, IsChecked = showPopup };
            popupItem.Click += (_, _) => actions.SetShowPopup(popupItem.IsChecked);
            menu.Items.Add(popupItem);
            startupItem = new MenuItem { Header = "Start with Windows", IsCheckable = true, IsChecked = startWithWindows };
            startupItem.Click += (_, _) => actions.SetStartWithWindows(startupItem.IsChecked);
            menu.Items.Add(startupItem);
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Open log folder", OpenLogFolder));
            menu.Items.Add(Item("Exit", actions.Exit));

            icon = new TaskbarIcon
            {
                ToolTipText = "Text to Speech",
                ContextMenu = menu,
                Icon = Environment.ProcessPath != null ? Icon.ExtractAssociatedIcon(Environment.ProcessPath) : null,
            };
            icon.TrayLeftMouseUp += (_, _) => actions.ShowWindow();
            icon.ForceCreate();
        }

        /// <summary>Shows the folder with app.log in Explorer (created first so it always opens).</summary>
        private static void OpenLogFolder()
        {
            try
            {
                string folder = global::TextToSpeech.Core.Diagnostics.AppLog.Folder;
                System.IO.Directory.CreateDirectory(folder);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                global::TextToSpeech.Core.Diagnostics.AppLog.Warn("Could not open the log folder", ex);
            }
        }

        private static MenuItem Item(string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => action();
            return item;
        }

        /// <summary>Keeps the menu checks in step with the Settings window.</summary>
        public void SetChecks(bool showPopup, bool startWithWindows)
        {
            popupItem.IsChecked = showPopup;
            startupItem.IsChecked = startWithWindows;
        }

        public void SetState(string text, bool playing)
        {
            icon.ToolTipText = text;
            playPauseItem.Header = playing ? "Pause" : "Resume";
        }

        public void ShowHint(string title, string message)
        {
            try { icon.ShowNotification(title, message); } catch (Exception) { /* notifications may be turned off */ }
        }

        public void Dispose() => icon.Dispose();
    }
}
