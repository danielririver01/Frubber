using System.Management;
using System.Runtime.Versioning;
using FrubberPOS.Application.Licensing;
using FrubberPOS.Application.System;
using FrubberPOS.Domain.Licensing;

namespace FrubberPOS.Infrastructure.Licensing;

/// <summary>
/// Team identity from SMBIOS UUID, board and disk serials (JHO-25, ADR-013).
/// Raw serials are hashed into the machine code inside this class and never
/// leave it; callers receive the code plus redacted tokens only. When WMI
/// yields nothing usable, falls back to the machine name so activation can
/// still bind to something stable (weaker, documented).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsMachineCodeProvider : IMachineCodeProvider
{
    public async Task<MachineFingerprint> GetFingerprintAsync(CancellationToken cancellationToken)
    {
        var raw = await Task.Run(ReadRawIdentifiers, cancellationToken);
        var code = MachineCode.Compute(raw);
        var redacted = raw.Select(MachineIdentityRedactor.Redact).ToList();
        return new MachineFingerprint(code, redacted);
    }

    private static IReadOnlyList<string> ReadRawIdentifiers()
    {
        var found = new List<string>();
        foreach (var value in ReadWmi())
        {
            var cleaned = value?.Trim();
            if (!string.IsNullOrWhiteSpace(cleaned)
                && !IsPlaceholder(cleaned)
                && !found.Contains(cleaned, StringComparer.OrdinalIgnoreCase))
            {
                found.Add(cleaned);
            }
        }

        if (found.Count == 0)
        {
            found.Add("FALLBACK-" + Environment.MachineName.Trim());
        }

        return found;
    }

    private static IEnumerable<string?> ReadWmi()
    {
        string? uuid = null;
        string? board = null;
        string? disk = null;
        try
        {
            uuid = QuerySingle("SELECT UUID FROM Win32_ComputerSystemProduct", "UUID");
            board = QuerySingle("SELECT SerialNumber FROM Win32_BaseBoard", "SerialNumber");
            disk = QueryFirst("SELECT SerialNumber FROM Win32_DiskDrive", "SerialNumber");
        }
        catch (Exception)
        {
            // WMI unavailable (service disabled, permissions): the fallback
            // below still binds the license to this team.
        }

        yield return uuid;
        yield return board;
        yield return disk;
    }

    private static bool IsPlaceholder(string value) =>
        value.Equals("System Serial Number", StringComparison.OrdinalIgnoreCase)
        || value.Equals("Default string", StringComparison.OrdinalIgnoreCase)
        || value.Equals("None", StringComparison.OrdinalIgnoreCase)
        || value.All(c => c == '0');

    private static string? QuerySingle(string query, string property)
    {
        using var searcher = new ManagementObjectSearcher(query);
        using var results = searcher.Get();
        foreach (var item in results)
        {
            using (item)
            {
                return item[property]?.ToString();
            }
        }

        return null;
    }

    private static string? QueryFirst(string query, string property)
    {
        using var searcher = new ManagementObjectSearcher(query);
        using var results = searcher.Get();
        foreach (var item in results)
        {
            using (item)
            {
                var value = item[property]?.ToString()?.Trim();
                if (!string.IsNullOrWhiteSpace(value) && !IsPlaceholder(value))
                {
                    return value;
                }
            }
        }

        return null;
    }
}
