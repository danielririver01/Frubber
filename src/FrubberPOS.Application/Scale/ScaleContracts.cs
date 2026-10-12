using FrubberPOS.Application.Common;
using FrubberPOS.Application.Persistence;

namespace FrubberPOS.Application.Scale;

/// <summary>Approved scale protocols: free-running stream or request/response (JHO-86).</summary>
public static class ScaleProtocols
{
    public const string Continuous = "CONTINUOUS";
    public const string Torrey = "TORREY_P";
    public const string Cas = "CAS_W";
    public const string Systel = "SYSTEL_ENQ";

    public static readonly IReadOnlyList<string> All = [Continuous, Torrey, Cas, Systel];
}

/// <summary>Scale configuration row (single-row table, migration 004).</summary>
public sealed record ScaleSnapshot(
    bool IsEnabled,
    string? PortName,
    int BaudRate,
    string Protocol,
    string ScaleUnit,
    int ReadTimeoutMs)
{
    public static ScaleSnapshot Disabled { get; } = new(false, null, 9600, ScaleProtocols.Continuous, "KG", 1200);
}

/// <summary>Scale settings port (JHO-86). Single-row table.</summary>
public interface IScaleSettingRepository
{
    Task<ScaleSnapshot?> GetAsync(IUnitOfWork unit, CancellationToken cancellationToken);

    Task UpdateAsync(IUnitOfWork unit, ScaleSnapshot settings, CancellationToken cancellationToken);
}

/// <summary>One decoded weighing: value in thousandths of the scale unit (kg/lb × 1000).</summary>
public sealed record ScaleReading(long WeightMilli, bool Stable);

/// <summary>
/// Serial scale reader port (JHO-86): the Infrastructure implementation
/// wraps a Windows COM port (native RS232 or HL-340/CH340 USB converter).
/// </summary>
public interface IScaleReader
{
    /// <summary>Reads ONE weighing using the given settings; fails with a user-safe error.</summary>
    Task<Result<ScaleReading>> ReadAsync(ScaleSnapshot settings, CancellationToken cancellationToken);

    /// <summary>COM ports currently visible to Windows (for the configuration dropdown).</summary>
    Task<IReadOnlyList<string>> ListPortsAsync(CancellationToken cancellationToken);
}

public sealed record GetScaleSettingsRequest;

public sealed record UpdateScaleSettingsRequest(
    bool IsEnabled,
    string? PortName,
    int BaudRate,
    string Protocol,
    string ScaleUnit,
    int ReadTimeoutMs);

public sealed record UpdateScaleSettingsResponse;

/// <summary>
/// Scale configuration (JHO-86): written by the administrator from the
/// settings screen. The port may stay null while disabled; baud rate and
/// protocol are restricted to the approved lists.
/// </summary>
public sealed class GetScaleSettingsUseCase : IUseCase<GetScaleSettingsRequest, ScaleSnapshot>
{
    private readonly IScaleSettingRepository settings;
    private readonly IUnitOfWorkFactory units;
    private readonly Authentication.ICurrentSession session;

    public GetScaleSettingsUseCase(IScaleSettingRepository settings, IUnitOfWorkFactory units, Authentication.ICurrentSession session)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(session);
        this.settings = settings;
        this.units = units;
        this.session = session;
    }

    public async Task<Result<ScaleSnapshot>> ExecuteAsync(GetScaleSettingsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var denied = Authentication.Authorization.RequireAdmin(session);
        if (denied is not null)
        {
            return Result<ScaleSnapshot>.Failure(denied);
        }

        await using var unit = await units.CreateAsync(UnitOfWorkMode.ReadOnly, cancellationToken);
        var row = await settings.GetAsync(unit, cancellationToken);
        return Result<ScaleSnapshot>.Success(row ?? ScaleSnapshot.Disabled);
    }
}

/// <summary>
/// Saves the scale configuration (JHO-86): administrator-only, trimmed
/// port name, approved baud rates and protocol codes only.
/// </summary>
public sealed class UpdateScaleSettingsUseCase : IUseCase<UpdateScaleSettingsRequest, UpdateScaleSettingsResponse>
{
    private static readonly int[] ApprovedBauds = [1200, 2400, 4800, 9600, 19200, 38400, 57600, 115200];

    private readonly IScaleSettingRepository settings;
    private readonly IUnitOfWorkFactory units;
    private readonly Authentication.ICurrentSession session;

