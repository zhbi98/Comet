using System.IO.Ports;

namespace Comet.Models;

public sealed record SerialPortConnectionOptions(
    string PortName,
    int BaudRate,
    int DataBits,
    Parity Parity,
    StopBits StopBits,
    Handshake Handshake,
    bool IsDtrEnabled,
    bool IsRtsEnabled);
