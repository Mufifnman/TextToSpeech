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

    [Fact]
    public void IsValid_NeedsModifierAndKey()
    {
        Assert.False(new HotkeyBinding(ModifierKeys.None, Key.R).IsValid);
        Assert.False(new HotkeyBinding(ModifierKeys.Control, Key.None).IsValid);
        Assert.True(new HotkeyBinding(ModifierKeys.Control, Key.R).IsValid);
    }
}

public class HotkeySettingsTests
{
    [Fact]
    public void Defaults_AreValidAndDistinct()
    {
        var s = new HotkeySettings();
        var all = new[] { s.GetReadSelection(), s.GetSummonWindow(), s.GetReadRegion() };
        Assert.All(all, b => Assert.True(b.IsValid));
        Assert.Equal(3, all.Distinct().Count());
        Assert.False(s.ReadingSummonsWindow);
    }

    [Fact]
    public void BadText_FallsBackToDefault()
    {
        var s = new HotkeySettings { ReadSelection = "garbage", SummonWindow = "", ReadRegion = "Ctrl" };
        Assert.Equal(HotkeySettings.Default(nameof(HotkeySettings.ReadSelection)), s.GetReadSelection());
        Assert.Equal(HotkeySettings.Default(nameof(HotkeySettings.SummonWindow)), s.GetSummonWindow());
        Assert.Equal(HotkeySettings.Default(nameof(HotkeySettings.ReadRegion)), s.GetReadRegion());
    }

    [Fact]
    public void CustomBinding_IsUsed()
    {
        var s = new HotkeySettings { ReadRegion = "Ctrl+Shift+G" };
        Assert.Equal(new HotkeyBinding(ModifierKeys.Control | ModifierKeys.Shift, Key.G), s.GetReadRegion());
    }

    [Fact]
    public void Json_RoundTripsAndStaysHandEditable()
    {
        var s = new HotkeySettings { ReadSelection = "Ctrl+Shift+Y", ReadingSummonsWindow = true };
        string json = JsonSerializer.Serialize(s, HotkeySettings.WriteOptions);
        Assert.Contains("Ctrl+Shift+Y", json);
        var back = JsonSerializer.Deserialize<HotkeySettings>(json)!;
        Assert.Equal("Ctrl+Shift+Y", back.ReadSelection);
        Assert.True(back.ReadingSummonsWindow);
    }
}
