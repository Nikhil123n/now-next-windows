namespace NowNext.App;

public interface IKeepAwakeController
{
    public void Release();
}

public sealed class NoOpKeepAwakeController : IKeepAwakeController
{
    public void Release()
    {
    }
}
