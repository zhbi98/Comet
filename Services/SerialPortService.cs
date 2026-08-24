using System.IO.Ports;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Comet.Models;

namespace Comet.Services;

public sealed record SerialPortSettings(
    string PortName,
    int BaudRate,
    int DataBits,
    Parity Parity,
    StopBits StopBits,
    Handshake Handshake,
    bool DtrEnable,
    bool RtsEnable);

public sealed class SerialBytesReceivedEventArgs(byte[] data) : EventArgs
{
    public byte[] Data { get; } = data;
}

public sealed class SerialPortService : IDisposable
{
    private const uint DigcfPresent = 0x00000002;
    private const uint SpdrpDeviceDescription = 0x00000000;
    private const uint SpdrpFriendlyName = 0x0000000C;
    private static readonly Guid PortsDeviceSetupClass = new("4D36E978-E325-11CE-BFC1-08002BE10318");
    private static readonly Regex PortNameSuffixPattern = new(
        @"\((COM\d+)\)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex UsbIdentifierPattern = new(
        @"\b(?:VID|PID)_[0-9A-F]{4}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex EmptyDelimiterGroupPattern = new(
        @"\(\s*[·|&:;/,\\-]*\s*\)|\[\s*[·|&:;/,\\-]*\s*\]|\{\s*[·|&:;/,\\-]*\s*\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex RepeatedWhitespacePattern = new(
        @"\s{2,}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly object _syncRoot = new();
    private SerialPort? _port;
    private bool _disposed;

    public event EventHandler<SerialBytesReceivedEventArgs>? BytesReceived;
    public event Action<string>? ErrorOccurred;

    public bool IsOpen
    {
        get
        {
            lock (_syncRoot)
            {
                return _port?.IsOpen == true;
            }
        }
    }

    public string? PortName
    {
        get
        {
            lock (_syncRoot)
            {
                return _port?.PortName;
            }
        }
    }

    public static IReadOnlyList<SerialPortInfo> GetAvailablePorts()
    {
        var friendlyNames = GetPresentPortFriendlyNames();
        return SerialPort.GetPortNames()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(portName => new SerialPortInfo(
                portName,
                friendlyNames.GetValueOrDefault(portName)))
            .OrderBy(port => GetPortNumber(port.PortName))
            .ThenBy(port => port.PortName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static Dictionary<string, string> GetPresentPortFriendlyNames()
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var classGuid = PortsDeviceSetupClass;
        var deviceInfoSet = SetupDiGetClassDevs(
            ref classGuid,
            null,
            IntPtr.Zero,
            DigcfPresent);
        if (deviceInfoSet == new IntPtr(-1))
        {
            return names;
        }

        try
        {
            for (uint index = 0; ; index++)
            {
                var deviceInfo = new SpDeviceInfoData
                {
                    Size = (uint)Marshal.SizeOf<SpDeviceInfoData>()
                };
                if (!SetupDiEnumDeviceInfo(deviceInfoSet, index, ref deviceInfo))
                {
                    break;
                }

                var fullName = GetDeviceRegistryProperty(
                                   deviceInfoSet,
                                   ref deviceInfo,
                                   SpdrpFriendlyName) ??
                               GetDeviceRegistryProperty(
                                   deviceInfoSet,
                                   ref deviceInfo,
                                   SpdrpDeviceDescription);
                if (string.IsNullOrWhiteSpace(fullName))
                {
                    continue;
                }

                var match = PortNameSuffixPattern.Match(fullName);
                if (!match.Success)
                {
                    continue;
                }

                var portName = match.Groups[1].Value.ToUpperInvariant();
                var friendlyName = fullName[..match.Index].Trim();
                if (friendlyName.Length > 0)
                {
                    names[portName] = SanitizeFriendlyName(friendlyName);
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }

        return names;
    }

    private static string SanitizeFriendlyName(string friendlyName)
    {
        var sanitized = UsbIdentifierPattern.Replace(friendlyName, string.Empty);
        sanitized = EmptyDelimiterGroupPattern.Replace(sanitized, string.Empty);
        sanitized = RepeatedWhitespacePattern.Replace(sanitized, " ");
        sanitized = sanitized.Trim(' ', '·', '|', '&', ':', ';', '/', '\\', ',', '-');
        return sanitized.Length > 0 ? sanitized : "串行接口";
    }

    private static string? GetDeviceRegistryProperty(
        IntPtr deviceInfoSet,
        ref SpDeviceInfoData deviceInfo,
        uint property)
    {
        var buffer = new byte[512];
        if (!SetupDiGetDeviceRegistryProperty(
                deviceInfoSet,
                ref deviceInfo,
                property,
                out _,
                buffer,
                (uint)buffer.Length,
                out var requiredSize))
        {
            if (requiredSize <= buffer.Length)
            {
                return null;
            }

            buffer = new byte[requiredSize];
            if (!SetupDiGetDeviceRegistryProperty(
                    deviceInfoSet,
                    ref deviceInfo,
                    property,
                    out _,
                    buffer,
                    (uint)buffer.Length,
                    out requiredSize))
            {
                return null;
            }
        }

        var textLength = Math.Min((int)requiredSize, buffer.Length);
        return Encoding.Unicode.GetString(buffer, 0, textLength).TrimEnd('\0').Trim();
    }

    public void Open(SerialPortSettings settings)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_syncRoot)
        {
            CloseCore();
            var port = new SerialPort
            {
                PortName = settings.PortName,
                BaudRate = settings.BaudRate,
                DataBits = settings.DataBits,
                Parity = settings.Parity,
                StopBits = settings.StopBits,
                Handshake = settings.Handshake,
                DtrEnable = settings.DtrEnable,
                RtsEnable = settings.RtsEnable,
                ReadTimeout = 500,
                WriteTimeout = 1000,
                ReadBufferSize = 16 * 1024,
                WriteBufferSize = 4 * 1024
            };

            port.DataReceived += OnDataReceived;
            try
            {
                port.Open();
                _port = port;
            }
            catch
            {
                port.DataReceived -= OnDataReceived;
                port.Dispose();
                throw;
            }
        }
    }

    public void Close()
    {
        lock (_syncRoot)
        {
            CloseCore();
        }
    }

    public void Send(byte[] data)
    {
        if (data.Length == 0)
        {
            return;
        }

        lock (_syncRoot)
        {
            if (_port?.IsOpen != true)
            {
                throw new InvalidOperationException("串口尚未连接。");
            }

            _port.Write(data, 0, data.Length);
        }
    }

    private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        try
        {
            var port = (SerialPort)sender;
            var count = port.BytesToRead;
            if (count <= 0)
            {
                return;
            }

            var buffer = new byte[count];
            var read = port.Read(buffer, 0, buffer.Length);
            if (read <= 0)
            {
                return;
            }

            if (read != buffer.Length)
            {
                Array.Resize(ref buffer, read);
            }

            BytesReceived?.Invoke(this, new SerialBytesReceivedEventArgs(buffer));
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            ErrorOccurred?.Invoke(exception.Message);
        }
    }

    private void CloseCore()
    {
        var port = _port;
        _port = null;
        if (port is null)
        {
            return;
        }

        port.DataReceived -= OnDataReceived;
        try
        {
            if (port.IsOpen)
            {
                port.Close();
            }
        }
        finally
        {
            port.Dispose();
        }
    }

    private static int GetPortNumber(string portName)
    {
        var digits = new string(portName.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var number) ? number : int.MaxValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDeviceInfoData
    {
        public uint Size;
        public Guid ClassGuid;
        public uint DeviceInstance;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(
        ref Guid classGuid,
        string? enumerator,
        IntPtr parentWindow,
        uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInfo(
        IntPtr deviceInfoSet,
        uint memberIndex,
        ref SpDeviceInfoData deviceInfoData);

    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceRegistryPropertyW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceRegistryProperty(
        IntPtr deviceInfoSet,
        ref SpDeviceInfoData deviceInfoData,
        uint property,
        out uint propertyRegistryDataType,
        byte[] propertyBuffer,
        uint propertyBufferSize,
        out uint requiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_syncRoot)
        {
            CloseCore();
            _disposed = true;
        }
    }
}
