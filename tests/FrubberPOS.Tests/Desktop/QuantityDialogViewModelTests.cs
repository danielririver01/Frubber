using FrubberPOS.Application.Products;
using FrubberPOS.Desktop.ViewModels;
using FrubberPOS.Domain.Products;
using Xunit;

namespace FrubberPOS.Tests.Desktop;

/// <summary>
/// JHO-94 — modo «vender por monto» del diálogo de cantidad del TPV:
/// el cajero teclea los pesos, el VM deriva la cantidad con el precio del
/// producto (redondeo al gramo que nunca supera el monto tecleado) y la
/// vista previa muestra el total real antes de confirmar.
/// </summary>
public sealed class QuantityDialogViewModelTests
{
    private static ProductDto Tomate(long priceCop = 4_000) =>
        new(1, "101", "Tomate", ProductUnit.Kg, priceCop, 30_000, true, true);

    private static ProductDto CebollaLarga() =>
        new(2, "102", "Cebolla larga", ProductUnit.Kg, 3_500, 12_000, true, true);

    private static ProductDto LecheLibra() =>
        new(3, "103", "Leche libra", ProductUnit.Lb, 7_000, 40_000, true, true);

    private static ProductDto Bolsa() =>
        new(4, "104", "Bolsa de arroz", ProductUnit.Unit, 2_000, 50, true, true);

    private static (QuantityDialogViewModel Vm, List<bool> Closed) Create(ProductDto product)
    {
        var vm = new QuantityDialogViewModel();
        var closed = new List<bool>();
        vm.RequestClose += (_, ok) => closed.Add(ok);
        vm.Load(product);
        return (vm, closed);
    }

    // ---------- Disponibilidad del modo ----------

    [Fact]
    public void Amount_mode_available_for_Kg_and_Lb()
    {
        var (kg, _) = Create(Tomate());
        var (lb, _) = Create(LecheLibra());

        Assert.True(kg.AmountModeAvailable);
        Assert.True(lb.AmountModeAvailable);
    }

    [Fact]
    public void Amount_mode_not_available_for_Unit()
    {
        var (vm, _) = Create(Bolsa());

        Assert.False(vm.AmountModeAvailable);
    }

    [Fact]
    public void Amount_mode_cannot_be_enabled_for_Unit()
    {
        var (vm, _) = Create(Bolsa());

        vm.AmountMode = true;

        Assert.False(vm.IsAmountMode);
        Assert.True(vm.IsQuantityMode);
    }

    // ---------- Cálculo de kilos desde el monto ----------

    [Fact]
    public void Amount_2000_at_price_4000_yields_500_grams()
    {
        var (vm, closed) = Create(Tomate());
        vm.AmountMode = true;
        vm.AmountText = "2000";

        vm.Confirm();

        Assert.Equal(500, vm.ParsedMilli);
        Assert.Single(closed);
        Assert.True(closed[0]);
        Assert.True(vm.AmountModeAvailable);
    }

    [Fact]
    public void Amount_with_group_separators_yields_same_result()
    {
        var (vm, closed) = Create(Tomate());
        vm.AmountMode = true;
        vm.AmountText = "2.000";

        vm.Confirm();

        Assert.Equal(500, vm.ParsedMilli);
        Assert.Single(closed);
        Assert.True(closed[0]);
    }

    [Fact]
    public void Amount_total_never_exceeds_typed_amount()
    {
        // $2.000 a $3.500/kg = 571,43 g → el sistema toma 571 g = $1.999
        // (nunca 572 g = $2.002, que costaría más de lo pedido).
        var (vm, closed) = Create(CebollaLarga());
        vm.AmountMode = true;
        vm.AmountText = "2000";

        vm.Confirm();

        Assert.Equal(571, vm.ParsedMilli);
        var total = (3_500 * vm.ParsedMilli + 500) / 1_000;
        Assert.True(total <= 2_000, $"el total ${total} supera el monto tecleado");
        Assert.Single(closed);
        Assert.True(closed[0]);
    }

    [Fact]
    public void Amount_rounds_up_to_the_closest_total_not_above_the_amount()
    {
        // A $4.000/kg, $2.010 → 502,5 g; el más cercano por debajo es 502 g ($2.008).
        var (vm, _) = Create(Tomate());
        vm.AmountMode = true;
        vm.AmountText = "2010";

        vm.Confirm();

        Assert.Equal(502, vm.ParsedMilli);
    }

    // ---------- Rechazos ----------

