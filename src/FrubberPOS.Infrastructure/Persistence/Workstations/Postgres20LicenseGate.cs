using FrubberPOS.Application.Common;
using FrubberPOS.Application.System;
using FrubberPOS.Application.Trial;
using FrubberPOS.Infrastructure.Licensing;

namespace FrubberPOS.Infrastructure.Persistence.Workstations;

public sealed class Postgres20LicenseGate(
    Postgres20WorkstationStore store,
    EcdsaLicenseSignatureVerifier verifier,
    string serverMachineCode,
    IClock clock) : IOperationalGate
{
    public async Task<Error?> CheckAsync(CancellationToken cancellationToken)
    {
        var state = await store.ReadLicenseAsync(cancellationToken);
        if (state.IsFailure)
        {
            return state.Error;
        }

        var row = state.Value;
        var clockError = Postgres20LicensePolicy.CheckClock(clock.UtcNow, row.ServerTime);
        if (clockError is not null)
        {
            return clockError;
        }

        if (row.SignedPayload is null)
        {
            return Postgres20LicensePolicy.CheckTrial(row.ServerTime, row.TrialStart, row.TrialLastSeen)
                ?? CheckSlot(row.Slot, Postgres20LicensePolicy.TrialSeats, "La prueba del negocio no autoriza este puesto.");
        }

        var seats = Postgres20LicensePolicy.GetLicensedSeats(row.SignedPayload, serverMachineCode,
            row.ServerTime, verifier);
        return seats is { } limit
            ? CheckSlot(row.Slot, limit, "La licencia del negocio no autoriza este puesto.")
            : Postgres20LicensePolicy.InvalidLicense;
    }

    private static Error? CheckSlot(long slot, int seats, string message) =>
        slot > 0 && slot <= seats
            ? null
            : new Error(ErrorCode.LicenseBlocked, message);
}
