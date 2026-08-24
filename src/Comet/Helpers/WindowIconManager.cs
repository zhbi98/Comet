using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices;

namespace Comet.Helpers;

internal sealed class WindowIconManager : IDisposable
{
    private const string EmbeddedIconResourceName = "Comet.Assets.CometTerminalIcon.ico";
    private const uint WmSetIcon = 0x0080;
    private const int IconSmall = 0;
    private const int IconBig = 1;
    private const int IconSmall2 = 2;
    private const int SystemMetricIconWidth = 11;
    private const int SystemMetricIconHeight = 12;
    private const uint DefaultDpi = 96;

    private readonly nint _windowIconHandle;
    private bool _isDisposed;

    private WindowIconManager(nint windowIconHandle)
    {
        _windowIconHandle = windowIconHandle;
    }

    public static ImageIconSource? CreateTitleBarIconSource()
    {
        using var iconStream = typeof(WindowIconManager).Assembly
            .GetManifestResourceStream(EmbeddedIconResourceName);
        if (iconStream is null)
        {
            return null;
        }

        using var randomAccessStream = iconStream.AsRandomAccessStream();
        var bitmapImage = new BitmapImage
        {
            DecodePixelWidth = 20,
            DecodePixelHeight = 20
        };
        bitmapImage.SetSource(randomAccessStream);

        return new ImageIconSource
        {
            ImageSource = bitmapImage
        };
    }

    public static WindowIconManager? Attach(nint windowHandle)
    {
        var executablePath = Environment.ProcessPath;
        if (windowHandle == 0 || string.IsNullOrWhiteSpace(executablePath))
        {
            return null;
        }

        var dpi = GetDpiForWindow(windowHandle);
        if (dpi == 0)
        {
            dpi = DefaultDpi;
        }

        var iconWidth = GetNextIconSize(GetSystemMetricsForDpi(SystemMetricIconWidth, dpi));
        var iconHeight = GetNextIconSize(GetSystemMetricsForDpi(SystemMetricIconHeight, dpi));
        var extractedIconCount = PrivateExtractIcons(
            executablePath,
            0,
            iconWidth,
            iconHeight,
            out var windowIconHandle,
            0,
            1,
            0);
        if (extractedIconCount is 0 or uint.MaxValue || windowIconHandle == 0)
        {
            return null;
        }

        // WinUI applies the last WM_SETICON handle to every icon role. Supplying
        // the DPI-sized large frame prevents the taskbar from enlarging a 16 px frame.
        _ = SendMessage(windowHandle, WmSetIcon, IconBig, windowIconHandle);
        _ = SendMessage(windowHandle, WmSetIcon, IconSmall, windowIconHandle);
        _ = SendMessage(windowHandle, WmSetIcon, IconSmall2, windowIconHandle);

        return new WindowIconManager(windowIconHandle);
    }

    private static int GetNextIconSize(int systemSize)
    {
        ReadOnlySpan<int> availableSizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
        foreach (var size in availableSizes)
        {
            if (size > systemSize)
            {
                return size;
            }
        }

        return availableSizes[^1];
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        if (_windowIconHandle != 0)
        {
            _ = DestroyIcon(_windowIconHandle);
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "PrivateExtractIconsW")]
    private static extern uint PrivateExtractIcons(
        string fileName,
        int iconIndex,
        int iconWidth,
        int iconHeight,
        out nint iconHandle,
        nint iconId,
        uint iconCount,
        uint flags);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint windowHandle);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);

    [DllImport("user32.dll")]
    private static extern nint SendMessage(
        nint windowHandle,
        uint message,
        nint wordParameter,
        nint longParameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint iconHandle);
}