    [Fact]
    public void Amount_below_price_of_one_gram_is_rejected()
    {
        var (vm, closed) = Create(Tomate()); // 1 g = $4
        vm.AmountMode = true;
        vm.AmountText = "3";

        vm.Confirm();

        Assert.Empty(closed);
        Assert.Equal(0, vm.ParsedMilli);
        Assert.False(string.IsNullOrWhiteSpace(vm.ErrorMessage));
    }

    [Fact]
    public void Non_numeric_amount_is_rejected()
    {
        var (vm, closed) = Create(Tomate());
        vm.AmountMode = true;
        vm.AmountText = "abc";

        vm.Confirm();

        Assert.Empty(closed);
        Assert.Equal(0, vm.ParsedMilli);
        Assert.False(string.IsNullOrWhiteSpace(vm.ErrorMessage));
    }

    [Fact]
    public void Zero_amount_is_rejected()
    {
        var (vm, closed) = Create(Tomate());
        vm.AmountMode = true;
        vm.AmountText = "0";

        vm.Confirm();

        Assert.Empty(closed);
        Assert.False(string.IsNullOrWhiteSpace(vm.ErrorMessage));
    }

    [Fact]
    public void Empty_amount_does_not_close()
    {
        var (vm, closed) = Create(Tomate());
        vm.AmountMode = true;
        vm.AmountText = "";

        vm.Confirm();

        Assert.Empty(closed);
    }

    // ---------- Vista previa antes de confirmar ----------

    [Fact]
    public void Preview_shows_equivalence_before_confirming_in_amount_mode()
    {
        var (vm, _) = Create(Tomate());

        vm.AmountMode = true;
        vm.AmountText = "2000";

        // Formato es-CO: los ceros finales se recortan (0,500 se muestra «0,5»).
        Assert.Equal("Equivale a 0,5 KG → $ 2.000", vm.PreviewMessage);
        Assert.True(vm.HasPreview);
    }

    [Fact]
    public void Preview_shows_line_total_in_quantity_mode()
    {
        var (vm, _) = Create(Tomate());

        vm.QuantityText = "0,750";

        Assert.Equal("0,75 KG → $ 3.000", vm.PreviewMessage);
        Assert.True(vm.HasPreview);
    }

    [Fact]
    public void Preview_shows_minimum_when_amount_cannot_buy_one_gram()
    {
        var (vm, _) = Create(Tomate());

        vm.AmountMode = true;
        vm.AmountText = "3";

        Assert.Equal("Mínimo $ 4", vm.PreviewMessage);
        Assert.True(vm.HasPreview);
    }

    // ---------- Transiciones de modo ----------

    [Fact]
    public void Toggling_amount_mode_clears_both_inputs()
    {
        var (vm, _) = Create(Tomate());
        vm.QuantityText = "1,250";

        vm.AmountMode = true;

        Assert.Equal(string.Empty, vm.QuantityText);
        Assert.Equal(string.Empty, vm.AmountText);
        Assert.Equal(string.Empty, vm.PreviewMessage);

        vm.AmountText = "5000";
        vm.AmountMode = false;

        Assert.Equal(string.Empty, vm.AmountText);
    }

    [Fact]
    public void Input_hints_and_titles_follow_the_active_mode()
    {
        var (vm, _) = Create(Tomate());

        Assert.Equal("Cantidad", vm.DialogTitle);
        Assert.Equal("Cantidad (KG)", vm.InputHint);

        vm.AmountMode = true;

        Assert.Equal("Vender por monto", vm.DialogTitle);
        Assert.Equal("Monto a cobrar ($)", vm.InputHint);
        Assert.Equal("Precio: $ 4.000 / KG", vm.PriceHint);
    }

    // ---------- El modo normal sigue intacto ----------

    [Fact]
    public void Quantity_mode_works_unchanged_with_scale_free_entry()
    {
        var (vm, closed) = Create(Tomate());

        vm.QuantityText = "0,500";
        vm.Confirm();

        Assert.Equal(500, vm.ParsedMilli);
        Assert.Single(closed);
        Assert.True(closed[0]);
        Assert.False(vm.IsAmountMode);
    }

    [Fact]
    public void Unit_quantity_stays_integer_in_quantity_mode()
    {
        var (vm, closed) = Create(Bolsa());

        vm.QuantityText = "2,5";
        vm.Confirm();

        Assert.Empty(closed);
        Assert.False(string.IsNullOrWhiteSpace(vm.ErrorMessage));
    }
}
