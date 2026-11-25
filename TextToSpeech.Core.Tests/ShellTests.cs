using TextToSpeech.Core.Shell;

namespace TextToSpeech.Core.Tests
{
    public class LaunchOptionsTests
    {
        [Theory]
        [InlineData("--background", true)]
        [InlineData("--BACKGROUND", true)]
        [InlineData("--other", false)]
        public void Background_flag_is_recognized(string arg, bool expected) =>
            Assert.Equal(expected, LaunchOptions.Parse(new[] { arg }).Background);

        [Fact]
        public void No_arguments_means_a_normal_start()
        {
            Assert.False(LaunchOptions.Parse(Array.Empty<string>()).Background);
            Assert.False(LaunchOptions.Parse(null).Background);
        }

        [Fact]
        public void Startup_command_quotes_the_path_and_adds_the_flag()
        {
            Assert.Equal("\"C:\\Program Files\\TTS\\TextToSpeech.exe\" --background", StartupCommand.Build(@"C:\Program Files\TTS\TextToSpeech.exe"));
            Assert.Equal("\"C:\\a.exe\" --background", StartupCommand.Build("\"C:\\a.exe\""));
            Assert.Throws<ArgumentException>(() => StartupCommand.Build(" "));
        }

        [Fact]
        public void Matches_detects_a_moved_exe()
        {
            string stored = StartupCommand.Build(@"C:\old\TextToSpeech.exe");
            Assert.True(StartupCommand.Matches(stored, @"c:\OLD\texttospeech.exe"));
            Assert.False(StartupCommand.Matches(stored, @"C:\new\TextToSpeech.exe"));
            Assert.False(StartupCommand.Matches(null, @"C:\new\TextToSpeech.exe"));
        }
    }

    public class PopupPolicyTests
    {
        private DateTime now = new(2026, 11, 25, 12, 0, 0, DateTimeKind.Utc);
        private readonly PopupPolicy policy;

        public PopupPolicyTests() => policy = new PopupPolicy(() => now);

        [Fact]
        public void Hidden_until_reading_starts()
        {
            Assert.False(policy.IsVisible(false, false));
            policy.ReadingChanged(true, true);
            Assert.True(policy.IsVisible(true, true));
        }

        [Fact]
        public void Not_shown_when_disabled_or_when_the_full_window_is_up()
        {
            policy.ReadingChanged(true, true);
            policy.Enabled = false;
            Assert.False(policy.IsVisible(true, true));
            policy.Enabled = true;
            policy.FullWindowShown = true;
            Assert.False(policy.IsVisible(true, true));
        }

        [Fact]
        public void With_the_unfocused_setting_the_popup_shows_beside_an_open_window_that_is_not_in_focus()
        {
            policy.ReadingChanged(true, true);
            policy.FullWindowShown = true;
            policy.ShowWhenWindowUnfocused = true;
            policy.FullWindowFocused = false;
            Assert.True(policy.IsVisible(true, true));
            policy.FullWindowFocused = true;
            Assert.False(policy.IsVisible(true, true));
        }

        [Fact]
        public void The_popup_stays_up_while_settings_are_open_even_when_nothing_is_reading()
        {
            policy.FullWindowShown = true;
            policy.KeepForSettings = true;
            Assert.True(policy.IsVisible(false, false));
            policy.Enabled = false;
            Assert.False(policy.IsVisible(false, false));
        }

        [Fact]
        public void Stays_open_while_paused_by_default_and_hides_when_the_setting_is_off()
        {
            policy.ReadingChanged(true, true);
            policy.ReadingChanged(false, true);
            Assert.True(policy.IsVisible(false, true));
            policy.KeepOpenWhenPaused = false;
            Assert.False(policy.IsVisible(false, true));
            policy.ReadingChanged(true, true);   // resume brings it back
            Assert.True(policy.IsVisible(true, true));
        }

        [Fact]
        public void After_everything_is_read_it_lingers_for_the_grace_period_then_hides()
        {
            policy.ReadingChanged(true, true);
            policy.ReadingChanged(false, false);
            Assert.True(policy.IsVisible(false, false));
            Assert.NotNull(policy.TimeUntilRecheck());
            now += PopupPolicy.Grace + TimeSpan.FromMilliseconds(1);
            Assert.False(policy.IsVisible(false, false));
            Assert.Null(policy.TimeUntilRecheck());
        }

        [Fact]
        public void With_auto_hide_off_it_stays_after_reading_until_dismissed()
        {
            policy.AutoHide = false;
            policy.ReadingChanged(true, true);
            policy.ReadingChanged(false, false);
            now += TimeSpan.FromMinutes(5);
            Assert.True(policy.IsVisible(false, false));
            policy.Dismiss();
            Assert.False(policy.IsVisible(false, false));
        }

        [Fact]
        public void New_text_during_the_grace_keeps_it_open()
        {
            policy.ReadingChanged(true, true);
            policy.ReadingChanged(false, false);
            now += TimeSpan.FromSeconds(1);
            policy.ReadingChanged(true, true);
            now += TimeSpan.FromSeconds(10);
            Assert.True(policy.IsVisible(true, true));
        }

        [Fact]
        public void Dismissed_stays_away_until_reading_starts_again()
        {
            policy.ReadingChanged(true, true);
            policy.Dismiss();
            policy.ReadingChanged(true, true);        // still playing, no new start
            Assert.False(policy.IsVisible(true, true));
            policy.ReadingChanged(false, true);       // paused
            policy.ReadingChanged(true, true);        // started again
            Assert.True(policy.IsVisible(true, true));
        }

        [Fact]
        public void Clearing_hides_it()
        {
            policy.AutoHide = false;
            policy.ReadingChanged(true, true);
            policy.Cleared();
            Assert.False(policy.IsVisible(false, false));
        }

        [Fact]
        public void An_open_handoff_keeps_it_up_with_nothing_left_to_read()
        {
            policy.ReadingChanged(true, true);
            policy.ReadingChanged(false, false);
            policy.HandoffOpen = true;
            now += TimeSpan.FromMinutes(1);
            Assert.True(policy.IsVisible(false, false));
            policy.HandoffOpen = false;
            Assert.False(policy.IsVisible(false, false));
        }
    }
}
