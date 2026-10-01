using FrubberPOS.Application.Authentication;
using FrubberPOS.Application.Common;
using FrubberPOS.Application.Persistence;

namespace FrubberPOS.Application.Business;

public sealed record GetBusinessSettingsRequest();

public sealed record UpdateBusinessSettingsRequest(
    string? BusinessName,
    string? TaxIdentifier,
    string? Phone,
    string? Address,
    string? TicketFooterMessage,
    string? PrinterName);

public sealed record UpdateBusinessSettingsResponse();

/// <summary>Reads the business settings row; empty columns come back as null (JHO-51).</summary>
public sealed class GetBusinessSettingsUseCase : IUseCase<GetBusinessSettingsRequest, BusinessSettingSnapshot>
{
    private readonly IBusinessSettingRepository settings;
    private readonly IUnitOfWorkFactory units;
    private readonly ICurrentSession session;

    public GetBusinessSettingsUseCase(IBusinessSettingRepository settings, IUnitOfWorkFactory units, ICurrentSession session)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(session);
        this.settings = settings;
        this.units = units;
        this.session = session;
    }

    public async Task<Result<BusinessSettingSnapshot>> ExecuteAsync(GetBusinessSettingsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var denied = Authorization.RequireAdmin(session);
        if (denied is not null)
        {
            return Result<BusinessSettingSnapshot>.Failure(denied);
        }

        await using var unit = await units.CreateAsync(UnitOfWorkMode.ReadOnly, cancellationToken);
        var row = await settings.GetAsync(unit, cancellationToken);
        return Result<BusinessSettingSnapshot>.Success(
            row ?? new BusinessSettingSnapshot(null, null, null, null, null, null));
    }
}

/// <summary>
/// Saves the business data and default printer (JHO-51, §15.1/15.2):
/// administrator-only, trimmed input, empty/whitespace means "not
/// configured" (stored as null), approved length limits enforced. No
/// printer width or device commands exist here — Windows printing only.
/// </summary>
public sealed class UpdateBusinessSettingsUseCase : IUseCase<UpdateBusinessSettingsRequest, UpdateBusinessSettingsResponse>
{
    public const int BusinessNameMaxLength = 120;
    public const int TaxIdentifierMaxLength = 32;
    public const int PhoneMaxLength = 30;
    public const int AddressMaxLength = 200;
    public const int TicketFooterMaxLength = 250;
    public const int PrinterNameMaxLength = 260;

    private readonly IBusinessSettingRepository settings;
    private readonly IUnitOfWorkFactory units;
    private readonly ICurrentSession session;

    public UpdateBusinessSettingsUseCase(IBusinessSettingRepository settings, IUnitOfWorkFactory units, ICurrentSession session)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(session);
        this.settings = settings;
        this.units = units;
        this.session = session;
    }

    public async Task<Result<UpdateBusinessSettingsResponse>> ExecuteAsync(UpdateBusinessSettingsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var denied = Authorization.RequireAdmin(session);
        if (denied is not null)
        {
            return Result<UpdateBusinessSettingsResponse>.Failure(denied);
        }

        var cleaned = new BusinessSettingSnapshot(
            TrimOrNull(request.BusinessName),
            TrimOrNull(request.TaxIdentifier),
            TrimOrNull(request.Phone),
            TrimOrNull(request.Address),
            TrimOrNull(request.TicketFooterMessage),
            TrimOrNull(request.PrinterName));

        var invalid =
            Validate("El nombre del negocio", cleaned.BusinessName, BusinessNameMaxLength)
            ?? Validate("La identificación (NIT)", cleaned.TaxIdentifier, TaxIdentifierMaxLength)
            ?? Validate("El teléfono", cleaned.Phone, PhoneMaxLength)
            ?? Validate("La dirección", cleaned.Address, AddressMaxLength)
            ?? Validate("El mensaje final", cleaned.TicketFooterMessage, TicketFooterMaxLength)
            ?? Validate("La impresora", cleaned.PrinterName, PrinterNameMaxLength);
        if (invalid is not null)
        {
            return Result<UpdateBusinessSettingsResponse>.Failure(invalid);
        }

        await using var unit = await units.CreateAsync(UnitOfWorkMode.WriteImmediate, cancellationToken);
        await settings.UpdateAsync(unit, cleaned, cancellationToken);
        await unit.CommitAsync(cancellationToken);
        return Result<UpdateBusinessSettingsResponse>.Success(new UpdateBusinessSettingsResponse());
    }

    private static string? TrimOrNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Error? Validate(string fieldLabel, string? value, int maxLength) =>
        value is not null && value.Length > maxLength
            ? new Error(ErrorCode.Validation, $"{fieldLabel} no puede superar {maxLength} caracteres.")
            : null;
}
