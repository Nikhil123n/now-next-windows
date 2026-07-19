using Windows.System.Display;

namespace NowNext.App.WindowsIntegration;

public sealed class WindowsDisplayKeepAwakeController : IKeepAwakeController, IDisposable
{
    private readonly DisplayRequest _displayRequest = new();
    private bool _disposed;

    public bool IsActive { get; private set; }

    public void Acquire()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsActive)
        {
            return;
        }

        _displayRequest.RequestActive();
        IsActive = true;
    }

    public void Release()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsActive)
        {
            return;
        }

        _displayRequest.RequestRelease();
        IsActive = false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (IsActive)
        {
            try
            {
                _displayRequest.RequestRelease();
            }
            catch (Exception exception) when (
                exception is InvalidOperationException
                    or System.Runtime.InteropServices.ExternalException)
            {
                // Process exit cannot be delayed if Windows rejects a final release.
            }
            finally
            {
                IsActive = false;
            }
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
