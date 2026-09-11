using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AtomicDriftTuner;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

internal static partial class Program
{
    private static void CheckThemeCoverage(string repo, string output)
    {
        var count = 0;
        void Check(bool value, string message)
        {
            if (!value) throw new Exception("Theme regression: " + message);
            count++;
        }
        static string Hex(Brush brush) => ThemeService.ToHex(((SolidColorBrush)brush).Color);
        static object? Call(object target, string method, params object?[] args) =>
            target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args);

        var legacy = JsonSerializer.Deserialize<ThemeSettings>("{\"PrimaryText\":\"#112233\",\"Accent\":\"#FFCC00\"}")!;
        Check(legacy.SectionHeading == "#112233" && legacy.SliderFill == "#FFCC00", "legacy themes must derive missing roles from their own colors");
        foreach (var preset in ThemeCatalog.Presets)
        {
            ThemeService.Validate(preset);
            Check(ThemeService.ContrastRatio(preset.SectionHeading, preset.Surface) >= 4.5, "preset heading contrast: " + preset.PresetName);
        }
        var custom = ThemeCatalog.Clone(ThemeCatalog.Presets[0]);
        foreach (var option in AdditionalThemeColors.All) option.Set(custom, "#D0A050");
        var clone = ThemeCatalog.Clone(custom);
        var roundtrip = JsonSerializer.Deserialize<ThemeSettings>(JsonSerializer.Serialize(custom))!;
        foreach (var option in AdditionalThemeColors.All)
        {
            Check(option.Get(clone) == "#D0A050" && option.Get(roundtrip) == "#D0A050", option.Name + " lost during clone/save");
        }
        ThemeService.Apply(custom);
        foreach (var option in AdditionalThemeColors.All)
            Check(Hex((Brush)Application.Current.Resources[option.ResourceKey]) == "#D0A050", "resource mapping missing: " + option.Name);
        var before = Application.Current.Resources["SectionHeadingBrush"];
        var bad = ThemeCatalog.Clone(custom); bad.ScrollThumb = "not a color";
        try { ThemeService.Apply(bad); throw new Exception("Invalid theme was accepted"); }
        catch (InvalidOperationException) { }
        Check(ReferenceEquals(before, Application.Current.Resources["SectionHeadingBrush"]), "invalid edit partially changed active palette");
        custom = ThemeCatalog.Clone(ThemeCatalog.Presets[0]);
        custom.SectionHeading = "#FFE06A"; custom.FieldLabel = "#90EE90";
        custom.TabSelectedText = "#091019"; custom.ExpanderGlyph = "#FF00FF";
        ThemeService.Apply(custom);

        var azom = LoadContent(Path.Combine(repo, "src/AtomicDriftTuner/AzomSettingsWindow.xaml"));
        var azomHost = new Window { Content = azom };
        var tabs = Descendants(azom).OfType<TabControl>().Single();
        tabs.SelectedIndex = 2;
        var size = new Size(1400, 900);
        Layout(azom, size);
        var heading = Descendants(azom).OfType<TextBlock>().First(t => t.Text.StartsWith("CORE SETTINGS"));
        Check(Hex(heading.Foreground) == custom.SectionHeading, "Core Settings inherited selected-tab text");
        var checkbox = Descendants(azom).OfType<CheckBox>().First(c => c.Content is TextBlock);
        custom.CheckBoxText = "#BB55FF"; ThemeService.Apply(custom);
        Check(Hex(((TextBlock)checkbox.Content).Foreground) == custom.CheckBoxText, "wrapped checkbox text ignores its own color");
        var selected = (TabItem)tabs.Items[2]; selected.ApplyTemplate();
        var header = (ContentPresenter)selected.Template.FindName("TabHeaderPresenter", selected);
        Check(Hex((Brush)header.GetValue(System.Windows.Documents.TextElement.ForegroundProperty)) == custom.TabSelectedText, "selected tab label lost its own color");
        Render(azom, size, Path.Combine(output, "Theme-Core-Settings-Custom.png"));
        custom.SectionHeading = "#7EDBFF"; ThemeService.Apply(custom); Layout(azom, size);
        Check(Hex(heading.Foreground) == "#7EDBFF", "open heading did not update dynamically");
        ThemeService.Apply(ThemeCatalog.Presets[0]); Layout(azom, size);
        Check(Hex(heading.Foreground) == ThemeCatalog.Presets[0].PrimaryText, "default restore left a custom heading behind");
        Render(azom, size, Path.Combine(output, "Theme-Core-Settings-Default.png"));

