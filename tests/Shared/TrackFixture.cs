using AtomicDriftTuner.Models;

internal static class TrackFixture
{
    public static TelemetrySession Run(int rate=50,int passes=3,double offset=0,int seconds=8)
    {
        var run=new TelemetrySession { CarFolder="test_car", CarName="Track fixture", DriftPack="Test", StartedUtc=DateTime.UtcNow };
        for(int pass=0;pass<passes;pass++)
            for(int i=0;i<=seconds*rate;i++)
            {
                double t=pass*(seconds+2d)+i/(double)rate, x=i/(double)rate*12.5;
                run.Samples.Add(new TelemetrySample { TimeSeconds=t,PacketId=run.Samples.Count+1, HasExtendedSignals=true,
                    SpeedKmh=45,LongitudinalVelocityMs=10,Throttle=.6,Brake=.02,Clutch=0,Gear=3,Rpm=5000,
                    SlipAngleDeg=35,YawRateDegPerSec=20,SteeringAngleDeg=60,FrontWheelSlipAvg=.2,RearWheelSlipAvg=.8,
                    Position=new TrackPosition { Track="test_track",Layout="layout_a",Car="test_car",X=x,Y=0,Z=offset,
                        SourceTimeMs=t*1000,AlignmentUncertaintySeconds=.025 } });
            }
        return run;
    }
    public static TelemetrySession CurvedRun()
    {
        var run=Run();
        foreach(var s in run.Samples)
        {
            int pass=(int)(s.TimeSeconds/10);double theta=(s.TimeSeconds-pass*10)/8*Math.PI/2;
            double radius=pass==1?58:60;
            s.Position=s.Position! with { X=radius*Math.Cos(theta), Z=radius*Math.Sin(theta) };
            s.SpeedKmh=radius*Math.PI/2/8*3.6;
            if(pass==1)s.SlipAngleDeg=40;
        }
        return run;
    }
}
