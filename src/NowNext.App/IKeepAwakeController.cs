namespace NowNext.App;

public interface IKeepAwakeController
{
    public bool IsActive { get; }

    public void Acquire();

    public void Release();
}

public sealed class NoOpKeepAwakeController : IKeepAwakeController
{
    public bool IsActive => false;

    public void Acquire()
    {
    }

    public void Release()
    {
    }
}