    public UpdateScaleSettingsUseCase(IScaleSettingRepository settings, IUnitOfWorkFactory units, Authentication.ICurrentSession session)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(session);
        this.settings = settings;
        this.units = units;
        this.session = session;
    }

    public async Task<Result<UpdateScaleSettingsResponse>> ExecuteAsync(UpdateScaleSettingsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var denied = Authentication.Authorization.RequireAdmin(session);
        if (denied is not null)
        {
            return Result<UpdateScaleSettingsResponse>.Failure(denied);
        }

        var port = string.IsNullOrWhiteSpace(request.PortName) ? null : request.PortName.Trim();
        if (port is not null && port.Length > 40)
        {
            return Result<UpdateScaleSettingsResponse>.Failure(new Error(
                Common.ErrorCode.Validation, "El nombre del puerto no puede superar 40 caracteres.", "PortName"));
        }

        if (!ApprovedBauds.Contains(request.BaudRate))
        {
            return Result<UpdateScaleSettingsResponse>.Failure(new Error(
                Common.ErrorCode.Validation, "La velocidad (baudios) no es válida.", "BaudRate"));
        }

        if (!ScaleProtocols.All.Contains(request.Protocol))
        {
            return Result<UpdateScaleSettingsResponse>.Failure(new Error(
                Common.ErrorCode.Validation, "El protocolo de la báscula no es válido.", "Protocol"));
        }

        if (request.ScaleUnit is not ("KG" or "LB"))
        {
            return Result<UpdateScaleSettingsResponse>.Failure(new Error(
                Common.ErrorCode.Validation, "La unidad de la báscula debe ser KG o LB.", "ScaleUnit"));
        }

        if (request.ReadTimeoutMs is < 200 or > 10_000)
        {
            return Result<UpdateScaleSettingsResponse>.Failure(new Error(
                Common.ErrorCode.Validation, "El tiempo de espera debe estar entre 0,2 y 10 segundos.", "ReadTimeoutMs"));
        }

        await using var unit = await units.CreateAsync(UnitOfWorkMode.WriteImmediate, cancellationToken);
        await settings.UpdateAsync(
            unit,
            new ScaleSnapshot(request.IsEnabled, port, request.BaudRate, request.Protocol, request.ScaleUnit, request.ReadTimeoutMs),
            cancellationToken);
        await unit.CommitAsync(cancellationToken);
        return Result<UpdateScaleSettingsResponse>.Success(new UpdateScaleSettingsResponse());
    }
}

public sealed record ReadScaleWeightRequest(string ExpectedUnit, ScaleSnapshot? Draft = null);

public sealed record ReadScaleWeightResponse(long QuantityMilli, bool Stable);

/// <summary>
/// Reads the scale for the TPV (JHO-86; cambio de alcance 2026-10-12):
/// any signed-in user may weigh. The reading must match the product unit
/// (RN-UNI-003/005; there is no kg↔lb conversion) and be stable when the
/// protocol reports stability; otherwise the sale continues manually
/// (RN-VEN-004 queda como respaldo).
/// </summary>
public sealed class ReadScaleWeightUseCase : IUseCase<ReadScaleWeightRequest, ReadScaleWeightResponse>
{
    private readonly IScaleSettingRepository settings;
    private readonly IScaleReader reader;
    private readonly IUnitOfWorkFactory units;
    private readonly Authentication.ICurrentSession session;

    public ReadScaleWeightUseCase(
        IScaleSettingRepository settings,
        IScaleReader reader,
        IUnitOfWorkFactory units,
        Authentication.ICurrentSession session)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(session);
        this.settings = settings;
        this.reader = reader;
        this.units = units;
        this.session = session;
    }

    public async Task<Result<ReadScaleWeightResponse>> ExecuteAsync(ReadScaleWeightRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var denied = Authentication.Authorization.RequireSignedIn(session);
        if (denied is not null)
        {
            return Result<ReadScaleWeightResponse>.Failure(denied);
        }

        var snapshot = request.Draft;
        if (snapshot is null)
        {
            await using var unit = await units.CreateAsync(UnitOfWorkMode.ReadOnly, cancellationToken);
            snapshot = await settings.GetAsync(unit, cancellationToken) ?? ScaleSnapshot.Disabled;
        }

        if (!snapshot.IsEnabled)
        {
            return Result<ReadScaleWeightResponse>.Failure(new Error(
                Common.ErrorCode.Conflict, "La báscula está desactivada. Actívela en Configuración o digite el peso."));
        }

        if (snapshot.PortName is null)
        {
            return Result<ReadScaleWeightResponse>.Failure(new Error(
                Common.ErrorCode.Conflict, "La báscula no tiene puerto configurado. Revise Configuración."));
        }

        if (snapshot.ScaleUnit != request.ExpectedUnit)
        {
            return Result<ReadScaleWeightResponse>.Failure(new Error(
                Common.ErrorCode.Conflict,
                $"La báscula entrega {snapshot.ScaleUnit} y este producto se vende por {request.ExpectedUnit}. "
                    + "Ajuste la báscula o digite el peso."));
        }

        var reading = await reader.ReadAsync(snapshot, cancellationToken);
        if (reading.IsFailure)
        {
            return Result<ReadScaleWeightResponse>.Failure(reading.Error!);
        }

        if (!reading.Value.Stable)
        {
            return Result<ReadScaleWeightResponse>.Failure(new Error(
                Common.ErrorCode.Validation, "El peso no está estable. Espere un momento e intente de nuevo."));
        }

        if (reading.Value.WeightMilli <= 0)
        {
            return Result<ReadScaleWeightResponse>.Failure(new Error(
                Common.ErrorCode.Validation, "La báscula marcó cero o peso negativo. Verifique la carga."));
        }

        return Result<ReadScaleWeightResponse>.Success(new ReadScaleWeightResponse(reading.Value.WeightMilli, true));
    }
}
