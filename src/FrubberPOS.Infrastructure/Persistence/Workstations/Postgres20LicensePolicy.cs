using FrubberPOS.Application.Common;
using FrubberPOS.Domain.Licensing;
using FrubberPOS.Domain.Trial;
using FrubberPOS.Infrastructure.Licensing;

namespace FrubberPOS.Infrastructure.Persistence.Workstations;

internal static class Postgres20LicensePolicy
{
    internal const int TrialSeats = 2;
    private static readonly TimeSpan ClockTolerance = TimeSpan.FromMinutes(5);

    internal static Error InvalidLicense { get; } =
        new(ErrorCode.LicenseBlocked, "La licencia del servidor no es válida para este negocio.");

    internal static Error? CheckClock(DateTimeOffset localTime, DateTime serverTime)
    {
        var difference = localTime - new DateTimeOffset(serverTime, TimeSpan.Zero);
        if (difference.Duration() <= ClockTolerance)
        {
            return null;
        }

        var direction = difference > TimeSpan.Zero ? "adelantado" : "atrasado";
        return new(ErrorCode.LicenseBlocked,
            $"El reloj de este equipo está {direction} respecto al servidor. Corrija la fecha y hora para continuar.");
    }

    internal static int? GetLicensedSeats(string payload, string serverMachineCode, DateTime serverTime,
        EcdsaLicenseSignatureVerifier verifier)
    {
        return WorkstationLicenseFormat.TryParse(payload, out var license) &&
               license!.Key.MachineCode == serverMachineCode &&
               license.Key.IssuedDate <= DateOnly.FromDateTime(serverTime) &&
               verifier.VerifyCanonical(license.Key.KeyId, license.CanonicalPayload, license.Key.Signature)
            ? license.Seats
            : null;
    }

    internal static Error? CheckTrial(DateTime serverTime, DateTime? firstRun, DateTime? lastSeen)
    {
        if (firstRun is not { } start || lastSeen is not { } previous)
        {
            return new(ErrorCode.LicenseBlocked, "El negocio no tiene una licencia ni un periodo de prueba habilitado.");
        }

        var outcome = TrialPolicy.Evaluate(ToUnixMilliseconds(start), ToUnixMilliseconds(previous),
            ToUnixMilliseconds(serverTime)).Outcome;
        return outcome switch
        {
            TrialOutcome.Active => null,
            TrialOutcome.ClockTampered => new(ErrorCode.LicenseBlocked,
                "Se detectó un retroceso en el reloj del servidor. Corrija la fecha y hora para continuar."),
            _ => new(ErrorCode.LicenseBlocked, "El periodo de prueba terminó. Active la licencia para continuar.")
        };
    }

    private static long ToUnixMilliseconds(DateTime utcTime) =>
        new DateTimeOffset(utcTime, TimeSpan.Zero).ToUnixTimeMilliseconds();
}
