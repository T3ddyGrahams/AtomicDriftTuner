using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AtomicDriftTuner;
using AtomicDriftTuner.Controls;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

internal static partial class Program
{
    private static void CheckSetupComparison(string output)
    {
        var checks = 0;
        void Check(bool ok, string why) { if (!ok) throw new Exception("Setup comparison UI: " + why); checks++; }
        static object? Call(object o, string method, params object?[] args) => o.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(o, args);
        static ItemsControl Rows(SetupComparisonView v) => (ItemsControl)v.FindName("RowsList");
        static CheckBox All(SetupComparisonView v) => (CheckBox)v.FindName("ShowAllBox");
        static IEnumerable<DependencyObject> Visuals(DependencyObject root)
        {
            yield return root;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                foreach (var child in Visuals(VisualTreeHelper.GetChild(root, i))) yield return child;
        }
        var f = new CarTestFixture(output);
        var review = new CarSetupTestWindow(f.Input, f.Run, f.Report);
        try
        {
            var v = (SetupComparisonView)review.FindName("TestComparison");
            Call(review, "LoadBaseline", f.Baseline);
            Check(Rows(v).Items.Count == 2 && Rows(v).Items.Cast<SetupComparisonPresentation.Row>().All(r => r.Changed), "Default did not show only paired proposed changes");
            All(v).IsChecked = true;
            Check(Rows(v).Items.Count == 6 && Rows(v).Items.Cast<SetupComparisonPresentation.Row>().Any(r => r.Key == "FUEL" && !r.Changed), "Show all lost preserved values");
            All(v).IsChecked = false;
            Check(Rows(v).Items.Count == 2, "Toggle did not return to changed settings");
            foreach (var size in new[] { new Size(340, 620), new Size(640, 800), new Size(1150, 620) })
            {
                Layout(v, size);
                var panels = Visuals(v).OfType<AdaptivePanel>().ToArray();
                Check(panels.Length == 2 && panels.All(p => p.ActualWidth <= size.Width), $"Value columns overflow narrow viewport {size}: {panels.Length} panels, widths {string.Join(",", panels.Select(p => p.ActualWidth))}");
                Check(Visuals(v).OfType<TextBlock>().Any(t => t.Text == "Recommended") && Visuals(v).OfType<TextBlock>().Any(t => t.Text == "26 psi"), "Labels or before/after values not rendered");
                Check(Visuals(v).OfType<TextBlock>().Where(t => t.Text.StartsWith("Aim:")).All(t => t.TextWrapping == TextWrapping.Wrap && t.ActualHeight > 30), "Long explanation clipped instead of wrapping");
                Render(v, size, Path.Combine(output, $"SetupChanges-Proposed-{size.Width}.png"));
            }
            var theme = ThemeCatalog.Clone(ThemeCatalog.Presets[0]); theme.PrimaryText = "#D5FFFF"; theme.FieldLabel = "#90EE90"; theme.SecondaryText = "#FFC0CB";
            ThemeService.Apply(theme); Layout(v, new Size(640, 800));
            Check(Visuals(v).OfType<TextBlock>().Where(t => t.Text == "26 psi").All(t => ThemeService.ToHex(((SolidColorBrush)t.Foreground).Color) == theme.PrimaryText), "Values ignore custom theme");
            ThemeService.Apply(ThemeCatalog.Presets[0]);
            // A stale/failed baseline must not keep a previous valid preview on screen.
            Call(review, "LoadBaseline", Path.Combine(f.DirectoryPath, "missing.ini"));
            Check(Rows(v).Items.Count == 0, "Failed load kept stale comparison");
        }
        finally { review.Close(); }

        var setup = new CarSetupWindow(f.Input);
        try
        {
            var type = typeof(CarSetupWindow).GetNestedType("SetupChoice", BindingFlags.NonPublic)!;
            var choices = (ComboBox)setup.FindName("BaselineBox"); choices.ItemsSource = new[] { Activator.CreateInstance(type, f.Baseline)! }; choices.SelectedIndex = 0;
            Call(setup, "GenerateSetup_Click", setup, new RoutedEventArgs());
            var view = (SetupComparisonView)setup.FindName("SetupComparison");
            Check(Rows(view).Items.Count > 0, "Full setup generation did not show comparison");
            Call(setup, "InvalidateGeneratedRecommendations", "Fixture goal change.");
            Check(Rows(view).Items.Count == 0, "Changed goals kept old proposed comparison");
        }
        finally { setup.Close(); }

        var assistant = new TuningAssistantWindow(f.Input);
        try
        {
            var after = RunHistoryStore.Clone(f.Run); after.Session.Id = Guid.NewGuid().ToString("N");
            after.Session.Context!.Tune!.Settings["ACSetup.PRESSURE_LR"] = 26;
            after.Session.Context.Tune.Settings["ACSetup.PRESSURE_RR"] = 26;
            after.Session.Context.TuneConfirmedInUse = false;
            after.Session.Context.TestedRecommendations.Add("Rear grip: test lower rear tyre pressures.");
            Call(assistant, "RenderSetupComparison", after, f.Run);
            var view = (SetupComparisonView)assistant.FindName("RecordedSetupComparison");
            Check(Rows(view).Items.Count == 2 && Rows(view).Items.Cast<SetupComparisonPresentation.Row>().All(r => r.AfterHeading == "Recorded after"), "Actual run comparison used recommendation labels");
            var context = (TextBlock)view.FindName("ContextText");
            Check(context.Text.Contains("not confirmed") && context.Text.Contains("not linked") && context.Text.Contains("Rear grip"), "Confirmation / test tracking limits missing");
            All(view).IsChecked = true; Check(Rows(view).Items.Count == 6, "History full comparison missing unchanged values");
            All(view).IsChecked = false;
            foreach (var size in new[] { new Size(340, 750), new Size(1000, 750) })
            { Layout(view, size); Render(view, size, Path.Combine(output, $"SetupChanges-Recorded-{size.Width}.png")); }
            Call(assistant, "ShowComparedTunes_Click", assistant, new RoutedEventArgs());
            Check(Descendants((FrameworkElement)assistant.Content).OfType<TabItem>().Any(t => Equals(t.Header, "Before / After") && t.IsSelected), "History shortcut did not open the comparison tab");
            foreach (var size in new[] { new Size(430, 430), new Size(700, 900), new Size(1800, 800) })
            {
                var root = (FrameworkElement)assistant.Content; Layout(root, size);
                var scroll = (ScrollViewer)assistant.FindName("AssistantBodyScroll");
                scroll.ScrollToHome(); Layout(root, size);
                var bounds = All(view).TransformToAncestor(scroll).TransformBounds(new Rect(All(view).RenderSize));
                scroll.ScrollToVerticalOffset(Math.Max(0, bounds.Top - 4)); Layout(root, size);
                var visible = All(view).TransformToAncestor(root).TransformBounds(new Rect(All(view).RenderSize));
                Check(visible.Top >= 0 && visible.Bottom <= size.Height && visible.Right <= size.Width, "Full comparison toggle not reachable by scrolling");
            }
            Call(assistant, "RenderSetupComparison", after, null);
            Check(Rows(view).Items.Count == 0 && context.Text.Contains("Choose an earlier baseline"), "Removing baseline retained unrelated data");
        }
        finally { assistant.Close(); }
        Progress($"PASS {checks} setup comparison assertions plus narrow/portrait/wide rendering.");
    }
}
