using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using AtomicDriftTuner.Controls;

internal static class Program
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static int _checks;

    // Application queues OnStartup in its constructor, even without Run().
    // Pumping the dispatcher for layout must not launch ADT's MainWindow,
    // services, first-run dialogs, or user settings. Load the real XAML resources
    // into a plain Application; never construct the production App here.
    private sealed class LayoutTestApplication : Application
    {
        public bool StartupIntercepted { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            StartupIntercepted = true;
            Progress("Intercepted application startup; production startup is disabled for layout tests");
            // No production startup or event handlers are needed by this harness.
        }
    }

    private static void Progress(string message)
    {
        Console.WriteLine($"[{DateTimeOffset.UtcNow:O}] {message}");
        Console.Out.Flush();
    }

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Progress($"START layout tests; process {Environment.ProcessId}; {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
            var repo = Path.GetFullPath(args.FirstOrDefault() ?? ".");
            var output = Path.GetFullPath(args.Skip(1).FirstOrDefault() ?? Path.Combine(repo, "artifacts", "layout-checks"));
            Directory.CreateDirectory(output);
            Progress($"Repository: {repo}; diagnostics: {output}");
            Progress("Creating WPF application");
            var app = new LayoutTestApplication();
            Progress("Initializing WPF resources");
            app.Resources = LoadApplicationResources(Path.Combine(repo, "src", "AtomicDriftTuner", "App.xaml"));
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Progress("Verifying isolated application startup");
            app.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            if (!app.StartupIntercepted || app.MainWindow != null || app.Windows.Count != 0)
                throw new Exception("Layout harness must intercept startup without creating application windows.");
            if (!app.Resources.Contains("AppBackgroundBrush"))
                throw new Exception("Production application resources were not loaded.");
            Progress("PASS startup isolation: no application windows; production resources loaded");
            Progress("Checking adaptive panel");
            CheckAdaptivePanel();
            var cases = new Dictionary<string, string[]>
            {
                ["CarSetupWindow"] = ["SaveBehaviorButton", "SaveGeneratedButton"],
                ["AzomSettingsWindow"] = ["SavePreferencesButton"],
                ["SetupWizardWindow"] = ["SaveButton", "CloseWithoutSavingButton"],
                ["TelemetryWindow"] = ["RecordButton", "StopButton", "SaveButton"],
                ["TuningAssistantWindow"] = ["ApplyCalibrationButton", "OpenSetupButton"],
                ["ShareCodeWindow"] = ["ShareCopyActions"],
                ["RemoteControlWindow"] = ["CloseButton"],
                ["DiagnosticsWindow"] = ["RefreshButton", "CopyButton", "ExportButton"],
                ["ThemeWindow"] = ["ApplyPreviewButton", "SaveThemeButton", "ResetThemeButton", "CloseButton"],
                ["UpdatesWindow"] = ["OpenReleaseButton", "CloseButton"],
            };
            foreach (var (name, buttons) in cases)
            {
                Progress($"Loading {name}");
                var root = LoadContent(Path.Combine(repo, "src", "AtomicDriftTuner", name + ".xaml"));
                Progress($"Seeding {name}");
                Seed(root);
                foreach (var size in new[] { new Size(430, 300), new Size(430, 430), new Size(680, 900), new Size(1200, 540), new Size(1800, 900) })
                {
                    Progress($"Checking {name} at {size.Width}x{size.Height}");
                    Layout(root, size);
                    foreach (var button in buttons) AssertVisible(root, button, size);
                    if (name == "TelemetryWindow")
                        foreach (var field in new[] { "DriverBox", "ConditionsBox", "TuneInUseCheck", "TestedChangeBox", "CompareSavedRunButton" })
                            AssertReachableByScrolling(root, "TelemetryBodyScroll", field, size);
                    if (name == "SetupWizardWindow")
                        foreach (var field in new[] { "InterviewDriverBox", "SimHubChoiceBox", "AzomChoiceBox", "UseLiveGuidanceBox", "CheckGuidedConnectionButton" })
                            AssertReachableByScrolling(root, "SetupBodyScroll", field, size);
                    foreach (var tab in Descendants(root).OfType<TabControl>().ToArray())
                    {
                        for (int i = 0; i < tab.Items.Count; i++)
                        {
                            tab.SelectedIndex = i;
                            Layout(root, size);
                            if (name == "TuningAssistantWindow")
                            {
                                var header = ((TabItem)tab.Items[i]).Header?.ToString();
                                if (header == "Recommendations") AssertReachableByScrolling(root, "AssistantBodyScroll", "TestRecommendationButton", size);
                                if (header == "Before / After") AssertReachableByScrolling(root, "AssistantBodyScroll", "BaselineSessionBox", size);
                                if (header == "Tune & Run History")
                                {
                                    foreach (var field in new[] { "DriverRatingBox", "DriverNotesBox", "SaveReviewButton", "ReviewHistoryBox" })
                                        AssertReachableByScrolling(root, "AssistantBodyScroll", field, size);
                                    if (size.Width is 430 or 1800) Render(root, size, Path.Combine(output, $"RunHistory-{size.Width}-{size.Height}.png"));
                                }
                            }
                            if (name == "AzomSettingsWindow") AssertVisible(root, "SavePreferencesButton", size);
                            if (name == "ShareCodeWindow")
                                AssertVisible(root, i == 0 ? "ShareCopyActions" : "ShareImportActions", size);
                        }
                        tab.SelectedIndex = 0;
                    }
                    Layout(root, size);
                    if (size.Width is 430 or 1800) Render(root, size, Path.Combine(output, $"{name}-{size.Width}-{size.Height}.png"));
                }
                Progress($"PASS {name}: narrow, portrait, short landscape, ultrawide");
            }
            Progress("Loading dashboard");
            var main = LoadContent(Path.Combine(repo, "src", "AtomicDriftTuner", "MainWindow.xaml"));
            Seed(main);
            foreach (var size in new[] { new Size(560, 480), new Size(800, 900), new Size(1280, 720), new Size(2560, 1080) })
            {
                Progress($"Checking dashboard at {size.Width}x{size.Height}");
                var sidebar = (FrameworkElement)main.FindName("SidebarPanel");
                var column = (ColumnDefinition)main.FindName("SidebarColumn");
                var divider = (FrameworkElement)main.FindName("SidebarDivider");
                sidebar.Visibility = divider.Visibility = size.Width < 900 ? Visibility.Collapsed : Visibility.Visible;
                column.MinWidth = size.Width < 900 ? 0 : 180;
                column.Width = new GridLength(size.Width < 900 ? 0 : 220);
                ((ColumnDefinition)main.FindName("SidebarDividerColumn")).Width = new GridLength(size.Width < 900 ? 0 : 5);
                ((FrameworkElement)main.FindName("BrandCaption")).Visibility = size.Width < 700 ? Visibility.Collapsed : Visibility.Visible;
                ((FrameworkElement)main.FindName("HeaderVersionBadge")).Visibility = size.Width < 1100 ? Visibility.Collapsed : Visibility.Visible;
                Layout(main, size);
                AssertVisible(main, "SaveProfileButton", size);
                ((CheckBox)main.FindName("GuidedReadyCheck")).Visibility = Visibility.Visible;
                foreach (var field in new[] { "GuidedDriverBox", "GuidedNextButton", "GuidedCheckButton", "GuidedReadyCheck" })
                    AssertReachableByScrolling(main, "DashboardScroll", field, size);
                var scroll = (ScrollViewer)main.FindName("DashboardScroll");
                scroll.ScrollToEnd(); Layout(main, size); AssertVisible(main, "SaveProfileButton", size);
                scroll.ScrollToHome(); Layout(main, size);
                Render(main, size, Path.Combine(output, $"Dashboard-{size.Width}.png"));
            }
            Progress($"PASS dashboard action bar at all sizes, including after scrolling. {_checks} geometry assertions passed.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static ResourceDictionary LoadApplicationResources(string path)
    {
        var application = XElement.Load(path);
        var ns = application.Name.Namespace;
        var resources = application.Element(ns + "Application.Resources")
            ?? throw new Exception("Application.Resources is missing from production App.xaml.");
        var dictionary = new XElement(ns + "ResourceDictionary",
            application.Attributes().Where(a => a.IsNamespaceDeclaration),
            resources.Elements());
        return (ResourceDictionary)XamlReader.Parse(dictionary.ToString(), new ParserContext
        { BaseUri = new Uri("pack://application:,,,/AtomicDriftTuner;component/") });
    }

    // Load the production markup without its business event handlers. This
    // measures real WPF controls/styles without opening services or user data.
    private static FrameworkElement LoadContent(string path)
    {
        Progress($"Reading markup: {Path.GetFileName(path)}");
        var xml = XElement.Load(path);
        Progress("Discovering and removing business event handlers");
        var eventNames = new[] { typeof(Window).Assembly, typeof(UIElement).Assembly, typeof(AdaptivePanel).Assembly }
            .Distinct().SelectMany(a => a.GetTypes()).Where(t => typeof(DependencyObject).IsAssignableFrom(t))
            .SelectMany(t => t.GetEvents()).Select(e => e.Name).ToHashSet();
        foreach (var node in xml.DescendantsAndSelf())
        {
            node.Attribute(X + "Class")?.Remove();
            foreach (var attribute in node.Attributes().ToArray())
            {
                if (!attribute.IsNamespaceDeclaration && eventNames.Contains(attribute.Name.LocalName)) attribute.Remove();
            }
        }
        var markup = xml.ToString().Replace("clr-namespace:AtomicDriftTuner.Controls", "clr-namespace:AtomicDriftTuner.Controls;assembly=AtomicDriftTuner");
        Progress("Parsing WPF markup");
        var window = (Window)XamlReader.Parse(markup, new ParserContext
        { BaseUri = new Uri("pack://application:,,,/AtomicDriftTuner;component/") });
        var root = (FrameworkElement)window.Content;
        window.Content = null;
        root.Resources.MergedDictionaries.Add(window.Resources);
        NameScope.SetNameScope(root, NameScope.GetNameScope(window));
        TextElementForeground(root);
        return root;
    }

    private static void TextElementForeground(FrameworkElement root) =>
        root.SetValue(System.Windows.Documents.TextElement.ForegroundProperty, Brushes.White);

    private static void Layout(FrameworkElement root, Size size)
    {
        for (var i = 0; i < 4; i++)
        {
            Progress($"Layout pass {i + 1}/4: measure and arrange");
            root.InvalidateMeasure();
            root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
            Progress($"Layout pass {i + 1}/4: waiting for dispatcher idle");
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Progress($"Layout pass {i + 1}/4: complete");
        }
    }

    private static void AssertVisible(FrameworkElement root, string name, Size size)
    {
        if (root.FindName(name) is not FrameworkElement element) throw new Exception($"Missing element: {name}");
        if (!element.IsVisible && element.Visibility == Visibility.Collapsed) throw new Exception($"Collapsed action: {name}");
        var bounds = element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
        if (bounds.Width < 1 || bounds.Height < 1 || bounds.Left < -1 || bounds.Top < -1 || bounds.Right > root.RenderSize.Width + 1 || bounds.Bottom > root.RenderSize.Height + 1)
            throw new Exception($"Unreachable {name} at {size}: {bounds}");
        _checks++;
    }

    private static void AssertReachableByScrolling(FrameworkElement root, string scrollName, string name, Size size)
    {
        var scroll = (ScrollViewer)root.FindName(scrollName);
        var field = (FrameworkElement)root.FindName(name);
        scroll.ScrollToHome(); Layout(root, size);
        var bounds = field.TransformToAncestor(scroll).TransformBounds(new Rect(field.RenderSize));
        scroll.ScrollToVerticalOffset(Math.Max(0, bounds.Top - 4)); Layout(root, size);
        AssertVisible(root, name, size);
        bounds = field.TransformToAncestor(scroll).TransformBounds(new Rect(field.RenderSize));
        if (bounds.Top < -1 || bounds.Bottom > scroll.ViewportHeight + 1)
            throw new Exception($"{name} is clipped by its scrolling viewport at {size}: {bounds}; viewport {scroll.ViewportHeight}");
    }

    private static void Seed(FrameworkElement root)
    {
        if (root.FindName("GuidedStepText") is TextBlock guided)
        {
            guided.Text = "3 · Prepare your baseline tune";
            ((TextBlock)root.FindName("GuidedInstructionsText")).Text = "Example installed car · Driver: Tester\nGenerate and review your recommendation. Load your chosen AC setup in the game and enter the recommended FFB/wheelbase settings before confirming below. Generating and saving a profile do not apply settings.";
            ((TextBlock)root.FindName("GuidedIntegrationText")).Text = "Manual workflow: save the AC setup file, load it from AC's Setup menu, and enter the recommended AC FFB settings. Use your wheelbase's software for supported settings. SimHub/AZOM live control is optional.";
            ((TextBlock)root.FindName("GuidedProgressText")).Text = "Car ✓ → Goals ✓ → Prepare ○ → Baseline ○ → Test ○ → Compare ○ → Review ○";
            ((Button)root.FindName("GuidedNextButton")).Content = "Confirm Tune Is Ready";
        }
        foreach (var text in Descendants(root).OfType<TextBlock>().Where(t => t.Name.EndsWith("StatusText") || t.Name == "SetupText"))
            text.Text = "R12 / CS Pro • Example drift car • A longer status message to check wrapping and action visibility.";
        foreach (var combo in Descendants(root).OfType<ComboBox>().Where(c => c.Items.Count == 0))
        { combo.Items.Add("Example hardware / saved profile"); combo.SelectedIndex = 0; }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var descendant in Descendants(child)) yield return descendant;
    }

    private static void Render(FrameworkElement root, Size size, string path)
    {
        Progress($"Rendering {Path.GetFileName(path)}");
        var target = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
        target.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(target));
        using var stream = File.Create(path); encoder.Save(stream);
        Progress($"Saved {Path.GetFileName(path)}");
    }

    private static void CheckAdaptivePanel()
    {
        var panel = new AdaptivePanel { MinItemWidth = 250, MaxColumns = 3, Spacing = 12 };
        for (var i = 0; i < 6; i++) panel.Children.Add(new Border { Height = 100 });
        panel.Children.Add(new Border { Height = 900, Visibility = Visibility.Collapsed });
        foreach (var width in new[] { 400d, 800d, 1800d })
        {
            panel.Measure(new Size(width, double.PositiveInfinity)); panel.Arrange(new Rect(0, 0, width, panel.DesiredSize.Height));
            var expected = width == 400 ? 660 : 212;
            if (Math.Abs(panel.DesiredSize.Height - expected) > 1) throw new Exception("Card reflow height incorrect.");
            foreach (var child in panel.Children.OfType<Border>().Where(c => c.Visibility != Visibility.Collapsed))
                if (child.ActualWidth < 250) throw new Exception("Adaptive panel made cards unreadably narrow.");
            _checks++;
        }
    }
}
