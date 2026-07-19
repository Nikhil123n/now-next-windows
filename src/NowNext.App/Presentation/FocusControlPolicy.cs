using NowNext.Core.Sessions;

namespace NowNext.App.Presentation;

public sealed record FocusControlAvailability(
    string PauseResumeLabel,
    bool CanPauseOrResume,
    bool CanFinish,
    bool CanPark,
    bool CanBeginLanding,
    bool CanContinueOvertime,
    bool CanExtend,
    bool RequiresRecovery);

public static class FocusControlPolicy
{
    public static FocusControlAvailability For(FocusSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        SessionState state = session.State;
        bool isPaused = state is PausedSessionState;
        bool isActiveDecision = state is
            FocusingSessionState
            or PausedSessionState
            or LimitReachedSessionState
            or OvertimeSessionState
            or LandingSessionState
            or RecoveryRequiredSessionState { InterruptedPhase: not ActiveSessionPhase.Break };
        bool focusBoundary = state is LimitReachedSessionState
        {
            Boundary: SessionBoundary.FocusLimit,
        };

        return new FocusControlAvailability(
            isPaused ? "Resume" : "Pause",
            state is FocusingSessionState or OvertimeSessionState or PausedSessionState,
            isActiveDecision,
            isActiveDecision,
            state is OvertimeSessionState || focusBoundary,
            focusBoundary,
            state is OvertimeSessionState or LandingSessionState or LimitReachedSessionState,
            state is RecoveryRequiredSessionState);
    }
}
