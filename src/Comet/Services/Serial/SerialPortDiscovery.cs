using System.IO.Ports;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Comet.Models;

namespace Comet.Services.Serial;

/// <summary>
/// Enumerates present COM ports and enriches them with sanitized SetupAPI names.
/// </summary>
internal static class SerialPortDiscovery
{
    private const uint DIGCF_PRESENT = 0x00000002;
    private const uint SPDRP_DEVICE_DESCRIPTION = 0x00000000;
    private const uint SPDRP_FRIENDLY_NAME = 0x0000000C;
    private static readonly Guid _portsDeviceSetupClass = new("4D36E978-E325-11CE-BFC1-08002BE10318");
    private static readonly Regex _portNameSuffixPattern = new(
        @"\((COM\d+)\)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex _usbIdentifierPattern = new(
        @"\b(?:VID|PID)_[0-9A-F]{4}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex _emptyDelimiterGroupPattern = new(
        @"\(\s*[·|&:;/,\\-]*\s*\)|\[\s*[·|&:;/,\\-]*\s*\]|\{\s*[·|&:;/,\\-]*\s*\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex _repeatedWhitespacePattern = new(
        @"\s{2,}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IReadOnlyList<SerialPortInfoModel> GetAvailablePorts()
    {
        var friendlyNames = GetPresentPortFriendlyNames();
        return SerialPort.GetPortNames()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(portName => new SerialPortInfoModel(
                portName,
                friendlyNames.GetValueOrDefault(portName)))
            .OrderBy(port => GetPortNumber(port.PortName))
            .ThenBy(port => port.PortName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static bool IsPortPresent(string portName)
    {
        try
        {
            return SerialPort.GetPortNames().Contains(portName, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static Dictionary<string, string> GetPresentPortFriendlyNames()
    {
        // SerialPort exposes only COM names. SetupAPI supplies the matching device
        // description (for example, CH340) without opening the port.
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var classGuid = _portsDeviceSetupClass;
        var deviceInfoSet = SetupDiGetClassDevs(
            ref classGuid,
            null,
            IntPtr.Zero,
            DIGCF_PRESENT);
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
                                   SPDRP_FRIENDLY_NAME) ??
                               GetDeviceRegistryProperty(
                                   deviceInfoSet,
                                   ref deviceInfo,
                                   SPDRP_DEVICE_DESCRIPTION);
                if (string.IsNullOrWhiteSpace(fullName))
                {
                    continue;
                }

                var match = _portNameSuffixPattern.Match(fullName);
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
        var sanitized = _usbIdentifierPattern.Replace(friendlyName, string.Empty);
        sanitized = _emptyDelimiterGroupPattern.Replace(sanitized, string.Empty);
        sanitized = _repeatedWhitespacePattern.Replace(sanitized, " ");
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
}
