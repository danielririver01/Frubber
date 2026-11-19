using System.ComponentModel;
using System.Globalization;
using FrubberPOS.Application.Workstations;
using FrubberPOS.Desktop.Common;

namespace FrubberPOS.Desktop.ViewModels;

public sealed class DailySalesViewModel(GetDailySalesUseCase getDailySales) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public string DateText { get; private set; } = string.Empty;
    public string TotalText { get; private set; } = string.Empty;
    public string ErrorMessage { get; private set; } = string.Empty;
    public IReadOnlyList<DailySalesDisplayRow> Rows { get; private set; } = [];
    public bool HasReport { get; private set; }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var result = await getDailySales.ExecuteAsync(new(), cancellationToken);
        HasReport = result.IsSuccess;
        if (HasReport)
        {
            var report = result.Value;
            DateText = report.BusinessDate.ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("es-CO"));
            TotalText = MoneyText.FormatCop(report.TotalCop);
            Rows = report.Workstations.Select(row =>
                new DailySalesDisplayRow(row.TerminalName, MoneyText.FormatCop(row.TotalCop))).ToArray();
        }
        ErrorMessage = result.Error?.Message ?? string.Empty;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }
}

public sealed record DailySalesDisplayRow(string TerminalName, string TotalText);
