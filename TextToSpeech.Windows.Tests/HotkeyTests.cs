using System.Text.Json;
using System.Windows.Input;
using TextToSpeech.Hotkeys;
using Xunit;

namespace TextToSpeech.Windows.Tests;

public class HotkeyBindingTests
{
    [Theory]
    [InlineData("Ctrl+Alt+R", ModifierKeys.Control | ModifierKeys.Alt, Key.R)]
    [InlineData("ctrl+shift+F5", ModifierKeys.Control | ModifierKeys.Shift, Key.F5)]
    [InlineData("Win+Alt+W", ModifierKeys.Windows | ModifierKeys.Alt, Key.W)]
    [InlineData(" Control + Alt + O ", ModifierKeys.Control | ModifierKeys.Alt, Key.O)]
    public void TryParse_ReadsModifiersAndKey(string text, ModifierKeys mods, Key key)
    {
        Assert.True(HotkeyBinding.TryParse(text, out var b));
        Assert.Equal(mods, b.Modifiers);
        Assert.Equal(key, b.Key);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("R")]               // no modifier
    [InlineData("Ctrl+Alt")]        // no key
    [InlineData("Ctrl+Alt+Banana")] // unknown key
    public void TryParse_RejectsInvalid(string? text)
    {
        Assert.False(HotkeyBinding.TryParse(text, out _));
    }

    [Fact]
    public void ToString_RoundTrips()
    {
        var b = new HotkeyBinding(ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt | ModifierKeys.Windows, Key.F12);
        Assert.Equal("Ctrl+Alt+Shift+Win+F12", b.ToString());
        Assert.True(HotkeyBinding.TryParse(b.ToString(), out var back));
        Assert.Equal(b, back);
    }
}

public class HotkeySettingsTests
{
    [Fact]
    public void Defaults_AreValidAndDistinct()
    {
        var s = new HotkeySettings();
        var all = new[] { s.GetReadSelection(), s.GetReadSelectionAndShow(), s.GetSummonWindow(), s.GetReadRegion() };
        Assert.All(all, b => Assert.True(b.IsValid));
        Assert.Equal(4, all.Distinct().Count());
    }

    [Fact]
    public void OldSavedCtrlAltR_IsResetSoItDoesNotCollide()
    {
        var s = new HotkeySettings { ReadSelection = "Ctrl+Alt+R" };
        s.FixOldDefaults();
        Assert.Equal("Alt+R", s.ReadSelection);

        var custom = new HotkeySettings { ReadSelection = "Ctrl+Shift+Y" };
        custom.FixOldDefaults();
        Assert.Equal("Ctrl+Shift+Y", custom.ReadSelection);
    }

    [Fact]
    public void BadText_FallsBackToDefault()
    {
        var s = new HotkeySettings { ReadSelection = "garbage", ReadSelectionAndShow = "Alt", SummonWindow = "", ReadRegion = "Ctrl" };
        Assert.Equal(HotkeySettings.Default(nameof(HotkeySettings.ReadSelection)), s.GetReadSelection());
        Assert.Equal(HotkeySettings.Default(nameof(HotkeySettings.ReadSelectionAndShow)), s.GetReadSelectionAndShow());
        Assert.Equal(HotkeySettings.Default(nameof(HotkeySettings.SummonWindow)), s.GetSummonWindow());
        Assert.Equal(HotkeySettings.Default(nameof(HotkeySettings.ReadRegion)), s.GetReadRegion());
    }

    [Fact]
    public void Json_RoundTripsAndStaysHandEditable()
    {
        var s = new HotkeySettings { ReadSelection = "Ctrl+Shift+Y", ReadSelectionAndShow = "Ctrl+Shift+U" };
        string json = JsonSerializer.Serialize(s, HotkeySettings.WriteOptions);
        Assert.Contains("Ctrl+Shift+Y", json);
        var back = JsonSerializer.Deserialize<HotkeySettings>(json)!;
        Assert.Equal("Ctrl+Shift+Y", back.ReadSelection);
        Assert.Equal("Ctrl+Shift+U", back.ReadSelectionAndShow);
    }
}

public class ResponsiveLayoutTests
{
    [Theory]
    [InlineData(500, true)]
    [InlineData(699, true)]
    [InlineData(700, false)]
    [InlineData(780, false)]   // the default window is big: no step buttons
    [InlineData(1400, false)]
    public void SpeedStepButtons_ShowOnlyWhenTheWindowIsSmall(double width, bool shown)
    {
        Assert.Equal(shown, TextToSpeech.ResponsiveLayout.ShowSpeedStepButtons(width));
    }
}
