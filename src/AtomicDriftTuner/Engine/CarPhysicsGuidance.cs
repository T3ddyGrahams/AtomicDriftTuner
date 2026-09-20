using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Engine;

public static class CarPhysicsGuidance
{
    public static void Apply(CarSetupAnalysis analysis)
    {
        var physics = analysis.Physics;
        foreach (var parameter in analysis.Parameters)
        {
            parameter.PhysicsContext = "Base value not mapped.";
            if (physics?.Available != true) continue;
            var section = parameter.Section.ToUpperInvariant();
            var front = section.EndsWith("_LF") || section.EndsWith("_RF") || section.EndsWith("_FRONT");
            var rear = section.EndsWith("_LR") || section.EndsWith("_RR") || section.EndsWith("_REAR");
            var axle = front ? "FRONT" : rear ? "REAR" : "";
            CarPhysicsFact? fact = null;
            if (axle.Length > 0)
            {
                foreach (var key in new[] { "SPRING_RATE", "DAMP_FAST_BUMP", "DAMP_FAST_REBOUND", "DAMP_BUMP", "DAMP_REBOUND", "CAMBER", "TOE_OUT" })
                    if (section.StartsWith(key + "_")) fact = physics.Find("suspensions.ini", axle, key == "CAMBER" ? "STATIC_CAMBER" : key);
                if (section.StartsWith("ARB_")) fact = physics.Find("suspensions.ini", "ARB", axle);
                if (section.StartsWith("PRESSURE_")) fact = physics.Facts.FirstOrDefault(f => f.File == "tyres.ini" &&
                    (f.Section == axle || f.Section.StartsWith(axle + "_")) && f.Key == "PRESSURE_STATIC");
            }
            if (section is "DIFF_POWER" or "DIFF_COAST" or "DIFF_PRELOAD") fact = physics.Find("drivetrain.ini", "DIFFERENTIAL", section[5..]);
            if (section == "BRAKE_BIAS") fact = physics.Find("brakes.ini", "DATA", "FRONT_SHARE");
            if (section == "FINAL_RATIO") fact = physics.Find("drivetrain.ini", "GEARS", "FINAL");
            if (fact is not null)
            {
                parameter.PhysicsContext = fact.Display;
                parameter.Reason += " Base-car context: " + fact.Display + ". The recommendation is a change to the loaded setup VALUE; base physics units are not assumed to match saved clicks or indexes.";
            }
            if (physics.HasSetupDefinition && !physics.AdjustableSections.Contains(parameter.Section, StringComparer.OrdinalIgnoreCase))
            {
                parameter.RecommendedValue = parameter.CurrentValue;
                parameter.Reason = "Left unchanged: this saved control is not exposed in the imported setup.ini. ADT will not invent an adjustable control from a base physics value.";
                parameter.BlendStatus = "Not exposed";
            }
            if (section.StartsWith("DIFF_") && physics.DriveType is "FWD" or "AWD" or "AWD2")
            {
                parameter.RecommendedValue = parameter.CurrentValue;
                parameter.Reason = $"Left unchanged: imported drivetrain is {physics.DriveType}; ADT's current rear-drive differential advice is not verified for this drivetrain. Review the base values and tune this control manually.";
                parameter.BlendStatus = "Drivetrain check";
            }
        }
    }
}
