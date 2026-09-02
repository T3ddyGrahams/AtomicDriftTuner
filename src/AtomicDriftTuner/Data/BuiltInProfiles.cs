using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Data;

public static class BuiltInProfiles
{
    public static List<HardwareProfile> Hardware() =>
    [
        new() { Id="moza-r3", Manufacturer="MOZA", Model="R3", PeakTorqueNm=3.9 },
        new() { Id="moza-r5", Manufacturer="MOZA", Model="R5", PeakTorqueNm=5.5 },
        new() { Id="moza-r9", Manufacturer="MOZA", Model="R9", PeakTorqueNm=9.0 },
        new() { Id="moza-r12", Manufacturer="MOZA", Model="R12 / R12 V2", PeakTorqueNm=12.0 },
        new() { Id="moza-r16", Manufacturer="MOZA", Model="R16", PeakTorqueNm=16.0 },
        new() { Id="moza-r21", Manufacturer="MOZA", Model="R21", PeakTorqueNm=21.0 },
        new() { Id="moza-r25", Manufacturer="MOZA", Model="R25 Ultra", PeakTorqueNm=25.0 },
        new() { Id="custom-base", Manufacturer="Custom", Model="Direct Drive Base", PeakTorqueNm=10.0, IsCustom=true }
    ];

    public static List<SteeringWheelProfile> Wheels() =>
    [
        new() { Id="moza-cs-pro", Manufacturer="MOZA", Model="CS Pro", DiameterMm=325, InertiaFactor=1.04, IsRound=true },
        new() { Id="moza-ks-pro", Manufacturer="MOZA", Model="KS Pro", DiameterMm=300, InertiaFactor=0.86, IsRound=false },
        new() { Id="moza-cs-v2p", Manufacturer="MOZA", Model="CS V2P", DiameterMm=330, InertiaFactor=1.08, IsRound=true },
        new() { Id="moza-rs-v2", Manufacturer="MOZA", Model="RS V2", DiameterMm=330, InertiaFactor=1.10, IsRound=true },
        new() { Id="moza-ks", Manufacturer="MOZA", Model="KS", DiameterMm=300, InertiaFactor=0.86, IsRound=false },
        new() { Id="moza-esx", Manufacturer="MOZA", Model="ES / ES Lite / ESX", DiameterMm=280, InertiaFactor=0.78, IsRound=true },
        new() { Id="moza-vision-gs", Manufacturer="MOZA", Model="Vision GS", DiameterMm=310, InertiaFactor=0.94, IsRound=false },
        new() { Id="moza-gs-v2p", Manufacturer="MOZA", Model="GS V2P GT", DiameterMm=300, InertiaFactor=0.91, IsRound=false },
        new() { Id="moza-tsw", Manufacturer="MOZA", Model="TSW", DiameterMm=400, InertiaFactor=1.35, IsRound=true },
        new() { Id="custom-wheel", Manufacturer="Custom", Model="Other / Aftermarket Wheel", DiameterMm=330, InertiaFactor=1.0, IsRound=true, IsCustom=true }
    ];

    public static List<DriftPackProfile> DriftPacks() =>
    [
        new() { Id="vdc", Name="VDC Public 5.0", Category="Competition / Pro", GripBias=.12, SelfSteerBias=.02, DampingBias=.03, DetailBias=.08 },
        new() { Id="gravy", Name="Gravy Garage V2", Category="Street / Tandem", GripBias=-.05, SelfSteerBias=.07, DampingBias=-.03, DetailBias=0 },
        new() { Id="swarm", Name="Team SWARM V3.2", Category="Street / Tandem", GripBias=0, SelfSteerBias=.05, DampingBias=-.01, DetailBias=.02 },
        new() { Id="adl", Name="ADL (Elite / Pro-Am)", Category="Competition", GripBias=.14, SelfSteerBias=.01, DampingBias=.04, DetailBias=.08 },
        new() { Id="wdts", Name="WDT / WDTS", Category="Street / Training", GripBias=-.02, SelfSteerBias=.03, DampingBias=0, DetailBias=-.02 },
        new() { Id="dwg", Name="Deathwish Garage", Category="Street / Tandem", GripBias=0, SelfSteerBias=.04, DampingBias=0, DetailBias=0 },
        new() { Id="custom-pack", Name="Custom / Other Pack", Category="Custom", GripBias=0, SelfSteerBias=0, DampingBias=0, DetailBias=0, IsCustom=true }
    ];

