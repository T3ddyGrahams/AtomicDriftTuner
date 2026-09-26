using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using AtomicDriftTuner.Engine;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

namespace AtomicDriftTuner.Controls;

public partial class TrackSectionView : UserControl
{
    private SavedTelemetrySession? _run, _before;
    private double? _start, _end;
    private bool _binding;
    public TrackSectionStore Store { get; set; } = new();
    public TrackSectionView()
    {
        InitializeComponent();
        SectionFeedback.SaveRequested += SaveFeedback;
        LineAimBox.ItemsSource=new[]{"Follow reference","Positive map side","Negative map side"};
        LineAimBox.SelectedIndex=0;
        Map.TimePicked+=t=>TimeSlider.Value=t;
    }
    public void Bind(SavedTelemetrySession? run,SavedTelemetrySession? before)
    {
        _binding=true;
        try
        {
            _run=run; _before=before ?? run; _start=_end=null;
            RangeText.Text="Mark the start and end of one pass."; Status.Text=""; Findings.Text=""; GoalText.Text=""; PassSummary.Text="";
            BeforeBox.ItemsSource=AfterBox.ItemsSource=null;
            Map.Before=Map.After=null; Map.Section=null;
            Map.Paths=run is null ? [] : TrackSectionEngine.Paths(run.Session);
            TimeSlider.Maximum=Math.Max(1,run?.Session.Samples.LastOrDefault()?.TimeSeconds ?? 1); TimeSlider.Value=0;
            SaveSectionButton.IsEnabled=MarkStartButton.IsEnabled=MarkEndButton.IsEnabled=Map.Paths.Count>0;
            int usable=Map.Paths.Sum(p=>p.Count), raw=run?.Session.Samples.Count ?? 0;
            int bounds=Map.Paths.SelectMany(p=>p).Count(s=>s.Position?.LeftBoundaryM is not null && s.Position?.RightBoundaryM is not null);
            CaptureText.Text=usable==0 ? "No usable world positions. Old recordings remain readable. Update and enable ADT Companion, then record another forward pass." :
                $"{usable:N0}/{raw:N0} native samples have usable position in {Map.Paths.Count} continuous path(s). Track/layout: {Map.Paths[0][0].Position!.Track}/{(Map.Paths[0][0].Position!.Layout.Length==0?"default":Map.Paths[0][0].Position!.Layout)}. "+
                (bounds==0?"Boundary context: unknown.":$"Mod-authored AI boundary distances available at {bounds:N0} samples; unverified as physical road edges. Usable width: unknown.");
            string selected=(SectionBox.SelectedItem as TrackSection)?.Id ?? "";
            var sections=Store.Load(out var issue); Status.Text=issue;
            SectionBox.ItemsSource=sections;
            SectionBox.SelectedItem=sections.FirstOrDefault(s=>s.Id==selected) ?? sections.FirstOrDefault();
            BeforeLabel.Text=before is null?"Before pass · another pass in this same recording":"Before pass · selected baseline recording";
            Map.InvalidateVisual();
        }
        catch(Exception ex) { Status.Text="Track review unavailable: "+ex.Message; SectionBox.ItemsSource=null; }
        finally { _binding=false; }
        RefreshPasses();
    }
    private void CursorChanged(object sender,RoutedPropertyChangedEventArgs<double> e)
    {
        if(Map is null || CursorText is null) return;
        Map.CursorTime=e.NewValue; CursorText.Text=$"Cursor {e.NewValue:0.00} s"; Map.InvalidateVisual();
    }
    private void MarkStart(object sender,RoutedEventArgs e) { _start=TimeSlider.Value; ShowRange(); }
    private void MarkEnd(object sender,RoutedEventArgs e) { _end=TimeSlider.Value; ShowRange(); }
    private void ShowRange() => RangeText.Text=$"Section {_start?.ToString("0.00") ?? "?"} → {_end?.ToString("0.00") ?? "?"} s";
    private static double? Number(TextBox box)
    {
        if(string.IsNullOrWhiteSpace(box.Text)) return null;
        if(double.TryParse(box.Text,NumberStyles.Float,CultureInfo.CurrentCulture,out var v)&&double.IsFinite(v)) return v;
        throw new InvalidOperationException("Enter a finite number, or leave optional goals blank.");
    }
    private void SaveSection(object sender,RoutedEventArgs e)
    {
        try
        {
            if(_run is null || _start is null || _end is null) throw new InvalidOperationException("Mark the start and end on a recorded pass first.");
            var gear=Number(GearBox);
            if(gear is not null && (gear!=Math.Truncate(gear.Value)||gear<1||gear>10)) throw new InvalidOperationException("Preferred forward gear must be a whole number from 1 to 10.");
            string aim=LineAimBox.SelectedItem as string ?? "Follow reference";
            double distance=Number(OffsetBox) ?? 0;
            if(distance<0) throw new InvalidOperationException("Enter a positive offset distance and choose its side.");
            double offset=aim=="Negative map side"?-distance:distance;
            var section=TrackSectionEngine.Mark(_run.Session,_start.Value,_end.Value,NameBox.Text,
                Number(AngleMinBox),Number(AngleMaxBox),gear is null?null:(int)gear,offset,aim);
            Store.Save(section);
            SectionBox.ItemsSource=Store.Load(out var issue); SectionBox.SelectedItem=((List<TrackSection>)SectionBox.ItemsSource).First(s=>s.Id==section.Id);
            Status.Text="Saved a new section and goal revision in your ADT history. Record matching repeat passes. "+issue;
        }
        catch(Exception ex) { Status.Text=ex.Message; }
    }
    private void SectionChanged(object sender,SelectionChangedEventArgs e) { if(!_binding) RefreshPasses(); }
    private void RefreshPasses()
    {
        SectionFeedback.Bind(null); SectionReviewHistory.ItemsSource = null; SectionReviewText.Text = "";
        _binding=true;
        try
        {
            BeforeBox.ItemsSource=AfterBox.ItemsSource=null; Findings.Text=""; PassSummary.Text=""; GoalText.Text="";
            Map.Section=SectionBox.SelectedItem as TrackSection; Map.Before=Map.After=null;
            if(Map.Section is not TrackSection section || _run is null || _before is null) { Map.Section=null; return; }
            GoalText.Text=$"{section.Name}: {section.LineAim}, {Math.Abs(section.TargetOffsetM):0.0} m. "+
                (section.MinimumAngle.HasValue?$"Body angle {section.MinimumAngle:0}–{section.MaximumAngle:0}°. ":"No body-angle target. ")+
                (section.PreferredGear.HasValue?$"Forward gear {section.PreferredGear}. ":"No preferred gear. ")+"Saved goals do not change Desired Behavior or setup values.";
            var a=TrackSectionEngine.Analyze(_before.Session,section); var b=_before==_run?a:TrackSectionEngine.Analyze(_run.Session,section);
            if(!Map.Paths.Any(p=>p.Any(s=>string.Equals(s.Position!.Track,section.Track,StringComparison.OrdinalIgnoreCase) &&
                string.Equals(s.Position!.Layout,section.Layout,StringComparison.OrdinalIgnoreCase)))) Map.Section=null;
            BeforeBox.ItemsSource=a.Passes; AfterBox.ItemsSource=b.Passes;
            BeforeBox.SelectedIndex=a.Passes.Count>0?0:-1; AfterBox.SelectedIndex=b.Passes.Count>0?(_before==_run && b.Passes.Count>1?1:0):-1;
            PassSummary.Text=(_before==_run?"": "Baseline: "+a.Summary+"\nSelected run: ")+b.Summary;
        }
        catch(Exception ex) { Findings.Text="Section review held: "+ex.Message; }
        finally { _binding=false; Map.InvalidateVisual(); }
        RefreshComparison();
    }
    private void PassChanged(object sender,SelectionChangedEventArgs e) { if(!_binding) RefreshComparison(); }
    private void RefreshComparison()
    {
        Map.Before=BeforeBox.SelectedItem as SectionPass; Map.After=AfterBox.SelectedItem as SectionPass;
        if(Map.Section is null) Map.Before=Map.After=null;
        if(Map.Section is TrackSection section && Map.Before is SectionPass a && Map.After is SectionPass b && _run is not null && _before is not null)
        {
            Findings.Text=TrackSectionEngine.Compare(a,b,section,_before,_run).Findings;
            var feedback = GoalFeedbackEngine.ForSection(section, a, b, _before, _run);
            try
            {
                string historyIssue = "";
                var reviews = feedback is null ? [] : Store.LoadFeedback(feedback.ContextFingerprint, out historyIssue);
                SectionFeedback.Bind(feedback, reviews.FirstOrDefault()?.Feedback);
                SectionReviewHistory.ItemsSource = reviews; SectionReviewHistory.SelectedIndex = reviews.Count > 0 ? 0 : -1;
                if (reviews.Count == 0) SectionReviewText.Text = "No saved review for this exact section revision and pass pair.";
                if (historyIssue.Length > 0) SectionReviewText.Text += "\n" + historyIssue;
            }
            catch (Exception ex) { SectionFeedback.Bind(feedback); SectionReviewHistory.ItemsSource = null; SectionReviewText.Text = "Could not load feedback: " + ex.Message; }
        }
        else
        {
            Findings.Text="Choose two complete passes. Missing evidence requires another pass; no result is inferred.";
            SectionFeedback.Bind(null); SectionReviewHistory.ItemsSource = null; SectionReviewText.Text = "";
        }
        Map.InvalidateVisual();
    }
    private void SaveFeedback(object? sender, EventArgs e)
    {
        try
        {
            var feedback = SectionFeedback.Snapshot;
            if (feedback is null || !feedback.HasAnswers) return;
            Store.SaveFeedback(new SectionFeedbackReview { Feedback = feedback });
            SectionReviewHistory.ItemsSource = Store.LoadFeedback(feedback.ContextFingerprint, out var issue);
            SectionReviewHistory.SelectedIndex = 0;
            SectionFeedback.Saved("Saved for this section revision and these two passes. " + issue);
        }
        catch (Exception ex) { SectionFeedback.Saved("Feedback was not saved: " + ex.Message); }
    }
    private void ReviewSelected(object sender, SelectionChangedEventArgs e)
    {
        if (SectionReviewHistory.SelectedItem is SectionFeedbackReview review)
            SectionReviewText.Text = GoalFeedbackEngine.Summary(review.Feedback);
    }
}
