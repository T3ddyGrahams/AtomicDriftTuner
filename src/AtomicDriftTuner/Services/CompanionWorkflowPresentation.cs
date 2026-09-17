using System.Security.Cryptography;
using System.Text.Json;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

public static class CompanionWorkflowPresentation
{
    public static string Version(object value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
    public static string RecommendationId(string sessionId, AssistantRecommendation recommendation) => Version(new { sessionId, recommendation });

    public static CompanionWorkflowReport Report(TuningAssistantReport report, string sessionId, string baselineId,
        TuningFocus focus, bool mayPlan, bool reviewed) => new()
    {
        SessionId = sessionId, BaselineSessionId = baselineId, Goal = report.NextStep.Goal,
        Noticed = report.NextStep.Noticed, Instruction = report.NextStep.Instruction, Why = report.NextStep.Why,
        Confidence = report.NextStep.Confidence, ReviewSaved = reviewed,
        Recommendations = report.Recommendations.Where(r => TuningFocusOptions.Allows(focus, r)).Select(r => new CompanionWorkflowRecommendation
        {
            Id = RecommendationId(sessionId, r), Domain = r.Domain, Change = r.Change, Why = r.Why,
            Confidence = r.Confidence, CanSelect = mayPlan && r.Confidence != "LOW",
            DisabledReason = r.Confidence == "LOW" ? "Collect more reliable evidence before planning this change from the companion."
                : !mayPlan ? "Follow Your next step or prepare the matching idle recorder before planning this test." : ""
        }).ToList(),
        Comparison = new()
        {
            Comparable = report.Outcome.Comparable, Verdict = report.Outcome.Verdict,
            Summary = report.Outcome.Summary, Limitations = report.Outcome.Limitations.ToList()
        }
    };

    public static string? Reject(CompanionWorkflowCommand command, CompanionWorkflowState state)
    {
        if (command.Action is not ("prepare" or "confirm" or "findings" or "plan" or "review")) return "Unknown workflow command.";
        if (!state.Available) return state.Message;
        if (command.ControlVersion != state.ControlVersion || command.ControlVersion?.Length != 64)
            return "The workflow changed. Refresh status before trying again.";
        if (command.Action == "prepare" && !state.CanPrepare || command.Action == "confirm" && !state.CanConfirm ||
            command.Action == "findings" && !state.CanReadFindings || command.Action == "review" && !state.CanSaveReview)
            return state.Message;
        if (command.Action == "confirm" && !command.TuneConfirmed)
            return "Confirm only after loading the attached setup and checking the displayed settings in AC.";
        if (command.Action == "findings" && command.SessionId != state.SavedSessionId ||
            command.Action is "plan" or "review" && (state.Report is null || command.SessionId != state.Report.SessionId))
            return "The selected saved run changed. Read its findings again.";
        if (command.Action == "plan" && state.Report?.Recommendations.Any(r => r.Id == command.RecommendationId && r.CanSelect) != true)
            return "That recommendation is not available for this run and workflow. Read the current findings first.";
        if (command.Action == "review" && (!new[] { "Better", "Worse", "No noticeable difference", "Tradeoff" }.Contains(command.DriverRating) ||
            !new[] { "Undecided", "Keep and verify", "Revert manually", "Test again" }.Contains(command.NextAction) ||
            command.Notes is null || command.Notes.Length > 2000))
            return "Choose a driver rating and next action, with notes no longer than 2,000 characters.";
        return null;
    }
}
