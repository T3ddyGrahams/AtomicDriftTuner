using System.IO;
using System.IO.MemoryMappedFiles;
using System.Reflection;
using System.Text;
using System.Text.Json;
using AtomicDriftTuner.Engine;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

internal static class TrackSectionChecks
{
    private static void Check(bool b,string why) { if(!b) throw new Exception(why); }
    private static TrackSection Section(TelemetrySession run) => TrackSectionEngine.Mark(run,1,7,"Long corner",30,45,2,0,"Follow reference");
    private static SavedTelemetrySession Saved(TelemetrySession s) => new() { Session=s,Analysis=new TelemetryAnalyzer().Analyze(s) };
    private static void Reject(Action action) { try { action(); } catch(InvalidDataException) { return; } throw new Exception("Expected rejection"); }
    public static void Run(Action<string,Action> test,string root)
    {
        test("track long recording has bounded route matching cost",()=>
        {
            var s=TrackFixture.Run(50,4,0,240);var timer=System.Diagnostics.Stopwatch.StartNew();
            var section=TrackSectionEngine.Mark(s,1,239,"Long route",null,null,null,0,"Follow reference");
            var result=TrackSectionEngine.Analyze(s,section);
            Check(result.Passes.Count==4 && timer.Elapsed.TotalSeconds<20,"Long-route matching failed or exceeded 20 seconds");
            Console.WriteLine($"Track stress: {s.Samples.Count:N0} frames, {section.Route.Count:N0} reference points, {timer.Elapsed.TotalSeconds:0.000}s on this PC.");
        });
        test("main track tools retain existing ADT storage and remote identity",()=>
        {
            var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AtomicDriftTuner");
            Check(typeof(TelemetrySessionStore).Assembly.GetName().Name == "AtomicDriftTuner", "Developer executable identity leaked");
            Check(new TelemetrySessionStore().RootDirectory == Path.Combine(data, "TelemetrySessions"), "Existing recordings disconnected");
            Check(new RunHistoryStore().RootDirectory == Path.Combine(data, "RunHistory"), "Existing run history disconnected");
            Check(new TrackSectionStore().RootDirectory == Path.Combine(data, "TrackSections"), "Track history outside normal ADT data");
            foreach(var type in new[]{typeof(AppSettingsStore),typeof(CalibrationStore),typeof(CarBehaviorProfileStore),typeof(AzomRevertStore)})
            {
                var instance=Activator.CreateInstance(type)!;
                foreach(var f in type.GetFields(BindingFlags.Instance|BindingFlags.NonPublic).Where(f=>f.FieldType==typeof(string)))
                    if(f.GetValue(instance) is string path && Path.IsPathFullyQualified(path)) Check(path.StartsWith(data),type.Name+" changed data path");
            }
            Check(RemoteServerService.DefaultPort==5190,"Existing remote port changed");
            Check(TrackPositionReader.RequestMap == @"Local\ADT.TrackPosition.Request.v1" && TrackPositionReader.ResponseMap == @"Local\ADT.TrackPosition.Position.v1", "Wrong production position channel");
        });
        test("track complete repeated passes retain distinct section goals",()=>
        {
            var s=TrackFixture.Run();var section=Section(s);var result=TrackSectionEngine.Analyze(s,section);
            Check(result.Passes.Count==3,result.Summary);
            Check(result.Passes[0].Start>=.96 && result.Passes[0].End<=7,"Approach or exit outside the marked section entered evidence");
            Check(result.Passes.All(p=>p.AngleInGoalPct>99.9&&p.PreferredGearPct>99.9&&Math.Abs(p.LineOffset)<.001),"Wrong units or goals");
            var saved=Saved(s);var comparison=TrackSectionEngine.Compare(result.Passes[0],result.Passes[1],section,saved,saved);
            Check(comparison.Comparable&&comparison.Findings.Contains("cannot test a setup change"),comparison.Findings);
            Check(!TrackSectionEngine.Compare(result.Passes[0],result.Passes[0],section,saved,saved).Comparable,"Same pass compared");
        });
        test("track angle and path offset stay independent and time weighted",()=>
        {
            var reference=TrackFixture.Run();var section=Section(reference);
            var a=TrackSectionEngine.Analyze(TrackFixture.Run(25,1,2),section).Passes.Single();
            var b=TrackSectionEngine.Analyze(TrackFixture.Run(100,1,2),section).Passes.Single();
            Check(Math.Abs(a.BodyAngle-b.BodyAngle)<.001&&Math.Abs(a.LineOffset-2)<.001&&Math.Abs(a.BodyAngle-35)<.001,"Angle became width");
            Check(Math.Abs(a.LineError-b.LineError)<.001&&Math.Abs(a.Speed-b.Speed)<.001,"Sample-rate weighted");
        });
        test("track identity and travel direction mismatch cannot supply passes",()=>
        {
            var refRun=TrackFixture.Run();var section=Section(refRun);
            foreach(string kind in new[]{"track","layout","car","backward","unknown","reverse-route","overpass"})
            {
                var s=TrackFixture.Run(50,1);
                foreach(var f in s.Samples)
                {
                    if(kind=="track") f.Position=f.Position! with { Track="other" };
                    if(kind=="layout") f.Position=f.Position! with { Layout="other" };
                    if(kind=="car") f.Position=f.Position! with { Car="other" };
                    if(kind=="backward") f.LongitudinalVelocityMs=-10;
                    if(kind=="unknown") f.LongitudinalVelocityMs=null;
                    if(kind=="reverse-route") f.Position=f.Position! with { X=100-f.Position!.X };
                    if(kind=="overpass") f.Position=f.Position! with { Y=8 };
                }
                Check(TrackSectionEngine.Analyze(s,section).Passes.Count==0,"Accepted "+kind);
            }
        });
        test("track gaps teleports frozen positions resets invalid motion and exclusions split passes",()=>
        {
            var section=Section(TrackFixture.Run());
            foreach(string kind in new[]{"gap","teleport","frozen","reset","nan","pit","ai","damage","position-gap"})
            {
                var s=TrackFixture.Run(50,1);
                foreach(var f in s.Samples.Where(f=>f.TimeSeconds>=3&&f.TimeSeconds<=4))
                {
                    if(kind=="gap") f.TimeSeconds+=1;
                    if(kind=="teleport") f.Position=f.Position! with { X=f.Position!.X+200 };
                    if(kind=="frozen") f.Position=f.Position! with { X=37.5, SourceTimeMs=3000 };
                    if(kind=="reset") f.PacketId=1;
                    if(kind=="nan") f.SpeedKmh=double.NaN;
                    if(kind=="pit") f.PitLimiterOn=true;
                    if(kind=="ai") f.IsAiControlled=true;
                    if(kind=="damage") f.DamageTotal=10;
                    if(kind=="position-gap") f.Position=null;
                }
                Check(TrackSectionEngine.Analyze(s,section).Passes.Count==0,"Bridged "+kind);
            }
        });
        test("track low spatial coverage incomplete endpoints and ambiguous routes held",()=>
        {
            var s=TrackFixture.Run(50,1);var section=Section(s);
            s.Samples.RemoveAll(f=>f.TimeSeconds<3||f.TimeSeconds>6);
            Check(TrackSectionEngine.Analyze(s,section).Passes.Count==0,"Partial counted");
            Reject(()=>TrackSectionEngine.Mark(s,1,7,"bad",null,null,null,0,"Follow reference"));
            var cross=Section(TrackFixture.Run());cross.Route=[];
            foreach(var (a,b) in new[]{(new RoutePoint(0,0,0),new RoutePoint(50,0,50)),(new RoutePoint(50,0,50),new RoutePoint(0,0,50)),(new RoutePoint(0,0,50),new RoutePoint(50,0,0))})
                for(int i=0;i<20;i++) cross.Route.Add(new(a.X+(b.X-a.X)*i/20,a.Y,a.Z+(b.Z-a.Z)*i/20));
            Reject(()=>TrackSectionEngine.Validate(cross));
        });
        test("track matched intervals reject different speed and pedal technique",()=>
        {
            var s=TrackFixture.Run();var section=Section(s);
            foreach(var f in s.Samples.Where(f=>f.TimeSeconds>12&&f.TimeSeconds<14)) f.Throttle=1;
            var passes=TrackSectionEngine.Analyze(s,section).Passes;var saved=Saved(s);
            Check(!TrackSectionEngine.Compare(passes[0],passes[1],section,saved,saved).Comparable,"Mismatched pedal timing accepted");
            Check(TrackSectionEngine.Compare(passes[0],passes[2],section,saved,saved).Comparable,"Independent repeat lost");
        });
        test("track cross-run review retains existing attribution and quality holds",()=>
        {
            var a=TrackFixture.Run();var b=TrackFixture.Run();var section=Section(a);
            var ap=TrackSectionEngine.Analyze(a,section).Passes[0];var bp=TrackSectionEngine.Analyze(b,section).Passes[0];
            var c=TrackSectionEngine.Compare(ap,bp,section,Saved(a),Saved(b));
            Check(!c.Comparable&&c.Findings.Contains("Driver identity")&&c.Findings.Contains("Exact setup-test attribution is unavailable"),c.Findings);
        });
        test("track goals are immutable append-only data and malformed files preserved",()=>
        {
            string dir=Path.Combine(root,"sections");var store=new TrackSectionStore(dir);var section=Section(TrackFixture.Run());store.Save(section);
            var path=Path.Combine(dir,section.Id+".json");var bytes=File.ReadAllBytes(path);
            try { store.Save(section);throw new Exception("Overwrote immutable goal"); } catch(IOException) { }
            Check(bytes.SequenceEqual(File.ReadAllBytes(path)),"Goal changed");
            File.WriteAllText(Path.Combine(dir,"bad.json"),"{\"Route\":null}");
            var loaded=store.Load(out var issue);Check(loaded.Count==1&&issue.Length>0&&File.Exists(Path.Combine(dir,"bad.json")),"Corruption not preserved");
            Check(loaded[0].PreferredGear==2&&loaded[0].MinimumAngle==30,"Goal lost");
        });
        test("track legacy recordings remain readable with unchanged tuning analysis",()=>
        {
            var s=TrackFixture.Run();var withPosition=JsonSerializer.Serialize(new TelemetryAnalyzer().Analyze(s));
            foreach(var f in s.Samples)f.Position=null;
            Check(withPosition==JsonSerializer.Serialize(new TelemetryAnalyzer().Analyze(s)),"Location altered existing tuning calculations");
            Check(TrackSectionEngine.Paths(s).Count==0,"Invented legacy positions");
            var store=new TelemetrySessionStore();typeof(TelemetrySessionStore).GetField("<RootDirectory>k__BackingField",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(store,Path.Combine(root,"legacy-track"));
            var saved=store.Save(s,new TelemetryAnalyzer().Analyze(s));var bytes=File.ReadAllBytes(saved.JsonPath);
            Check(store.TryLoad(saved.JsonPath)!.Session.Samples.All(f=>f.Position is null),"Legacy lost");
            Check(bytes.SequenceEqual(File.ReadAllBytes(saved.JsonPath)),"History rewritten");
        });
        test("track position JSON CSV and recording copies retain provenance",()=>
        {
            var s=TrackFixture.Run(25,1);var original=s.Samples[0];var copy=original.Copy();
            original.Position=original.Position! with { X=500 };Check(copy.Position!.X==0,"Shared mutable position");original.Position=copy.Position;
            var store=new TelemetrySessionStore();typeof(TelemetrySessionStore).GetField("<RootDirectory>k__BackingField",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(store,Path.Combine(root,"position-track"));
            var saved=store.Save(s,new TelemetryAnalyzer().Analyze(s));var loaded=store.TryLoad(saved.JsonPath)!;
            Check(loaded.Session.Samples[1].Position==s.Samples[1].Position,"Capture metadata lost");
            Check(File.ReadAllLines(saved.CsvPath)[0].Contains("position_uncertainty_s"),"CSV location missing");
        });
        test("track response rejects stale wrong nonce malformed and nonfinite evidence",()=>
        {
            var bytes=Response(42);Check(TrackPositionReader.Decode(bytes,42,.02,null) is { Track:"test_track",Layout:"",SplineProgress:null },"Valid missing spline rejected");
            Check(TrackPositionReader.Decode(bytes,43,.02,null) is null,"Wrong nonce");
            Check(TrackPositionReader.Decode(bytes,42,.09,null) is null,"Late response");
            Check(TrackPositionReader.Decode(bytes,42,.02,1000) is null,"Frozen source");
            BitConverter.GetBytes(double.NaN).CopyTo(bytes,32);Check(TrackPositionReader.Decode(bytes,42,.02,null) is null,"NaN position");
            bytes=Response(42);bytes[232]=(byte)'/';Check(TrackPositionReader.Decode(bytes,42,.02,null) is null,"Invalid layout became default");
        });
        test("track real shared memory request response consumes each fresh pose once",()=>
        {
            using var request=MemoryMappedFile.CreateNew(null,8);using var response=MemoryMappedFile.CreateNew(null,TrackPositionReader.ResponseSize);
            using var rv=request.CreateViewAccessor();using var pv=response.CreateViewAccessor();using var reader=new TrackPositionReader();
            typeof(TrackPositionReader).GetField("_requestView",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(reader,rv);
            typeof(TrackPositionReader).GetField("_responseView",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(reader,pv);
            Check(reader.Read(1)==null,"Unrequested sample");int token=rv.ReadInt32(0);var bytes=Response(token);pv.WriteArray(0,bytes,0,bytes.Length);
            Check(reader.Read(1.02) is not null,"Aligned response lost");Check(reader.Read(1.04) is null,"Reused stale pose");
            bytes=Response(rv.ReadInt32(0));BitConverter.GetBytes(3).CopyTo(bytes,0);pv.WriteArray(0,bytes,0,bytes.Length);
            Check(reader.Read(1.06) is null,"Torn odd response accepted");
        });
    }
    public static byte[] Response(int token)
    {
        var b=new byte[TrackPositionReader.ResponseSize];
        foreach(var (offset,value) in new[]{(0,2),(4,1),(8,token),(12,1)})BitConverter.GetBytes(value).CopyTo(b,offset);
        foreach(var (offset,value) in new[]{(16,1000d),(24,.005),(32,10d),(40,0d),(48,20d),(56,-1d),(88,-1d),(96,-1d)})BitConverter.GetBytes(value).CopyTo(b,offset);
        Encoding.ASCII.GetBytes("test_track").CopyTo(b,104);Encoding.ASCII.GetBytes("test_car").CopyTo(b,360);return b;
    }
}
