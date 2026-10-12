using System.IO.Ports;
using FrubberPOS.Application.Common;
using FrubberPOS.Application.Scale;

namespace FrubberPOS.Infrastructure.Scale;

/// <summary>
/// COM-port scale reader (JHO-86): works the same for native RS232 and for
/// USB converters (HL-340/CH340), which Windows exposes as COM ports.
/// Polling protocols send their request byte ('P' Torrey, 'W' CAS, ENQ
/// Systel); CONTINUOUS scales just stream. One weighing is read per call
/// with the configured timeout; the weight never blocks the sale: any
/// failure is returned as a user-safe error and manual entry remains.
/// </summary>
public sealed class SerialPortScaleReader : IScaleReader
{
    public Task<IReadOnlyList<string>> ListPortsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<string> ports = SerialPort.GetPortNames()
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return Task.FromResult(ports);
    }

    public async Task<Result<ScaleReading>> ReadAsync(ScaleSnapshot settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.PortName is null)
        {
            return Result<ScaleReading>.Failure(new Error(ErrorCode.Validation, "La báscula no tiene puerto configurado."));
        }

        try
        {
            return await Task.Run(() => ReadOnce(settings, cancellationToken), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException or InvalidOperationException)
        {
            return Result<ScaleReading>.Failure(new Error(
                ErrorCode.Storage,
                $"No se pudo leer la báscula en {settings.PortName}. Revise el cable y que ningún otro programa la esté usando."));
        }
    }

    private static Result<ScaleReading> ReadOnce(ScaleSnapshot settings, CancellationToken cancellationToken)
    {
        using var port = new SerialPort(settings.PortName!, settings.BaudRate)
        {
            Parity = Parity.None,
            DataBits = 8,
            StopBits = StopBits.One,
            ReadTimeout = settings.ReadTimeoutMs,
            WriteTimeout = settings.ReadTimeoutMs,
        };
        port.Open();
        port.DiscardInBuffer();

        if (settings.Protocol != ScaleProtocols.Continuous)
        {
            var command = settings.Protocol switch
            {
                ScaleProtocols.Torrey => "P",
                ScaleProtocols.Cas => "W",
                _ => "\u0005", // Systel: ENQ
            };
            port.Write(command);
        }

        var buffer = new byte[512];
        var total = 0;
        var deadline = DateTime.UtcNow.AddMilliseconds(settings.ReadTimeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var read = port.Read(buffer, total, buffer.Length - total);
                total += read;
                if (read == 0)
                {
                    break;
                }
            }
            catch (TimeoutException)
            {
                break;
            }
        }

        if (total == 0)
        {
            return Result<ScaleReading>.Failure(new Error(
                ErrorCode.Storage, "La báscula no respondió. Revise el cable y el protocolo elegido."));
        }

        if (!ScaleFrameParser.TryParse(buffer, total, out var reading))
        {
            return Result<ScaleReading>.Failure(new Error(
                ErrorCode.Storage, "La báscula respondió con datos no reconocidos. Pruebe otro protocolo."));
        }

        return Result<ScaleReading>.Success(reading);
    }
}
