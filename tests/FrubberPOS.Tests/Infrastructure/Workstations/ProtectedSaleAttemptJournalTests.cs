using System.Security.Cryptography;
using FrubberPOS.Application.Workstations;
using FrubberPOS.Application.Common;
using FrubberPOS.Desktop.Authentication;
using FrubberPOS.Domain.Products;
using FrubberPOS.Domain.Users;
using FrubberPOS.Infrastructure.Persistence.Workstations;
using FrubberPOS.Tests.TestDoubles;

namespace FrubberPOS.Tests.Infrastructure.Workstations;

public sealed class ProtectedSaleAttemptJournalTests : IDisposable
{
    private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("frubber-cobro-");
    private string JournalPath => Path.Combine(directory.FullName, "attempt");

    [Fact]
    public void ReopeningJournalRecoversExactAttemptAndConfirmedReceipt()
    {
        var attempt = NewAttempt();
        var journal = new ProtectedSaleAttemptJournal(JournalPath);
        Assert.Null(journal.Read());
        journal.Start(attempt);

        var reopened = new ProtectedSaleAttemptJournal(JournalPath);
        var recovered = Assert.IsType<SaleAttemptEntry>(reopened.Read());
        Assert.Equal(attempt.RequestId, recovered.Attempt.RequestId);
        Assert.Equal(attempt.DatabaseId, recovered.Attempt.DatabaseId);
        Assert.Equal(attempt.TerminalId, recovered.Attempt.TerminalId);
        Assert.Equal(attempt.CashId, recovered.Attempt.CashId);
        Assert.Equal(attempt.CashierId, recovered.Attempt.CashierId);
        Assert.Equal(attempt.Sale.Lines.ToArray(), recovered.Attempt.Sale.Lines.ToArray());
        Assert.Equal(attempt.Sale.ReceivedCop, recovered.Attempt.Sale.ReceivedCop);
        Assert.Equal(attempt.Sale.DiscountCop, recovered.Attempt.Sale.DiscountCop);
        Assert.Equal(attempt.Sale.PaymentMethod, recovered.Attempt.Sale.PaymentMethod);
        Assert.Null(recovered.Receipt);

        var receipt = new WorkstationSale(2, 3, 1450);
        reopened.Confirm(attempt.RequestId, receipt);
        Assert.Equal(receipt, new ProtectedSaleAttemptJournal(JournalPath).Read()?.Receipt);
        reopened.Acknowledge(attempt.RequestId);
        Assert.Null(reopened.Read());
    }

    [Fact]
    public void PendingAttemptCannotBeReplacedOrAcknowledgedWithoutReceipt()
    {
        var attempt = NewAttempt();
        var journal = new ProtectedSaleAttemptJournal(JournalPath);
        journal.Start(attempt);
        Assert.Throws<IOException>(() => journal.Start(NewAttempt()));
        Assert.Throws<InvalidOperationException>(() => journal.Acknowledge(attempt.RequestId));
        Assert.Throws<InvalidOperationException>(() => journal.Confirm(Guid.NewGuid(), new WorkstationSale(2, 3, 1450)));
        Assert.Equal(attempt.RequestId, journal.Read()?.Attempt.RequestId);
    }

    [Fact]
    public void DamagedJournalCannotBeReadAsAnEmptySlot()
    {
        File.WriteAllText(JournalPath, "corrupt");
        var journal = new ProtectedSaleAttemptJournal(JournalPath);
        Assert.ThrowsAny<CryptographicException>(() => journal.Read());
        Assert.Throws<IOException>(() => journal.Start(NewAttempt()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecoveryAfterClientRecreationReusesOriginalAttempt(bool committedBeforeLoss)
    {
        var attempt = NewAttempt();
        var saved = new Dictionary<Guid, WorkstationSale>();
        var receipt = new WorkstationSale(2, 3, 1450);
        var originalSession = new CurrentSession();
        originalSession.SignIn(new(attempt.CashierId, "Cajero de prueba", UserRole.Cashier));
        var gate = new FakeOperationalGate();
        Guid firstRequest = Guid.Empty;
        var original = new WorkstationSaleRecovery(new(JournalPath), attempt.DatabaseId, attempt.TerminalId, originalSession, gate,
            (pending, _) =>
            {
                firstRequest = pending.RequestId;
                if (committedBeforeLoss)
                {
                    saved.Add(pending.RequestId, receipt);
                }
                return Task.FromResult(Result<WorkstationSale>.Failure(new Error(ErrorCode.Connection, "Respuesta perdida")));
            });
        Assert.True((await original.RegisterAsync(attempt.CashId, attempt.Sale, CancellationToken.None)).IsFailure);
        Assert.True(original.HasPendingAttempt);

        var newSession = new CurrentSession();
        var sends = 0;
        var restarted = new WorkstationSaleRecovery(new(JournalPath), attempt.DatabaseId, attempt.TerminalId, newSession, gate,
            (pending, _) =>
            {
                sends++;
                Assert.Equal(firstRequest, pending.RequestId);
                Assert.Equal(attempt.CashId, pending.CashId);
                Assert.Equal(attempt.Sale.Lines.ToArray(), pending.Sale.Lines.ToArray());
                saved.TryAdd(pending.RequestId, receipt);
                return Task.FromResult(Result<WorkstationSale>.Success(saved[pending.RequestId]));
            });
        newSession.SignIn(new(attempt.CashierId + 1, "Otro cajero", UserRole.Cashier));
        Assert.Equal(ErrorCode.Authorization, (await restarted.RecoverAsync(CancellationToken.None)).Error?.Code);
        newSession.SignIn(new(attempt.CashierId, "Cajero de prueba", UserRole.Cashier));
        Assert.Equal(ErrorCode.Conflict, (await restarted.RegisterAsync(attempt.CashId + 1, attempt.Sale, CancellationToken.None)).Error?.Code);
        Assert.Equal(receipt, (await restarted.RecoverAsync(CancellationToken.None)).Value);
        Assert.Equal(receipt, (await restarted.RecoverAsync(CancellationToken.None)).Value);
        Assert.Single(saved);
        Assert.Equal(1, sends);
        restarted.Acknowledge();
        Assert.False(restarted.HasPendingAttempt);
    }

    private static SaleAttempt NewAttempt() =>
        new(Guid.NewGuid(), Guid.NewGuid(), 2, 7, 4,
            new([new(12, "P1", "Pera", ProductUnit.Kg, 1500, 1000)], 50, "CASH", 2000));

    public void Dispose()
    {
        foreach (var file in directory.EnumerateFiles())
        {
            file.Delete();
        }
        directory.Delete();
    }
}