        var car = LoadContent(Path.Combine(repo, "src/AtomicDriftTuner/CarSetupWindow.xaml"));
        var carHost = new Window { Content = car };
        var expander = Descendants(car).OfType<Expander>().First();
        expander.IsExpanded = true; ThemeService.Apply(custom); Layout(car, size);
        var toggle = (System.Windows.Controls.Primitives.ToggleButton)expander.Template.FindName("HeaderToggle", expander);
        toggle.ApplyTemplate();
        var arrow = (System.Windows.Shapes.Path)toggle.Template.FindName("Arrow", toggle);
        Check(Hex(arrow.Stroke) == custom.ExpanderGlyph, "expander arrow ignores palette");
        toggle.IsChecked = false; Check(!expander.IsExpanded, "themed expander no longer collapses");
        toggle.IsChecked = true; Check(expander.IsExpanded, "themed expander no longer expands");
        Check(Descendants(car).OfType<TextBlock>().Where(t => t.Text == "Front-end bite").All(t => Hex(t.Foreground) == custom.SectionHeading), "expander labels inherit a system color");
        Render(car, size, Path.Combine(output, "Theme-Car-Behavior.png"));

        var store = new AppSettingsStore();
        var directory = Path.Combine(output, "isolated-theme-settings"); Directory.CreateDirectory(directory);
        foreach (var (field, value) in new[] { ("_directory", directory), ("_path", Path.Combine(directory, "settings.json")), ("_backupPath", Path.Combine(directory, "settings.backup.json")) })
            typeof(AppSettingsStore).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(store, value);
        var initial = store.Load(); initial.Theme = ThemeCatalog.Clone(ThemeCatalog.Presets[0]); store.Save(initial);
        var editor = new ThemeWindow(store);
        var boxes = (Dictionary<string, TextBox>)typeof(ThemeWindow).GetField("_additionalBoxes", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor)!;
        Check(boxes.Count == AdditionalThemeColors.All.Count, "some roles have no UI editor");
        foreach (var option in AdditionalThemeColors.All)
        {
            Check(System.Windows.Automation.AutomationProperties.GetName(boxes[option.Name]) == option.Label, "missing accessible editor label");
            Check(Call(editor, "FindTargetBox", option.EditorName) == boxes[option.Name], "color wheel cannot reach " + option.Name);
        }
        boxes["SectionHeading"].Text = "#FFAACC";
        Check(Hex((Brush)Application.Current.Resources["SectionHeadingBrush"]) == "#FFAACC", "typing a valid color did not preview");
        Check(store.Load().Theme.SectionHeading != "#FFAACC", "preview wrote settings before save");
        boxes["SectionHeading"].Text = "#XX";
        Check(Hex((Brush)Application.Current.Resources["SectionHeadingBrush"]) == "#FFAACC", "partial input broke active palette");
        boxes["SectionHeading"].Text = "#FFAACC";
        Call(editor, "SaveTheme_Click", editor, new RoutedEventArgs());
        Check(store.Load().Theme.SectionHeading == "#FFAACC", "new color did not survive actual settings store save");
        boxes["SectionHeading"].Text = "#00FF00";
        editor.Close();
        Check(Hex((Brush)Application.Current.Resources["SectionHeadingBrush"]) == "#FFAACC", "closing unsaved edits did not restore saved color");
        var reopened = new ThemeWindow(store);
        var reopenedBoxes = (Dictionary<string, TextBox>)typeof(ThemeWindow).GetField("_additionalBoxes", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(reopened)!;
        Check(reopenedBoxes["SectionHeading"].Text == "#FFAACC", "reopened editor lost saved value");
        var content = (FrameworkElement)reopened.Content;
        Layout(content, new Size(1280, 1000)); Render(content, new Size(1280, 1000), Path.Combine(output, "Theme-Appearance-Editors.png"));
        reopened.Close();

        var probe = new Button { Content = "Disabled primary action", IsEnabled = false };
        probe.SetResourceReference(Control.ForegroundProperty, "AccentTextBrush");
        probe.SetResourceReference(Control.BackgroundProperty, "AccentBrush");
        var probeHost = new Window { Content = probe }; Layout(probe, new Size(220, 50));
        var buttonContent = (ContentPresenter)probe.Template.FindName("ButtonContent", probe);
        Check(Hex((Brush)buttonContent.GetValue(System.Windows.Documents.TextElement.ForegroundProperty)) == store.Load().Theme.DisabledText, "explicit primary text defeats disabled theme");
        probeHost.Close();

        var remote = RemoteWebApp.Render(new ThemeSettings { AppBackground = "#80112233", SectionHeading = "#FFAA00" });
        Check(remote.Contains("--bg:#11223380;") && remote.Contains("--heading:#FFAA00FF;"), "remote palette or ARGB conversion is wrong");
        try { RemoteWebApp.Render(new ThemeSettings { StatusError = "</style>" }); throw new Exception("Unsafe remote color accepted"); }
        catch (InvalidOperationException) { count++; }
        ThemeService.Apply(ThemeCatalog.Presets[0]);
        azomHost.Close(); carHost.Close();
        Progress($"PASS {count} theme assertions: legacy migration, all additional roles, dynamic headings, selected tabs, glyphs, editing, save/reopen, cancel, remote palette.");
    }
}
