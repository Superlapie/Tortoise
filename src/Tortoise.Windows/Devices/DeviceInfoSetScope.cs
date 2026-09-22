using Tortoise.Windows.Interop;

namespace Tortoise.Windows.Devices;

internal sealed class DeviceInfoSetScope : IDisposable
{
    private IntPtr _handle;
    private bool _disposed;

    private DeviceInfoSetScope(IntPtr handle)
    {
        _handle = handle;
    }

    public IntPtr Handle => _handle;

    public static DeviceInfoSetScope Create()
    {
        var handle = SetupApiNative.CreateDeviceInfoList(IntPtr.Zero, IntPtr.Zero);
        if (handle == IntPtr.Zero || handle == new IntPtr(-1))
        {
            throw new InvalidOperationException("SetupDiCreateDeviceInfoList failed.");
        }

        return new DeviceInfoSetScope(handle);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_handle != IntPtr.Zero && _handle != new IntPtr(-1))
        {
            SetupApiNative.DestroyDeviceInfoList(_handle);
        }

        _handle = IntPtr.Zero;
        _disposed = true;
    }
}
