using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AtomicDriftTuner.Engine;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Controls;

public sealed class TrackMap : FrameworkElement
{
    public List<List<TelemetrySample>> Paths { get; set; } = [];
    public TrackSection? Section { get; set; }
    public SectionPass? Before { get; set; }
    public SectionPass? After { get; set; }
    public double CursorTime { get; set; }
    public event Action<double>? TimePicked;
    private Func<RoutePoint,Point>? _transform;
    public TrackMap() { MinHeight=260; ClipToBounds=true; MouseLeftButtonDown+=Pick; }
    private void Pick(object sender,MouseButtonEventArgs e)
    {
        if(_transform is null) return;
        var at=e.GetPosition(this);
        var nearest=Paths.SelectMany(p=>p).MinBy(s=>(_transform(TrackSectionEngine.Point(s.Position!))-at).LengthSquared);
        if(nearest is not null && (_transform(TrackSectionEngine.Point(nearest.Position!))-at).Length<25)
            TimePicked?.Invoke(nearest.TimeSeconds);
    }
    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(17,23,32)),null,new Rect(0,0,ActualWidth,ActualHeight));
        var all=Paths.SelectMany(p=>p).Select(s=>TrackSectionEngine.Point(s.Position!))
            .Concat(Section?.Route ?? []).Concat(Section is null?[]:TrackSectionEngine.AimRoute(Section)).Concat(Before?.Path ?? []).Concat(After?.Path ?? []).ToList();
        if(all.Count==0) { _transform=null; return; }
        double minX=all.Min(p=>p.X), maxX=all.Max(p=>p.X), minZ=all.Min(p=>p.Z), maxZ=all.Max(p=>p.Z);
        double scale=Math.Min(Math.Max(1,ActualWidth-40)/Math.Max(1,maxX-minX),Math.Max(1,ActualHeight-40)/Math.Max(1,maxZ-minZ));
        _transform=p=>new Point((p.X-(minX+maxX)/2)*scale+ActualWidth/2,ActualHeight/2-(p.Z-(minZ+maxZ)/2)*scale);
        void Line(IEnumerable<RoutePoint> source,Brush brush,double thickness)
        {
            var points=source.ToList(); if(points.Count<2) return;
            var geometry=new StreamGeometry();
            using(var context=geometry.Open())
            {
                context.BeginFigure(_transform(points[0]),false,false);
                int step=Math.Max(1,points.Count/2500);
                for(int i=step;i<points.Count;i+=step) context.LineTo(_transform(points[i]),true,false);
                context.LineTo(_transform(points[^1]),true,false);
            }
            geometry.Freeze(); dc.DrawGeometry(null,new Pen(brush,thickness),geometry);
        }
        foreach(var path in Paths)
        {
            Line(path.Select(s=>TrackSectionEngine.Point(s.Position!)),Brushes.SlateGray,1.2);
            // Split absent spline context. Gray dashed data is an AI spline, never a drift target.
            var spline=new List<RoutePoint>();
            foreach(var s in path)
            {
                var p=s.Position!;
                if(p.SplineX is double x && p.SplineY is double y && p.SplineZ is double z &&
                    new[]{x,y,z}.All(double.IsFinite))
                {
                    var next=new RoutePoint(x,y,z);
                    if(spline.Count>0 && TrackSectionEngine.Distance(spline[^1],next)>20) { Line(spline,Brushes.DimGray,.7); spline.Clear(); }
                    spline.Add(next);
                }
                else { Line(spline,Brushes.DimGray,.7); spline.Clear(); }
            }
            Line(spline,Brushes.DimGray,.7);
        }
        if(Section is not null)
        {
            Line(Section.Route,Brushes.Gold,3);
            if(Section.TargetOffsetM!=0) Line(TrackSectionEngine.AimRoute(Section),Brushes.LightGreen,2);
            dc.DrawEllipse(Brushes.Gold,null,_transform(Section.Route[0]),5,5);
            dc.DrawRectangle(Brushes.Gold,null,new Rect(_transform(Section.Route[^1])-new Vector(4,4),new Size(8,8)));
        }
        if(Before is not null) Line(Before.Path,Brushes.DeepSkyBlue,2);
        if(After is not null) Line(After.Path,Brushes.HotPink,2);
        var cursor=Paths.SelectMany(p=>p).MinBy(s=>Math.Abs(s.TimeSeconds-CursorTime));
        if(cursor is not null) dc.DrawEllipse(Brushes.White,new Pen(Brushes.Black,1),_transform(TrackSectionEngine.Point(cursor.Position!)),4,4);
    }
}
