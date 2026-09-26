using System.IO;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

namespace AtomicDriftTuner.Engine;

/// <summary>Conservative, descriptive route matching. No tuning or write commands.</summary>
public static class TrackSectionEngine
{
    public const string Version = "track-section/1";
    private const int Bins = 10;
    public static RoutePoint Point(TrackPosition p) => new(p.X, p.Y, p.Z);
    public static double Distance(RoutePoint a, RoutePoint b) => Math.Sqrt(
        Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2) + Math.Pow(a.Z - b.Z, 2));
    public static List<RoutePoint> AimRoute(TrackSection section) => section.Route.Select((p,i)=>
    {
        var a=section.Route[Math.Max(0,i-1)]; var b=section.Route[Math.Min(section.Route.Count-1,i+1)];
        double dx=b.X-a.X,dz=b.Z-a.Z,h=Math.Sqrt(dx*dx+dz*dz);
        return h<.001?p:new RoutePoint(p.X-dz/h*section.TargetOffsetM,p.Y,p.Z+dx/h*section.TargetOffsetM);
    }).ToList();
    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    public static bool ValidPosition(TrackPosition? p) => p is not null && p.Source is "csp-track/1" or "csp-track-dev/1" &&
        !string.IsNullOrWhiteSpace(p.Track) && !string.IsNullOrWhiteSpace(p.Car) && p.Track.Length <= 100 && p.Layout is { Length: <= 100 } &&
        new[] { p.X, p.Y, p.Z, p.SourceTimeMs, p.AlignmentUncertaintySeconds }.All(double.IsFinite) &&
        Math.Abs(p.X) <= 1_000_000 && Math.Abs(p.Y) <= 1_000_000 && Math.Abs(p.Z) <= 1_000_000 &&
        p.SourceTimeMs >= 0 && p.AlignmentUncertaintySeconds is >= 0 and <= .1;

    public static List<List<TelemetrySample>> Paths(TelemetrySession session)
    {
        var result = new List<List<TelemetrySample>>();
        foreach (var block in DriftDiagnosisEngine.LocationBlocks(session))
        {
            var path = new List<TelemetrySample>();
            void End() { if (path.Count > 1) result.Add(path); path = []; }
            foreach (var frame in block)
            {
                var s = frame.Sample;
                if (!s.HasExtendedSignals || s.LongitudinalVelocityMs is null or < 0 || s.SpeedKmh < 20 || s.Gear is < 1 or > 11)
                { End(); continue; }
                // Missing spatial polls may be bridged only inside an unbroken clean
                // native block and for at most 0.2s. No position is interpolated.
                if(s.Position is null) continue;
                if (!ValidPosition(s.Position) || !Same(s.Position!.Car, session.CarFolder)) { End(); continue; }
                if (path.Count > 0)
                {
                    var a = path[^1]; var p = a.Position!; var q = s.Position!;
                    double dt = s.TimeSeconds - a.TimeSeconds;
                    double distance = Distance(Point(p), Point(q));
                    if (dt is <= 0 or > .2 || !Same(p.Track, q.Track) || !Same(p.Layout, q.Layout) ||
                        q.SourceTimeMs <= p.SourceTimeMs || q.SourceTimeMs - p.SourceTimeMs > 250 ||
                        distance > Math.Max(4, Math.Max(a.SpeedKmh, s.SpeedKmh) / 3.6 * dt * 1.8 + 2) ||
                        distance < Math.Min(a.SpeedKmh, s.SpeedKmh) / 3.6 * dt * .1 ||
                        Math.Abs(q.Y - p.Y) > distance * .5 + 1)
                        End();
                }
                path.Add(s);
            }
            End();
        }
        return result;
    }

    public static TrackSection Mark(TelemetrySession session, double start, double end, string name,
        double? angleMin, double? angleMax, int? gear, double offset, string aim)
    {
        if (!double.IsFinite(start) || !double.IsFinite(end) || end - start < 2)
            throw new InvalidDataException("Mark at least two seconds of one continuous forward pass.");
        var paths = Paths(session);
        var path = paths.FirstOrDefault(p => p[0].TimeSeconds <= start + .1 && p[^1].TimeSeconds >= end - .1);
        var samples = path?.Where(s => s.TimeSeconds >= start && s.TimeSeconds <= end).ToList();
        if (samples is null || samples.Count < 20)
            throw new InvalidDataException("The marked interval crosses missing, invalid, reset, backward or excluded evidence. Record another continuous pass.");
        var first = samples[0].Position!;
        var section = new TrackSection { Name = name.Trim(), Track = first.Track, Layout = first.Layout,
            ReferenceSessionId = session.Id, ReferenceStartSeconds = start, ReferenceEndSeconds = end,
            MinimumAngle = angleMin, MaximumAngle = angleMax, PreferredGear = gear, TargetOffsetM = offset, LineAim = aim };
        // Resample by distance for stable nearest-segment projection and bounded work.
        section.Route.Add(Point(first));
        foreach (var s in samples.Skip(1))
            if (Distance(section.Route[^1], Point(s.Position!)) >= 2) section.Route.Add(Point(s.Position!));
        var last = Point(samples[^1].Position!);
        if (Distance(section.Route[^1], last) > .25) section.Route.Add(last);
        Validate(section);
        return section;
    }

    public static void Validate(TrackSection s)
    {
        if (s.Schema != "adt/track-section/1" || !Guid.TryParseExact(s.Id, "N", out _) ||
            string.IsNullOrWhiteSpace(s.Name) || s.Name.Length > 80 || s.Name.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(s.Track) || s.Track.Length > 100 || s.Layout is null || s.Layout.Length > 100 ||
            s.Route is null || s.Route.Count is < 3 or > 2000 ||
            s.Route.Any(p => p is null || new[] { p.X, p.Y, p.Z }.Any(v => !double.IsFinite(v) || Math.Abs(v) > 1_000_000)) ||
            s.MinimumAngle.HasValue != s.MaximumAngle.HasValue ||
            s.MinimumAngle is double min && (!double.IsFinite(min) || min < 10 || s.MaximumAngle is not double max || !double.IsFinite(max) || max > 85 || max - min < 5) ||
            s.PreferredGear is < 1 or > 10 || !double.IsFinite(s.TargetOffsetM) || Math.Abs(s.TargetOffsetM) > 15 ||
            s.LineAim is not ("Follow reference" or "Positive map side" or "Negative map side") ||
            s.LineAim == "Follow reference" && s.TargetOffsetM != 0 || s.LineAim == "Positive map side" && s.TargetOffsetM <= 0 ||
            s.LineAim == "Negative map side" && s.TargetOffsetM >= 0)
            throw new InvalidDataException("The section name, route or goals are invalid. Angle needs a 10–85° range at least 5° wide; forward gear is 1–10.");
        var lengths = Lengths(s.Route);
        if (lengths[^1] < 20 || lengths[^1] > 4000 ||
            s.Route.Zip(s.Route.Skip(1), Distance).Any(d => d is < .1 or > 20))
            throw new InvalidDataException("Use one continuous section, 20–4000 metres long, without large position gaps.");
        // Reject crossing/overlapping reference routes, including near-closed loops.
        // A 3D height separation preserves ordinary overpasses, though near-overlaps
        // remain conservative holds. Choose a shorter section on either side.
        for (int i = 0; i < s.Route.Count - 1; i++)
            for (int j = i + 2; j < s.Route.Count - 1; j++)
                if (lengths[j] - lengths[i + 1] > 15 &&
                    SegmentNear(s.Route[i], s.Route[i + 1], s.Route[j], s.Route[j + 1]))
                    throw new InvalidDataException("This reference overlaps or crosses itself. Mark a shorter unambiguous section.");
    }

    private static bool SegmentNear(RoutePoint a, RoutePoint b, RoutePoint c, RoutePoint d)
    {
        if(Math.Min(a.X,b.X)>Math.Max(c.X,d.X)+6 || Math.Min(c.X,d.X)>Math.Max(a.X,b.X)+6 ||
            Math.Min(a.Z,b.Z)>Math.Max(c.Z,d.Z)+6 || Math.Min(c.Z,d.Z)>Math.Max(a.Z,b.Z)+6 ||
            Math.Min(a.Y,b.Y)>Math.Max(c.Y,d.Y)+6 || Math.Min(c.Y,d.Y)>Math.Max(a.Y,b.Y)+6) return false;
        // Bounded <=20m segments; sample both at <=1m to conservatively catch crossings.
        int steps = Math.Max(1, (int)Math.Ceiling(Distance(a, b)));
        for (int k = 0; k <= steps; k++)
        {
            double t = (double)k / steps;
            var p = new RoutePoint(a.X + (b.X-a.X)*t, a.Y+(b.Y-a.Y)*t, a.Z+(b.Z-a.Z)*t);
            if (Project(p, c, d).Distance < 6) return true;
        }
        return false;
    }

    private static double[] Lengths(List<RoutePoint> route)
    {
        var lengths = new double[route.Count];
        for (int i = 1; i < route.Count; i++) lengths[i] = lengths[i-1] + Distance(route[i-1], route[i]);
        return lengths;
    }
    private sealed record Projection(double Along, double Offset, double Distance, int Segment, double HeightDifference = 0);
    private static Projection Project(RoutePoint p, RoutePoint a, RoutePoint b)
    {
        double dx=b.X-a.X, dy=b.Y-a.Y, dz=b.Z-a.Z, len=Distance(a,b);
        double t = len > .001 ? Math.Clamp(((p.X-a.X)*dx+(p.Y-a.Y)*dy+(p.Z-a.Z)*dz)/(len*len),0,1) : 0;
        double x=a.X+t*dx, y=a.Y+t*dy, z=a.Z+t*dz;
        double horizontal = Math.Sqrt(dx*dx+dz*dz);
        // +side is left of travel in the displayed X/right, Z/up map. It does
        // not infer an outside corner, physical road edge, or track handedness.
        double offset = horizontal > .001 ? (dx*(p.Z-z)-dz*(p.X-x))/horizontal : 0;
        return new(t*len, offset, Distance(p,new(x,y,z)),0,Math.Abs(p.Y-y));
    }
    private static Dictionary<(int X,int Z),List<int>> Index(List<RoutePoint> route)
    {
        var index=new Dictionary<(int,int),List<int>>();
        for(int i=0;i<route.Count-1;i++)
        {
            var a=route[i];var b=route[i+1];
            for(int x=(int)Math.Floor((Math.Min(a.X,b.X)-22)/20);x<=(int)Math.Floor((Math.Max(a.X,b.X)+22)/20);x++)
                for(int z=(int)Math.Floor((Math.Min(a.Z,b.Z)-22)/20);z<=(int)Math.Floor((Math.Max(a.Z,b.Z)+22)/20);z++)
                {
                    if(!index.TryGetValue((x,z),out var segments)) index[(x,z)]=segments=[];
                    segments.Add(i);
                }
        }
        return index;
    }
    private static Projection? Locate(TrackSection section, double[] lengths, Dictionary<(int,int),List<int>> index, TrackPosition p)
    {
        if(!index.TryGetValue(((int)Math.Floor(p.X/20),(int)Math.Floor(p.Z/20)),out var segments)) return null;
        var best = new Projection(0,0,double.MaxValue,0);
        var candidates = new List<Projection>();
        var point = Point(p);
        foreach(int i in segments)
        {
            var projection = Project(point,section.Route[i],section.Route[i+1]);
            projection = projection with { Along = projection.Along+lengths[i], Segment=i };
            if (projection.Distance < best.Distance) best=projection;
            candidates.Add(projection);
        }
        if (best.Distance > 20 || best.HeightDifference > 2) return null; // Matching corridor, never a claimed track width.
        // A nearest endpoint must not turn the approach/exit outside the marked
        // interval into section evidence merely because it is within the corridor.
        var a=section.Route[best.Segment]; var b=section.Route[best.Segment+1];
        double along=((p.X-a.X)*(b.X-a.X)+(p.Y-a.Y)*(b.Y-a.Y)+(p.Z-a.Z)*(b.Z-a.Z))/Distance(a,b);
        if(best.Segment==0 && along<-.25 || best.Segment==section.Route.Count-2 && along>Distance(a,b)+.25) return null;
        if (candidates.Any(c => Math.Abs(c.Along-best.Along)>15 && c.Distance < best.Distance+2)) return null;
        return best;
    }

    public static SectionPassResult Analyze(TelemetrySession session, TrackSection section)
    {
        Validate(section);
        var result=new SectionPassResult(); var lengths=Lengths(section.Route); double total=lengths[^1];var index=Index(section.Route);
        foreach(var path in Paths(session))
        {
            var points = new List<(TelemetrySample S, Projection P)>();
            double furthest=0;
            void Clear() { points.Clear(); furthest=0; }
            foreach(var sample in path)
            {
                var position=sample.Position!;
                if (!Same(position.Track,section.Track)||!Same(position.Layout,section.Layout)) { Clear(); continue; }
                var p=Locate(section,lengths,index,position);
                if(p is null) { Clear(); continue; }
                if(points.Count==0)
                {
                    if(p.Along/total <= .05) { points.Add((sample,p)); furthest=p.Along; }
                    continue;
                }
                var previous=points[^1];
                // Never infer order at crossings or accept a reverse traversal.
                double change=p.Along-previous.P.Along;
                if(change < -.5 || p.Along < furthest-2 || change > Math.Max(8, sample.SpeedKmh/3.6*(sample.TimeSeconds-previous.S.TimeSeconds)*2+3))
                { Clear(); continue; }
                points.Add((sample,p));
                furthest=Math.Max(furthest,p.Along);
                if(p.Along/total < .95) continue;
                var pass=Measure(session.Id,section,points,total);
                if(pass is not null) result.Passes.Add(pass);
                Clear();
            }
        }
        result.Summary=result.Passes.Count==0
            ? "No complete matched pass. Record another forward pass covering the marked start and end, at useful speed, with fresh position and pedal data. Gaps, resets, backward travel and ambiguous routes are excluded."
            : $"{result.Passes.Count} complete forward pass(es). Goals use this saved section revision. Road width and safe wider-line space remain unknown; offsets describe travel relative to the recorded reference.";
        return result;
    }

    private static SectionPass? Measure(string session,TrackSection section,List<(TelemetrySample S,Projection P)> points,double length)
    {
        double time=points[^1].S.TimeSeconds-points[0].S.TimeSeconds;
        double coverage=(points[^1].P.Along-points[0].P.Along)/length;
        if(time<2 || coverage<.9 || points.Count/time<10) return null;
        var weights=new double[Bins]; var speeds=new double[Bins]; var throttles=new double[Bins]; var brakes=new double[Bins]; var clutches=new double[Bins]; var leftShares=new double[Bins];
        double speed=0,throttle=0,brake=0,clutch=0,angle=0,inGoal=0,gear=0,offset=0,error=0;
        for(int i=1;i<points.Count;i++)
        {
            var(s,p)=points[i]; double dt=s.TimeSeconds-points[i-1].S.TimeSeconds;
            int bin=Math.Clamp((int)(p.Along/length*Bins),0,Bins-1);
            weights[bin]+=dt; speeds[bin]+=s.SpeedKmh*dt; throttles[bin]+=s.Throttle*dt; brakes[bin]+=s.Brake*dt; clutches[bin]+=s.Clutch*dt;
            if(s.SlipAngleDeg < -10) leftShares[bin]+=dt;
            speed+=s.SpeedKmh*dt; throttle+=s.Throttle*dt; brake+=s.Brake*dt; clutch+=s.Clutch*dt;
            angle+=Math.Abs(s.SlipAngleDeg)*dt;
            if(section.MinimumAngle is double min && Math.Abs(s.SlipAngleDeg)>=min && Math.Abs(s.SlipAngleDeg)<=section.MaximumAngle) inGoal+=dt;
            if(section.PreferredGear is int preferred && s.Gear-1==preferred) gear+=dt;
            offset+=p.Offset*dt; error+=Math.Abs(p.Offset-section.TargetOffsetM)*dt;
        }
        if(weights.Any(w=>w<=0)) return null;
        for(int b=0;b<Bins;b++) { speeds[b]/=weights[b]; throttles[b]/=weights[b]; brakes[b]/=weights[b]; clutches[b]/=weights[b]; leftShares[b]/=weights[b]; }
        return new SectionPass { SessionId=session, SectionId=section.Id, Start=points[0].S.TimeSeconds,End=points[^1].S.TimeSeconds,
            Coverage=coverage,Speed=speed/time,Throttle=throttle/time,Brake=brake/time,Clutch=clutch/time,BodyAngle=angle/time,
            AngleInGoalPct=section.MinimumAngle.HasValue?inGoal/time*100:null,PreferredGearPct=section.PreferredGear.HasValue?gear/time*100:null,
            LineOffset=offset/time,LineError=error/time,AlignmentUncertainty=points.Max(x=>x.S.Position!.AlignmentUncertaintySeconds),
            Path=points.Select(x=>Point(x.S.Position!)).ToList(),Speeds=speeds,Throttles=throttles,Brakes=brakes,Clutches=clutches,LeftShares=leftShares };
    }

    public static SectionComparison Compare(SectionPass before,SectionPass after,TrackSection section,
        SavedTelemetrySession beforeRun,SavedTelemetrySession afterRun)
    {
        var holds=new List<string>();
        if(before.SessionId!=beforeRun.Session.Id || after.SessionId!=afterRun.Session.Id ||
            before.SectionId!=section.Id || after.SectionId!=section.Id || before.Direction!=after.Direction)
            holds.Add("Section revision, recording or travel direction does not match.");
        if(before.SessionId==after.SessionId && after.Start<=before.End) holds.Add("Choose a distinct later pass.");
        var pairs=new[] {(before.Speeds,after.Speeds,8d,.2), (before.Throttles,after.Throttles,.2,0d),
            (before.Brakes,after.Brakes,.15,0d),(before.Clutches,after.Clutches,.2,0d),(before.LeftShares,after.LeftShares,.25,0d)};
        if(pairs.Any(p=>p.Item1.Length!=Bins||p.Item2.Length!=Bins || p.Item1.Zip(p.Item2).Any(v=>!double.IsFinite(v.First)||!double.IsFinite(v.Second)||Math.Abs(v.First-v.Second)>Math.Max(p.Item3,Math.Abs(v.First)*p.Item4))))
            holds.Add("Speed, pedal use or drift direction differs within the section. Repeat the line with similar inputs through all ten route intervals.");
        if(before.Coverage<.9||after.Coverage<.9) holds.Add("Section coverage is incomplete. Drive through both marked endpoints again.");
        RunComparison? existing=null;
        if(before.SessionId!=after.SessionId)
        {
            existing=new RunComparisonEngine().Compare(beforeRun,afterRun);
            if(!existing.Comparable) holds.AddRange(existing.Limitations);
        }
        string attribution=existing is null ? "Repeated passes in one run describe consistency; they cannot test a setup change." :
            existing.RecommendationTestTracked || existing.DriverTestTracked
                ? "Existing exact-test attribution checks matched. These path observations remain associations; they do not prove a setup caused them."
                : "Exact setup-test attribution is unavailable: "+existing.TestMatchSummary+" These paths cannot support a setup recommendation.";
        string values=$"Body angle: {before.BodyAngle:0.0}° → {after.BodyAngle:0.0}°. "+
            (before.AngleInGoalPct.HasValue?$"Time in section angle goal: {before.AngleInGoalPct:0}% → {after.AngleInGoalPct:0}%. ":"")+
            $"Signed map offset: {before.LineOffset:0.0} m → {after.LineOffset:0.0} m; mean distance from line aim: {before.LineError:0.0} m → {after.LineError:0.0} m. "+
            (before.PreferredGearPct.HasValue?$"Preferred forward gear: {before.PreferredGearPct:0}% → {after.PreferredGearPct:0}%. ":"")+
            $"Maximum timing uncertainty: {Math.Max(before.AlignmentUncertainty,after.AlignmentUncertainty)*1000:0} ms. "+
            "Body angle and route offset are different measurements. No tyre forces, hands-off self-steer or safe road width are established.";
        return new(holds.Count==0,(holds.Count==0 ? "Matched section passes. " : "Comparison held: "+string.Join(" ",holds.Distinct())+" Descriptive values only. ")+values+" "+attribution);
    }
}
