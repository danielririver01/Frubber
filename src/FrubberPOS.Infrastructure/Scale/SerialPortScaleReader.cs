using System.IO.Ports;
using FrubberPOS.Application.Common;
using FrubberPOS.Application.Scale;

namespace FrubberPOS.Infrastructure.Scale;

/// <summary>
/// COM-port scale reader (JHO-86): works the same for native RS232 and for
/// USB converters (HL-340/CH340), which Windows exposes as COM ports.
/// Validated against the owner's physical BBG MARKET-30: a CONTINUOUS scale
/// (frames <c>01 02 "S 01.010kga" 03 04 00</c> at 9600 8N1); polling
/// protocols send their request byte ('P' Torrey, 'W' CAS, ENQ Systel).
/// For CONTINUOUS scales two consecutive frames are collected so movement
/// can be detected by comparison (see <see cref="ScaleFrameParser"/>); any
/// failure is a user-safe error and manual entry keeps working (RN-VEN-004).
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
        using var connection = new NativeSerialConnection(settings.PortName!, settings.BaudRate, settings.ReadTimeoutMs);
        connection.Open();
        connection.Purge();

        if (settings.Protocol != ScaleProtocols.Continuous)
        {
            connection.Write(settings.Protocol switch
            {
                ScaleProtocols.Torrey => "P",
                ScaleProtocols.Cas => "W",
                _ => "\u0005", // Systel: ENQ
            });
        }

        var buffer = new byte[1024];
        var total = 0;
        var deadline = DateTime.UtcNow.AddMilliseconds(settings.ReadTimeoutMs);
        while (DateTime.UtcNow < deadline && total < buffer.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chunk = new byte[Math.Min(256, buffer.Length - total)];
            var read = connection.ReadChunk(chunk);
            if (read <= 0)
            {
                if (total > 0)
                {
                    break; // protocolo por solicitud: la respuesta ya llegó
                }

                continue; // sin datos aún: el plazo manda
            }

            Buffer.BlockCopy(chunk, 0, buffer, total, read);
            total += read;

            if (settings.Protocol == ScaleProtocols.Continuous)
            {
                if (CountFrames(buffer, total) >= 2)
                {
                    break; // dos tramas bastan para juzgar estabilidad
                }
            }
            else
            {
                break;
            }
        }

        if (total == 0)
        {
            return Result<ScaleReading>.Failure(new Error(
                ErrorCode.Storage, "La báscula no respondió. Revise el cable y el protocolo elegido."));
        }

        if (!ScaleFrameParser.TryParseLatest(buffer, total, out var reading))
        {
            return Result<ScaleReading>.Failure(new Error(
                ErrorCode.Storage, "La báscula respondió con datos no reconocidos. Pruebe otro protocolo."));
        }

        return Result<ScaleReading>.Success(reading);
    }

    private static int CountFrames(byte[] buffer, int count)
    {
        var frames = 0;
        for (var i = 0; i < count; i++)
        {
            if (buffer[i] == 0x01)
            {
                frames++;
            }
        }

        return frames;
    }
}
