namespace FrubberPOS.DataMigration;

internal sealed record ColumnMap(string Source, string Target);
internal sealed record TableMap(string Source, string Target, IReadOnlyList<ColumnMap> Columns);

internal static class TableMaps
{
    public static readonly TableMap Users = new("User", "app_user", [
        new("UserId", "user_id"),
        new("DisplayName", "display_name"),
        new("Role", "role"),
        new("IsActive", "is_active"),
        new("PinSalt", "pin_salt"),
        new("PinHash", "pin_hash"),
        new("PinIterations", "pin_iterations"),
        new("PinAlgorithmVersion", "pin_algorithm_version")]);

    public static readonly TableMap Products = new("Product", "product", [
        new("ProductId", "product_id"),
        new("Plu", "plu"),
        new("Name", "name"),
        new("Unit", "unit"),
        new("PriceCop", "price_cop"),
        new("StockMilli", "stock_milli"),
        new("IsActive", "is_active"),
        new("UnitLocked", "unit_locked")]);

    public static readonly TableMap Cash = new("CashSession", "cash_session", [
        new("CashSessionId", "cash_session_id"),
        new("OpenedAtUtcMs", "opened_at"),
        new("OpenedAtOffsetMinutes", "legacy_opened_at_offset_minutes"),
        new("OpenedByUserId", "opened_by_user_id"),
        new("OpenedByNameSnapshot", "opened_by_name_snapshot"),
        new("OpeningBalanceCop", "opening_balance_cop"),
        new("ClosedAtUtcMs", "closed_at"),
        new("ClosedAtOffsetMinutes", "closed_at_legacy_offset_minutes"),
        new("ClosedByUserId", "closed_by_user_id"),
        new("ClosedByNameSnapshot", "closed_by_name_snapshot"),
        new("ClosedCashSalesCop", "closed_cash_sales_cop"),
        new("ClosedTransferSalesCop", "closed_transfer_sales_cop"),
        new("ClosedDiscountCop", "closed_discount_cop"),
        new("ClosedNetSalesCop", "closed_net_sales_cop"),
        new("ClosedExpectedCashCop", "closed_expected_cash_cop"),
        new("ClosedSaleCount", "closed_sale_count")]);

    public static readonly TableMap Sales = new("Sale", "sale", [
        new("SaleId", "sale_id"),
        new("SaleNumber", "sale_number"),
        new("CashSessionId", "cash_session_id"),
        new("CashierUserId", "cashier_user_id"),
        new("CashierNameSnapshot", "cashier_name_snapshot"),
        new("SoldAtUtcMs", "sold_at"),
        new("SoldAtOffsetMinutes", "legacy_sold_at_offset_minutes"),
        new("SubtotalCop", "subtotal_cop"),
        new("DiscountCop", "discount_cop"),
        new("TotalCop", "total_cop"),
        new("PaymentMethod", "payment_method"),
        new("ReceivedCop", "received_cop"),
        new("ChangeCop", "change_cop")]);

    public static readonly TableMap Lines = new("SaleLine", "sale_line", [
        new("SaleLineId", "sale_line_id"),
        new("SaleId", "sale_id"),
        new("LineNumber", "line_number"),
        new("ProductId", "product_id"),
        new("PluSnapshot", "plu_snapshot"),
        new("ProductNameSnapshot", "product_name_snapshot"),
        new("UnitSnapshot", "unit_snapshot"),
        new("UnitPriceCopSnapshot", "unit_price_cop_snapshot"),
        new("QuantityMilli", "quantity_milli"),
        new("LineTotalCop", "line_total_cop")]);

    public static readonly TableMap Voids = new("SaleVoid", "sale_void", [
        new("SaleId", "sale_id"),
        new("VoidedAtUtcMs", "voided_at"),
        new("VoidedAtOffsetMinutes", "legacy_voided_at_offset_minutes"),
        new("AdminUserId", "voided_by_user_id"),
        new("AdminNameSnapshot", "voided_by_name_snapshot")]);

    public static readonly TableMap Settings = new("BusinessSetting", "business_setting", [
        new("BusinessSettingId", "business_setting_id"),
        new("BusinessName", "business_name"),
        new("TaxIdentifier", "tax_identifier"),
        new("Phone", "phone"),
        new("Address", "address"),
        new("TicketFooterMessage", "ticket_footer_message"),
        new("PrinterName", "printer_name")]);

    public static readonly TableMap Trial = new("TrialState", "trial_state", [
        new("TrialStateId", "trial_state_id"),
        new("FirstRunUtcMs", "first_run_at"),
        new("LastSeenUtcMs", "last_seen_at")]);

    public static readonly TableMap Scale = new("ScaleSetting", "scale_setting", [
        new("ScaleSettingId", "scale_setting_id"),
        new("IsEnabled", "is_enabled"),
        new("PortName", "port_name"),
        new("BaudRate", "baud_rate"),
        new("Protocol", "protocol"),
        new("ScaleUnit", "scale_unit"),
        new("ReadTimeoutMs", "read_timeout_ms")]);

    public static readonly TableMap[] All = [Users, Products, Cash, Sales, Lines, Voids, Settings, Trial, Scale];
}
