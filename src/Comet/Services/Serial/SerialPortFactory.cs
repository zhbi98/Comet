using System.IO.Ports;
using Comet.Models;

namespace Comet.Services.Serial;

/// <summary>
/// Creates consistently configured serial-port instances for initial and recovery opens.
/// </summary>
internal static class SerialPortFactory
{
    public static SerialPort Create(
        SerialPortConnectionOptions options,
        SerialDataReceivedEventHandler dataReceivedHandler)
    {
        var port = new SerialPort
        {
            PortName = options.PortName,
            BaudRate = options.BaudRate,
            DataBits = options.DataBits,
            Parity = options.Parity,
            StopBits = options.StopBits,
            Handshake = options.Handshake,
            DtrEnable = options.IsDtrEnabled,
            RtsEnable = options.IsRtsEnabled,
            ReadTimeout = 500,
            WriteTimeout = 1000,
            ReadBufferSize = 16 * 1024,
            WriteBufferSize = 4 * 1024
        };
        port.DataReceived += dataReceivedHandler;
        return port;
    }
}
