using System.Windows;
using System.Windows.Controls;
using AtomicDriftTuner.Services;

namespace AtomicDriftTuner.Controls;

public partial class SetupComparisonView : UserControl
{
    private IReadOnlyList<SetupComparisonPresentation.Row> _rows = [];
    public SetupComparisonView() => InitializeComponent();
    public void Show(IReadOnlyList<SetupComparisonPresentation.Row> rows, string context)
    {
        _rows = rows; ContextText.Text = context; Refresh(); RowsScroll.ScrollToHome();
    }
    public void Clear(string context = "Choose a setup or two saved runs to compare.") => Show([], context);
    private void Filter_Changed(object sender, RoutedEventArgs e) { if (RowsList is not null) Refresh(); }
    private void Refresh()
    {
        var visible = _rows.Where(r => ShowAllBox.IsChecked == true || r.Emphasized).ToArray();
        RowsList.ItemsSource = visible;
        CountText.Text = _rows.Count == 0 ? "No setup values available for this comparison." :
            $"{_rows.Count(r => r.Changed)} changed · {_rows.Count(r => r.Unavailable)} unavailable · {visible.Length} of {_rows.Count} settings shown." +
            (visible.Length == 0 ? " No changes to show. Select Show all settings to inspect the captured values." : "");
    }
}
