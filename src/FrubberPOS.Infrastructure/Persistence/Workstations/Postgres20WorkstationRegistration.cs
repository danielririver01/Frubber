using Dapper;
using System.Text.RegularExpressions;
using FrubberPOS.Application.Common;
using FrubberPOS.Application.System;
using FrubberPOS.Infrastructure.Licensing;
using Npgsql;

namespace FrubberPOS.Infrastructure.Persistence.Workstations;

/// <summary>Server administration only. The supplied connection belongs to the database administrator.</summary>
public sealed class Postgres20WorkstationRegistration(
    EcdsaLicenseSignatureVerifier verifier, string serverMachineCode, IClock clock)
{
    public async Task<Result<RegisteredWorkstation>> RegisterAsync(
        NpgsqlConnection connection, string name, string machineCode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 60 ||
            machineCode is null || !Regex.IsMatch(machineCode, @"\A[0-9A-F]{16}\z") ||
            machineCode == "0000000000000000")
        {
            return Result<RegisteredWorkstation>.Failure(new(ErrorCode.Validation,
                "Indique un nombre de 1 a 60 caracteres y un código de equipo de 16 caracteres hexadecimales."));
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        // Serialize registrations and license replacement until commit, including registrations
        // through register_terminal. The administrator remains the trusted provisioning boundary.
        await connection.ExecuteAsync(new CommandDefinition(
            "LOCK TABLE public.license_state, public.terminal IN EXCLUSIVE MODE;",
            transaction: transaction, cancellationToken: cancellationToken));
        var payload = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT signed_payload FROM public.license_state WHERE license_state_id = 1;",
            transaction: transaction, cancellationToken: cancellationToken));
        int seats;
        if (payload is null)
        {
            // Both the first registration and the trial start commit together.
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO public.trial_state (trial_state_id, first_run_at, last_seen_at)
                VALUES (1, statement_timestamp(), statement_timestamp()) ON CONFLICT DO NOTHING;
                """, transaction: transaction, cancellationToken: cancellationToken));
            var (serverTime, start, lastSeen) = await connection.QuerySingleAsync<(DateTime, DateTime?, DateTime?)>(
                new CommandDefinition("SELECT * FROM public.advance_business_trial();",
                    transaction: transaction, cancellationToken: cancellationToken));
            var error = Postgres20LicensePolicy.CheckClock(clock.UtcNow, serverTime)
                ?? Postgres20LicensePolicy.CheckTrial(serverTime, start, lastSeen);
            if (error is not null)
            {
                return Result<RegisteredWorkstation>.Failure(error);
            }
            seats = Postgres20LicensePolicy.TrialSeats;
        }
        else
        {
            var serverTime = await connection.ExecuteScalarAsync<DateTime>(new CommandDefinition(
                "SELECT clock_timestamp();", transaction: transaction, cancellationToken: cancellationToken));
            var clockError = Postgres20LicensePolicy.CheckClock(clock.UtcNow, serverTime);
            if (clockError is not null)
            {
                return Result<RegisteredWorkstation>.Failure(clockError);
            }
            if (Postgres20LicensePolicy.GetLicensedSeats(payload, serverMachineCode, serverTime, verifier) is not { } limit)
            {
                return Result<RegisteredWorkstation>.Failure(Postgres20LicensePolicy.InvalidLicense);
            }
            seats = limit;
        }

        var active = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*)::integer FROM public.terminal WHERE is_active;",
            transaction: transaction, cancellationToken: cancellationToken));
        if (active >= seats)
        {
            var entitlement = payload is null ? "La prueba" : "La licencia";
            return Result<RegisteredWorkstation>.Failure(new(ErrorCode.LicenseBlocked,
                $"El negocio ya tiene {active} de {seats} puestos autorizados. {entitlement} no permite autorizar otro puesto."));
        }

        var duplicate = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM public.terminal WHERE machine_code = @Code);",
            new { Code = machineCode }, transaction, cancellationToken: cancellationToken));
        if (duplicate)
        {
            return Result<RegisteredWorkstation>.Failure(new(ErrorCode.Conflict,
                "Ya existe un puesto registrado con ese código de equipo."));
        }

        var credentials = await connection.QuerySingleAsync<RegisteredWorkstation>(new CommandDefinition("""
            SELECT db_role AS "Role", generated_password AS "Password",
                   @Active AS "Active", @Seats AS "Seats"
              FROM public.register_terminal(@Name, CAST(@Code AS char(16)));
            """, new { Name = name.Trim(), Code = machineCode, Active = active + 1, Seats = seats },
            transaction, cancellationToken: cancellationToken));
        // A lost acknowledgement must be reconciled by the administrator before retrying.
        await transaction.CommitAsync(cancellationToken);
        return Result<RegisteredWorkstation>.Success(credentials);
    }
}

public sealed record RegisteredWorkstation(string Role, string Password, int Active, int Seats);
