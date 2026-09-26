using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AtomicDriftTuner.Controls;
using AtomicDriftTuner.Engine;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

internal static partial class Program
{
    private static void CheckTrack(string output)
    {
        int checks=0;
        void Check(bool ok,string why) { if(!ok)throw new Exception("Track UI: "+why); checks++; }
        var view=new TrackSectionView { Store=new TrackSectionStore(Path.Combine(output,"track-goals-"+Guid.NewGuid().ToString("N"))),
            Foreground=(Brush)Application.Current.Resources["PrimaryTextBrush"], Background=(Brush)Application.Current.Resources["AppBackgroundBrush"] };
        T C<T>(string name) where T:FrameworkElement => (T)view.FindName(name);
        void Click(string name) => C<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var session=TrackFixture.CurvedRun();var saved=new SavedTelemetrySession { Session=session,Analysis=new TelemetryAnalyzer().Analyze(session) };
        view.Bind(saved,null); Layout(view,new Size(680,900));
        Check(C<TextBlock>("CaptureText").Text.Contains("usable position")&&C<TrackMap>("Map").Paths.Count==3,"Capture missing");
        C<Slider>("TimeSlider").Value=1; Click("MarkStartButton");C<Slider>("TimeSlider").Value=7; Click("MarkEndButton");
        C<TextBox>("NameBox").Text="Entry to long corner";C<TextBox>("AngleMinBox").Text="30";C<TextBox>("AngleMaxBox").Text="45";C<TextBox>("GearBox").Text="2";
        Click("SaveSectionButton");
        Check(C<ComboBox>("SectionBox").Items.Count==1&&C<TextBlock>("Status").Text.Contains("Saved a new section"),C<TextBlock>("Status").Text);
        Check(C<ComboBox>("BeforeBox").Items.Count==3&&C<ComboBox>("AfterBox").SelectedIndex==1,"Default repeat selection");
        Check(C<TextBlock>("Findings").Text.Contains("Matched section passes")&&C<TextBlock>("GoalText").Text.Contains("Forward gear 2"),"Findings/goals missing");
        Check(C<TrackMap>("Map").Before!=null&&C<TrackMap>("Map").After!=null,"Before/after paths absent");
        C<ComboBox>("AfterBox").SelectedIndex=0;Check(C<TextBlock>("Findings").Text.Contains("Comparison held"),"Same pass allowed");C<ComboBox>("AfterBox").SelectedIndex=1;
        foreach(var size in new[]{new Size(430,620),new Size(680,900),new Size(1800,750)})
        {
            Layout(view,size);var scroll=(ScrollViewer)view.Content;
            Check(C<TrackMap>("Map").ActualWidth>200&&C<TrackMap>("Map").ActualWidth<=size.Width,"Map clipped");
            Check(scroll.ScrollableHeight>0&&scroll.ScrollableWidth==0,"Cannot scroll vertically or unwanted horizontal scroll");
            scroll.ScrollToTop();Layout(view,size);Render(view,size,Path.Combine(output,$"Track-{size.Width}-map.png"));
            C<Button>("SaveSectionButton").BringIntoView();Layout(view,size);
            Check(C<Button>("SaveSectionButton").ActualWidth<size.Width,"Save action clips");
            scroll.ScrollToBottom();Layout(view,size);Render(view,size,Path.Combine(output,$"Track-{size.Width}-findings.png"));
            Check(C<TextBlock>("Findings").TextWrapping==TextWrapping.Wrap&&C<TextBlock>("Findings").ActualWidth<size.Width,"Findings clip");
        }
        var other=TrackFixture.Run();foreach(var s in other.Samples)s.Position=s.Position! with { Layout="other" };
        view.Bind(new SavedTelemetrySession {Session=other,Analysis=new TelemetryAnalyzer().Analyze(other)},saved);
        Check(C<ComboBox>("AfterBox").Items.Count==0&&C<TrackMap>("Map").After is null,"Stale pass survived mismatch");
        foreach(var s in other.Samples)s.Position=null;
        view.Bind(new SavedTelemetrySession {Session=other},null);
        Check(C<TrackMap>("Map").Paths.Count==0&&!C<Button>("SaveSectionButton").IsEnabled&&C<TextBlock>("CaptureText").Text.Contains("Old recordings"),"Invented legacy track");
        view.Bind(null,null);
        Check(C<TrackMap>("Map").Before is null&&C<TrackMap>("Map").After is null&&C<ComboBox>("BeforeBox").Items.Count==0,"Clear left stale evidence");
        Progress($"PASS {checks} track UI assertions and narrow/portrait/wide renders; no production startup.");
    }
}