    public static List<CarProfile> Cars() =>
    [
        // VDC: generic competition templates because individual public-pack cars can change by version.
        new() { Id="vdc-generic", PackId="vdc", DisplayName="VDC - Current Car / Generic Pro", MassKg=1350, PowerHp=900, TorqueNm=950, SteeringLockPerSideDeg=65, CasterDeg=8.5, FrontTireWidthMm=275, RearTireWidthMm=285, Grip=GripLevel.High },
        new() { Id="vdc-s15", PackId="vdc", DisplayName="VDC - S15 Template", MassKg=1320, PowerHp=900, TorqueNm=950, SteeringLockPerSideDeg=65, CasterDeg=8.5, FrontTireWidthMm=275, RearTireWidthMm=285, Grip=GripLevel.High },
        new() { Id="vdc-a90", PackId="vdc", DisplayName="VDC - A90 Template", MassKg=1450, PowerHp=950, TorqueNm=1050, SteeringLockPerSideDeg=65, CasterDeg=8.5, FrontTireWidthMm=285, RearTireWidthMm=295, Grip=GripLevel.High },

        // Gravy Garage / AiO representative chassis.
        new() { Id="gravy-s13", PackId="gravy", DisplayName="Gravy Garage - S13", MassKg=1140, PowerHp=330, TorqueNm=480, SteeringLockPerSideDeg=60, CasterDeg=7.0, FrontTireWidthMm=235, RearTireWidthMm=245, Grip=GripLevel.Medium },
        new() { Id="gravy-s15", PackId="gravy", DisplayName="Gravy Garage - S15", MassKg=1250, PowerHp=360, TorqueNm=500, SteeringLockPerSideDeg=60, CasterDeg=7.0, FrontTireWidthMm=245, RearTireWidthMm=255, Grip=GripLevel.Medium },
        new() { Id="gravy-laurel", PackId="gravy", DisplayName="Gravy Garage - Laurel / C33", MassKg=1350, PowerHp=360, TorqueNm=510, SteeringLockPerSideDeg=60, CasterDeg=7.0, FrontTireWidthMm=245, RearTireWidthMm=255, Grip=GripLevel.Medium },
        new() { Id="gravy-e36", PackId="gravy", DisplayName="Gravy Garage - E36 Template", MassKg=1250, PowerHp=350, TorqueNm=470, SteeringLockPerSideDeg=60, CasterDeg=7.0, FrontTireWidthMm=235, RearTireWidthMm=245, Grip=GripLevel.Medium },
        new() { Id="gravy-jzx90", PackId="gravy", DisplayName="Gravy Garage - JZX90 Template", MassKg=1450, PowerHp=390, TorqueNm=540, SteeringLockPerSideDeg=60, CasterDeg=7.0, FrontTireWidthMm=245, RearTireWidthMm=255, Grip=GripLevel.Medium },

        // SWARM V3.2 representative cars from the current listed pack.
        new() { Id="swarm-s13", PackId="swarm", DisplayName="SWARM - S13 / Onevia", MassKg=1250, PowerHp=430, TorqueNm=520, SteeringLockPerSideDeg=60, CasterDeg=7.5, FrontTireWidthMm=245, RearTireWidthMm=255, Grip=GripLevel.Medium },
        new() { Id="swarm-e46", PackId="swarm", DisplayName="SWARM - BMW E46", MassKg=1400, PowerHp=450, TorqueNm=540, SteeringLockPerSideDeg=60, CasterDeg=7.5, FrontTireWidthMm=255, RearTireWidthMm=265, Grip=GripLevel.Medium },
        new() { Id="swarm-r32", PackId="swarm", DisplayName="SWARM - R32", MassKg=1350, PowerHp=450, TorqueNm=530, SteeringLockPerSideDeg=60, CasterDeg=7.5, FrontTireWidthMm=255, RearTireWidthMm=265, Grip=GripLevel.Medium },
        new() { Id="swarm-350z", PackId="swarm", DisplayName="SWARM - 350Z", MassKg=1500, PowerHp=450, TorqueNm=520, SteeringLockPerSideDeg=60, CasterDeg=7.5, FrontTireWidthMm=255, RearTireWidthMm=275, Grip=GripLevel.Medium },
        new() { Id="swarm-fd", PackId="swarm", DisplayName="SWARM - RX-7 FD", MassKg=1280, PowerHp=430, TorqueNm=500, SteeringLockPerSideDeg=60, CasterDeg=7.5, FrontTireWidthMm=245, RearTireWidthMm=255, Grip=GripLevel.Medium },

        // ADL: current competition / Pro-Am representative chassis.
        new() { Id="adl-s15", PackId="adl", DisplayName="ADL - S15", MassKg=1300, PowerHp=900, TorqueNm=950, SteeringLockPerSideDeg=65, CasterDeg=8.5, FrontTireWidthMm=275, RearTireWidthMm=285, Grip=GripLevel.High },
        new() { Id="adl-e46", PackId="adl", DisplayName="ADL - BMW E46", MassKg=1420, PowerHp=900, TorqueNm=950, SteeringLockPerSideDeg=65, CasterDeg=8.5, FrontTireWidthMm=275, RearTireWidthMm=285, Grip=GripLevel.High },
        new() { Id="adl-350z", PackId="adl", DisplayName="ADL - 350Z", MassKg=1500, PowerHp=900, TorqueNm=950, SteeringLockPerSideDeg=65, CasterDeg=8.5, FrontTireWidthMm=275, RearTireWidthMm=285, Grip=GripLevel.High },
        new() { Id="adl-a90", PackId="adl", DisplayName="ADL - A90 / GR Supra", MassKg=1500, PowerHp=950, TorqueNm=1000, SteeringLockPerSideDeg=65, CasterDeg=8.5, FrontTireWidthMm=285, RearTireWidthMm=295, Grip=GripLevel.High },

        new() { Id="wdts-generic", PackId="wdts", DisplayName="WDT / WDTS - Current Car / Generic", MassKg=1250, PowerHp=400, TorqueNm=480, SteeringLockPerSideDeg=60, CasterDeg=7.0, FrontTireWidthMm=235, RearTireWidthMm=245, Grip=GripLevel.Medium },
        new() { Id="dwg-generic", PackId="dwg", DisplayName="Deathwish Garage - Current Car / Generic", MassKg=1300, PowerHp=430, TorqueNm=510, SteeringLockPerSideDeg=60, CasterDeg=7.3, FrontTireWidthMm=245, RearTireWidthMm=255, Grip=GripLevel.Medium },
        new() { Id="custom-car", PackId="custom-pack", DisplayName="Custom Assetto Corsa Car", MassKg=1300, PowerHp=400, TorqueNm=450, SteeringLockPerSideDeg=60, CasterDeg=7.0, FrontTireWidthMm=265, RearTireWidthMm=265, Grip=GripLevel.Medium, IsCustom=true }
    ];

    public static List<DriftIntent> Intents() =>
    [
        new() { Kind=DriftStyleKind.Training, Name="Training", SelfSteer=.45, Stability=.88, Detail=.40, Weight=.55 },
        new() { Kind=DriftStyleKind.Realistic, Name="Realistic", SelfSteer=.64, Stability=.65, Detail=.72, Weight=.65 },
        new() { Kind=DriftStyleKind.FastSelfSteer, Name="Fast Self-Steer", SelfSteer=.92, Stability=.34, Detail=.62, Weight=.45 },
        new() { Kind=DriftStyleKind.Tandem, Name="Tandem", SelfSteer=.78, Stability=.72, Detail=.62, Weight=.58 },
        new() { Kind=DriftStyleKind.Competition, Name="Competition", SelfSteer=.84, Stability=.58, Detail=.92, Weight=.62 }
    ];
}
