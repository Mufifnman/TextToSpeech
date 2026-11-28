using System.Configuration;
using System.Data;
using System.Windows;
using System.Windows.Interop;
using TextToSpeech.Core.Diagnostics;
using TextToSpeech.Core.Shell;
using TextToSpeech.Shell;

namespace TextToSpeech
{
    /// <summary>
    /// Interaction logic for App.xaml
    ///
    /// Always-on behavior lives here: one instance only, an optional start hidden in the tray (<c>--background</c>,
    /// used by Start with Windows), and an explicit exit so closing the window can hide it instead.
    /// </summary>
    public partial class App : Application
    {
        private SingleInstance? instance;

        /// <summary>True once the app is really going away (tray Exit or Windows shutting down): the window must let itself close.</summary>
        public static bool IsExiting { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            // Record crashes and stray errors in the log (they do not change how the app reacts to them).
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
                AppLog.Error("Unhandled exception" + (args.IsTerminating ? " (app is closing)" : ""), args.ExceptionObject as Exception);
            DispatcherUnhandledException += (_, args) => AppLog.Error("Unhandled UI exception", args.Exception);
            TaskScheduler.UnobservedTaskException += (_, args) => AppLog.Warn("Unobserved task exception", args.Exception);
            AppLog.Info("Started, version " + (System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "?")
                        + (e.Args.Length > 0 ? ", args: " + string.Join(' ', e.Args) : ""));

            instance = SingleInstance.TryAcquire();
            if (instance == null)
            {
                // Already running: wake that copy (it shows its window) and leave.
                SingleInstance.SignalExisting();
                Shutdown();
                return;
            }

            // The app lives in the tray, so the window closing must not end the process; Exit does.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Theme brushes must exist before the first window is created.
            ThemeManager.Start();
            base.OnStartup(e);

            var window = new MainWindow();
            MainWindow = window;
            if (LaunchOptions.Parse(e.Args).Background)
            {
                // Create the window handle (hotkeys need it) without showing anything.
                new WindowInteropHelper(window).EnsureHandle();
            }
            else
            {
                window.Show();
            }

            instance.ShowRequested += () => Dispatcher.BeginInvoke(window.ShowFullWindow);
            instance.Listen();
        }

        /// <summary>Really exit (the window's close handler lets go once this is set).</summary>
        public static void ExitApp()
        {
            IsExiting = true;
            Current.Shutdown();
        }

        protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
        {
            IsExiting = true;   // Windows is logging off or shutting down: do not hide to the tray
            base.OnSessionEnding(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            AppLog.Info("Exited");
            instance?.Dispose();
            base.OnExit(e);
        }
    }
}
