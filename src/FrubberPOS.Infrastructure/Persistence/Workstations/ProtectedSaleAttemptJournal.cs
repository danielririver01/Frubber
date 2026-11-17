using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FrubberPOS.Application.Sales;
using FrubberPOS.Application.Workstations;

namespace FrubberPOS.Infrastructure.Persistence.Workstations;

internal sealed record SaleAttempt(
    Guid RequestId, Guid DatabaseId, long TerminalId, long CashId, int CashierId, FinalizeSaleRequest Sale);

internal sealed record SaleAttemptEntry(int Version, SaleAttempt Attempt, WorkstationSale? Receipt);

/// <summary>One unresolved attempt per provisioned Windows profile.</summary>
[SupportedOSPlatform("windows")]
internal sealed class ProtectedSaleAttemptJournal(string path)
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("FrubberPOS.SaleAttempt.v1");
    private readonly string path = Path.GetFullPath(path);

    public void Start(SaleAttempt attempt) =>
        Write(new SaleAttemptEntry(1, attempt, null), replace: false);

    public SaleAttemptEntry? Read()
    {
        byte[] encrypted;
        try
        {
            encrypted = File.ReadAllBytes(path);
        }
        catch (FileNotFoundException)
        {
            return null;
        }

        var bytes = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
        try
        {
            var entry = JsonSerializer.Deserialize<SaleAttemptEntry>(bytes);
            if (entry is not { Version: 1, Attempt: { } attempt } ||
                attempt.RequestId == Guid.Empty || attempt.DatabaseId == Guid.Empty ||
                attempt.TerminalId <= 0 || attempt.CashId <= 0 || attempt.CashierId <= 0 ||
                attempt.Sale?.Lines is not { Count: > 0 })
            {
                throw new InvalidDataException("El intento guardado no es válido.");
            }

            return entry;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public void Confirm(Guid requestId, WorkstationSale receipt)
    {
        using var guard = Lock();
        var entry = Read();
        if (entry is null || entry.Attempt.RequestId != requestId ||
            (entry.Receipt is not null && entry.Receipt != receipt))
        {
            throw new InvalidOperationException("El comprobante no corresponde al intento guardado.");
        }

        Write(entry with { Receipt = receipt }, replace: true);
    }

    public void Acknowledge(Guid requestId)
    {
        using var guard = Lock();
        var entry = Read();
        if (entry?.Receipt is null || entry.Attempt.RequestId != requestId)
        {
            throw new InvalidOperationException("El intento requiere un comprobante confirmado.");
        }

        File.Delete(path);
    }

    private FileStream Lock() => new(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

    private void Write(SaleAttemptEntry entry, bool replace)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(entry);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var encrypted = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(encrypted);
                stream.Flush(flushToDisk: true);
            }

            if (replace)
            {
                File.Replace(temp, path, null);
            }
            else
            {
                File.Move(temp, path);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            File.Delete(temp);
        }
    }
}
