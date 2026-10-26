namespace Armux.Core.Tests;

[TestClass]
public sealed class KeymapAndSettingsTests
{
    static Keymap Map(Dictionary<string, string>? overrides = null)
    {
        var k = new Keymap(overrides ?? []);
        k.AddDefault("Split Right", "Ctrl+Shift+D");
        k.AddDefault("New Workspace", "Ctrl+Shift+N");
        k.AddDefault("Settings…", "Ctrl+Shift+,");
        return k;
    }

    [TestMethod]
    public void OverridesReplaceOrRemoveDefaults()
    {
        var k = Map(new() { ["Split Right"] = "alt+shift+d", ["New Workspace"] = "" });
        Assert.AreEqual("Alt+Shift+D", k.Effective("Split Right"));
        Assert.AreEqual("", k.Effective("New Workspace"));
        Assert.AreEqual("Ctrl+Shift+,", k.Effective("Settings…"));
        Assert.IsTrue(k.IsCustom("Split Right"));
        Assert.IsFalse(k.IsCustom("Settings…"));
    }

    [TestMethod]
    public void ConflictsAndResetToDefault()
    {
        var k = Map();
        CollectionAssert.AreEqual(new[] { "New Workspace" }, k.Conflicts("Split Right", "ctrl+shift+n").ToArray());
        var saved = k.With("Split Right", "Ctrl+Shift+X");
        Assert.AreEqual("Ctrl+Shift+X", saved["Split Right"]);
        var back = new Keymap(saved);
        back.AddDefault("Split Right", "Ctrl+Shift+D");
        Assert.IsFalse(back.With("Split Right", "Ctrl+Shift+D").ContainsKey("Split Right"));   // setting the default drops the override
        Assert.IsFalse(back.With("Split Right", null).ContainsKey("Split Right"));
    }

    [TestMethod]
    public void ChordDisplayNames()
    {
        Assert.AreEqual("Ctrl+PageDown", Chord.Display("Ctrl+PAGEDOWN"));
        Assert.AreEqual("Ctrl+Alt+Shift+Enter", Chord.Display("shift+ctrl+alt+enter"));
        Assert.AreEqual("", Chord.Display(""));
    }

    [TestMethod]
    public void NewSettingsNormalizeAndRoundTrip()
    {
        var s = new Settings
        {
            Theme = "HIGH-CONTRAST", CursorStyle = "beam", SearchEngine = "google", ScrollbackLines = 10,
            LineHeight = 9, NewTabDirectory = "elsewhere", WorkspaceIndicator = "underline",
            Keybindings = new() { ["Split Right"] = "Ctrl+Shift+X" },
        }.Normalized();
        Assert.AreEqual("high-contrast", s.Theme);
        Assert.AreEqual("block", s.CursorStyle);
        Assert.AreEqual("google", s.SearchEngine);
        Assert.AreEqual(Settings.MinScrollback, s.ScrollbackLines);
        Assert.AreEqual(2.0, s.LineHeight);
        Assert.AreEqual("workspace", s.NewTabDirectory);

        var dir = Path.Combine(Path.GetTempPath(), "armux-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "settings.json");
        s.Save(path);
        Assert.AreEqual(s, Settings.Load(path));   // equality includes the shortcut overrides
        Assert.AreNotEqual(s, s with { Keybindings = new() { ["Split Right"] = "Ctrl+Shift+Y" } });
        Directory.Delete(dir, true);
    }
}
