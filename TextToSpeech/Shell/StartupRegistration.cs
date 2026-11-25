using Microsoft.Win32;
using TextToSpeech.Core.Shell;

namespace TextToSpeech.Shell
{
    /// <summary>"Start with Windows": a value in the current user's Run key (no admin rights needed).</summary>
    internal static class StartupRegistration
    {
        private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

        private static string ExePath => Environment.ProcessPath ?? "";

        public static bool IsEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
                return ExePath != "" && StartupCommand.Matches(key?.GetValue(StartupCommand.ValueName) as string, ExePath);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Writes or removes the value. Safe to call on every start: it also repairs a path left behind by a moved exe.</summary>
        public static void Apply(bool enabled)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
                if (enabled && ExePath != "")
                {
                    key.SetValue(StartupCommand.ValueName, StartupCommand.Build(ExePath));
                }
                else
                {
                    key.DeleteValue(StartupCommand.ValueName, throwOnMissingValue: false);
                }
            }
            catch (Exception)
            {
                // Group policy or a locked registry: the setting simply has no effect.
            }
        }
    }
}
