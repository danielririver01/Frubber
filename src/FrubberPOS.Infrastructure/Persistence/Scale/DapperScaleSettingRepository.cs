using Dapper;
using FrubberPOS.Application.Persistence;
using FrubberPOS.Application.Scale;

namespace FrubberPOS.Infrastructure.Persistence.Scale;

/// <summary>Single-row scale settings (migration 004, JHO-86).</summary>
public sealed class DapperScaleSettingRepository : IScaleSettingRepository
{
    public async Task<ScaleSnapshot?> GetAsync(IUnitOfWork unit, CancellationToken cancellationToken)
    {
        var row = await unit.Connection.QuerySingleOrDefaultAsync<Row>(
            new CommandDefinition(
                "SELECT ScaleSettingId, IsEnabled, PortName, BaudRate, Protocol, ScaleUnit, ReadTimeoutMs"
                + " FROM ScaleSetting WHERE ScaleSettingId = 1;",
                transaction: unit.Transaction,
                cancellationToken: cancellationToken));
        return row is null
            ? null
            : new ScaleSnapshot(row.IsEnabled == 1, row.PortName, (int)row.BaudRate, row.Protocol, row.ScaleUnit, (int)row.ReadTimeoutMs);
    }

    public async Task UpdateAsync(IUnitOfWork unit, ScaleSnapshot settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await unit.Connection.ExecuteAsync(
            new CommandDefinition(
                "UPDATE ScaleSetting"
                + " SET IsEnabled = @IsEnabled, PortName = @PortName, BaudRate = @BaudRate,"
                + " Protocol = @Protocol, ScaleUnit = @ScaleUnit, ReadTimeoutMs = @ReadTimeoutMs"
                + " WHERE ScaleSettingId = 1;",
                new
                {
                    IsEnabled = settings.IsEnabled ? 1 : 0,
                    settings.PortName,
                    settings.BaudRate,
                    settings.Protocol,
                    settings.ScaleUnit,
                    settings.ReadTimeoutMs,
                },
                transaction: unit.Transaction,
                cancellationToken: cancellationToken));
    }

    private sealed class Row
    {
        public long IsEnabled { get; set; }

        public string? PortName { get; set; }

        public long BaudRate { get; set; }

        public string Protocol { get; set; } = string.Empty;

        public string ScaleUnit { get; set; } = string.Empty;

        public long ReadTimeoutMs { get; set; }
    }
}
