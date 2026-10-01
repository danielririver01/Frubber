using FrubberPOS.Application.Authentication;
using FrubberPOS.Application.Common;
using FrubberPOS.Application.Logging;

namespace FrubberPOS.Application.Backup;

/// <summary>Manual backup request: absolute destination chosen by the operator (JHO-53).</summary>
public sealed record CreateBackupRequest(string DestinationFilePath);

/// <summary>Backup created: file name and full location for the success message (§16.2).</summary>
public sealed record CreateBackupResponse(string FileName, string DestinationPath);

/// <summary>
/// SQLite online-backup port (JHO-53, ADR-013): copies the live database
/// with the SQLite Backup API into a temporary file next to the target
/// and publishes it with an atomic rename. The source database is never
/// written to; a failed run leaves no partial destination.
/// </summary>
public interface IBackupService
{
    Task BackupAsync(string destinationFilePath, CancellationToken cancellationToken);
}

/// <summary>
/// Manual backup (JHO-53, P-UX §16): administrator-only; the destination
/// comes from the standard Windows dialog, the media write happens with
/// the SQLite Backup API and the final publish is an atomic rename.
/// Success names the file and location; failure states that the original
/// database was not altered plus a diagnostic reference (log id).
/// No restore exists in the product.
/// </summary>
public sealed class CreateBackupUseCase : IUseCase<CreateBackupRequest, CreateBackupResponse>
{
    private readonly IBackupService backup;
    private readonly ICurrentSession session;
    private readonly IAppLogger logger;

    public CreateBackupUseCase(IBackupService backup, ICurrentSession session, IAppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(backup);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(logger);
        this.backup = backup;
        this.session = session;
        this.logger = logger;
    }

    public async Task<Result<CreateBackupResponse>> ExecuteAsync(CreateBackupRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var denied = Authorization.RequireAdmin(session);
        if (denied is not null)
        {
            return Result<CreateBackupResponse>.Failure(denied);
        }

        var destination = request.DestinationFilePath?.Trim();
        if (string.IsNullOrEmpty(destination))
        {
            return Result<CreateBackupResponse>.Failure(new Error(ErrorCode.Validation, "Elija la ubicación del respaldo."));
        }

        try
        {
            await backup.BackupAsync(destination, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var reference = logger.LogError("BACKUP_FAILED", "Fallo al crear el respaldo manual.", ex);
            return Result<CreateBackupResponse>.Failure(new Error(
                ErrorCode.Backup,
                $"No se pudo crear el respaldo. La base de datos original no fue alterada. Código {reference}."));
        }

        logger.LogInformation("BACKUP_CREATED", $"Respaldo creado en {destination}.");
        return Result<CreateBackupResponse>.Success(
            new CreateBackupResponse(Path.GetFileName(destination), destination));
    }
}
