namespace NowNext.App.Presentation;

public static class TimerColonPolicy
{
    public static bool GetNextVisibility(bool isVisible, bool reducedMotionEnabled)
    {
        return reducedMotionEnabled || !isVisible;
    }
}
