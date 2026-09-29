-- Frubber POS
-- Migración: 001_Initial.sql
-- Diseño de base de datos: versión aprobada 1.0
-- El ejecutor de migraciones abre la transacción. Este script no ejecuta BEGIN/COMMIT.
-- La conexión debe habilitar foreign_keys, WAL, synchronous FULL y busy_timeout.

CREATE TABLE SchemaMigration (
    Version INTEGER NOT NULL PRIMARY KEY CHECK (Version > 0),
    ScriptName TEXT NOT NULL UNIQUE
        CHECK (ScriptName = trim(ScriptName) AND length(ScriptName) BETWEEN 1 AND 120),
    Checksum TEXT NOT NULL
        CHECK (
            length(Checksum) = 64
            AND Checksum = upper(Checksum)
            AND Checksum NOT GLOB '*[^0-9A-F]*'
        ),
    AppliedAtUtcMs INTEGER NOT NULL CHECK (AppliedAtUtcMs > 0)
) STRICT;

CREATE TABLE SystemSequence (
    SequenceName TEXT NOT NULL PRIMARY KEY
        CHECK (SequenceName = trim(SequenceName) AND length(SequenceName) BETWEEN 1 AND 60),
    LastValue INTEGER NOT NULL CHECK (LastValue >= 0)
) STRICT;

CREATE TABLE "User" (
    UserId INTEGER PRIMARY KEY,
    DisplayName TEXT NOT NULL
        CHECK (
            DisplayName = trim(DisplayName)
            AND length(DisplayName) BETWEEN 1 AND 60
        ),
    Role TEXT NOT NULL CHECK (Role IN ('ADMIN', 'CASHIER')),
    IsActive INTEGER NOT NULL CHECK (IsActive IN (0, 1)),
    PinSalt BLOB NOT NULL CHECK (length(PinSalt) = 16),
    PinHash BLOB NOT NULL CHECK (length(PinHash) = 32),
    PinIterations INTEGER NOT NULL CHECK (PinIterations > 0),
    PinAlgorithmVersion INTEGER NOT NULL CHECK (PinAlgorithmVersion > 0)
) STRICT;

CREATE TABLE Product (
    ProductId INTEGER PRIMARY KEY,
    Plu TEXT NOT NULL
        CHECK (
            Plu = trim(Plu)
            AND Plu = upper(Plu)
            AND length(Plu) BETWEEN 1 AND 32
            AND Plu NOT GLOB '*[^A-Z0-9]*'
        ),
    Name TEXT NOT NULL
        CHECK (
            Name = trim(Name)
            AND length(Name) BETWEEN 1 AND 120
        ),
    Unit TEXT NOT NULL CHECK (Unit IN ('KG', 'LB', 'UNIT')),
    PriceCop INTEGER NOT NULL CHECK (PriceCop > 0),
    StockMilli INTEGER NOT NULL,
    IsActive INTEGER NOT NULL CHECK (IsActive IN (0, 1)),
    UnitLocked INTEGER NOT NULL CHECK (UnitLocked IN (0, 1)),
    CHECK (Unit <> 'UNIT' OR StockMilli % 1000 = 0),
    CHECK (StockMilli = 0 OR UnitLocked = 1)
) STRICT;

CREATE TABLE CashSession (
    CashSessionId INTEGER PRIMARY KEY,
    OpenedAtUtcMs INTEGER NOT NULL CHECK (OpenedAtUtcMs > 0),
    OpenedAtOffsetMinutes INTEGER NOT NULL CHECK (OpenedAtOffsetMinutes BETWEEN -840 AND 840),
    OpenedByUserId INTEGER NOT NULL,
    OpenedByNameSnapshot TEXT NOT NULL
        CHECK (
            OpenedByNameSnapshot = trim(OpenedByNameSnapshot)
            AND length(OpenedByNameSnapshot) BETWEEN 1 AND 60
        ),
    OpeningBalanceCop INTEGER NOT NULL CHECK (OpeningBalanceCop >= 0),
    ClosedAtUtcMs INTEGER,
    ClosedAtOffsetMinutes INTEGER,
    ClosedByUserId INTEGER,
    ClosedByNameSnapshot TEXT,
    ClosedCashSalesCop INTEGER,
    ClosedTransferSalesCop INTEGER,
    ClosedDiscountCop INTEGER,
    ClosedNetSalesCop INTEGER,
    ClosedExpectedCashCop INTEGER,
    ClosedSaleCount INTEGER,
    FOREIGN KEY (OpenedByUserId) REFERENCES "User" (UserId) ON DELETE RESTRICT,
    FOREIGN KEY (ClosedByUserId) REFERENCES "User" (UserId) ON DELETE RESTRICT,
    CHECK (
        (
            ClosedAtUtcMs IS NULL
            AND ClosedAtOffsetMinutes IS NULL
            AND ClosedByUserId IS NULL
            AND ClosedByNameSnapshot IS NULL
            AND ClosedCashSalesCop IS NULL
            AND ClosedTransferSalesCop IS NULL
            AND ClosedDiscountCop IS NULL
            AND ClosedNetSalesCop IS NULL
            AND ClosedExpectedCashCop IS NULL
            AND ClosedSaleCount IS NULL
        )
        OR
        (
            ClosedAtUtcMs IS NOT NULL
            AND ClosedAtUtcMs > 0
            AND ClosedAtOffsetMinutes IS NOT NULL
            AND ClosedAtOffsetMinutes BETWEEN -840 AND 840
            AND ClosedByUserId IS NOT NULL
            AND ClosedByNameSnapshot IS NOT NULL
            AND ClosedByNameSnapshot = trim(ClosedByNameSnapshot)
            AND length(ClosedByNameSnapshot) BETWEEN 1 AND 60
            AND ClosedCashSalesCop IS NOT NULL
            AND ClosedCashSalesCop >= 0
            AND ClosedTransferSalesCop IS NOT NULL
            AND ClosedTransferSalesCop >= 0
            AND ClosedDiscountCop IS NOT NULL
            AND ClosedDiscountCop >= 0
            AND ClosedNetSalesCop IS NOT NULL
            AND ClosedNetSalesCop >= 0
            AND ClosedExpectedCashCop IS NOT NULL
            AND ClosedExpectedCashCop >= 0
            AND ClosedSaleCount IS NOT NULL
            AND ClosedSaleCount >= 0
            AND ClosedNetSalesCop = ClosedCashSalesCop + ClosedTransferSalesCop
            AND ClosedExpectedCashCop = OpeningBalanceCop + ClosedCashSalesCop
        )
    )
) STRICT;

CREATE TABLE Sale (
    SaleId INTEGER PRIMARY KEY,
    SaleNumber INTEGER NOT NULL CHECK (SaleNumber > 0),
    CashSessionId INTEGER NOT NULL,
    CashierUserId INTEGER NOT NULL,
    CashierNameSnapshot TEXT NOT NULL
        CHECK (
            CashierNameSnapshot = trim(CashierNameSnapshot)
            AND length(CashierNameSnapshot) BETWEEN 1 AND 60
        ),
    SoldAtUtcMs INTEGER NOT NULL CHECK (SoldAtUtcMs > 0),
    SoldAtOffsetMinutes INTEGER NOT NULL CHECK (SoldAtOffsetMinutes BETWEEN -840 AND 840),
    SubtotalCop INTEGER NOT NULL CHECK (SubtotalCop > 0),
    DiscountCop INTEGER NOT NULL CHECK (DiscountCop >= 0 AND DiscountCop <= SubtotalCop),
    TotalCop INTEGER NOT NULL CHECK (TotalCop = SubtotalCop - DiscountCop),
    PaymentMethod TEXT NOT NULL CHECK (PaymentMethod IN ('CASH', 'TRANSFER')),
    ReceivedCop INTEGER,
    ChangeCop INTEGER,
    FOREIGN KEY (CashSessionId) REFERENCES CashSession (CashSessionId) ON DELETE RESTRICT,
    FOREIGN KEY (CashierUserId) REFERENCES "User" (UserId) ON DELETE RESTRICT,
    CHECK (
        (
            PaymentMethod = 'CASH'
            AND ReceivedCop IS NOT NULL
            AND ReceivedCop >= TotalCop
            AND ChangeCop IS NOT NULL
            AND ChangeCop = ReceivedCop - TotalCop
        )
        OR
        (
            PaymentMethod = 'TRANSFER'
            AND ReceivedCop IS NULL
            AND ChangeCop IS NULL
        )
    )
) STRICT;

CREATE TABLE SaleLine (
    SaleLineId INTEGER PRIMARY KEY,
    SaleId INTEGER NOT NULL,
    LineNumber INTEGER NOT NULL CHECK (LineNumber > 0),
    ProductId INTEGER NOT NULL,
    PluSnapshot TEXT NOT NULL
        CHECK (
            PluSnapshot = trim(PluSnapshot)
            AND length(PluSnapshot) BETWEEN 1 AND 32
        ),
    ProductNameSnapshot TEXT NOT NULL
        CHECK (
            ProductNameSnapshot = trim(ProductNameSnapshot)
            AND length(ProductNameSnapshot) BETWEEN 1 AND 120
        ),
    UnitSnapshot TEXT NOT NULL CHECK (UnitSnapshot IN ('KG', 'LB', 'UNIT')),
    UnitPriceCopSnapshot INTEGER NOT NULL CHECK (UnitPriceCopSnapshot > 0),
    QuantityMilli INTEGER NOT NULL CHECK (QuantityMilli > 0),
    LineTotalCop INTEGER NOT NULL CHECK (LineTotalCop >= 0),
    FOREIGN KEY (SaleId) REFERENCES Sale (SaleId) ON DELETE RESTRICT,
    FOREIGN KEY (ProductId) REFERENCES Product (ProductId) ON DELETE RESTRICT,
    CHECK (UnitSnapshot <> 'UNIT' OR QuantityMilli % 1000 = 0),
    CHECK (LineTotalCop = (UnitPriceCopSnapshot * QuantityMilli + 500) / 1000)
) STRICT;

CREATE TABLE SaleVoid (
    SaleId INTEGER PRIMARY KEY,
    VoidedAtUtcMs INTEGER NOT NULL CHECK (VoidedAtUtcMs > 0),
    VoidedAtOffsetMinutes INTEGER NOT NULL CHECK (VoidedAtOffsetMinutes BETWEEN -840 AND 840),
    AdminUserId INTEGER NOT NULL,
    AdminNameSnapshot TEXT NOT NULL
        CHECK (
            AdminNameSnapshot = trim(AdminNameSnapshot)
            AND length(AdminNameSnapshot) BETWEEN 1 AND 60
        ),
    FOREIGN KEY (SaleId) REFERENCES Sale (SaleId) ON DELETE RESTRICT,
    FOREIGN KEY (AdminUserId) REFERENCES "User" (UserId) ON DELETE RESTRICT
) STRICT;

CREATE TABLE BusinessSetting (
    BusinessSettingId INTEGER PRIMARY KEY CHECK (BusinessSettingId = 1),
    BusinessName TEXT
        CHECK (
            BusinessName IS NULL
            OR (BusinessName = trim(BusinessName) AND length(BusinessName) BETWEEN 1 AND 120)
        ),
    TaxIdentifier TEXT
        CHECK (
            TaxIdentifier IS NULL
            OR (TaxIdentifier = trim(TaxIdentifier) AND length(TaxIdentifier) BETWEEN 1 AND 32)
        ),
    Phone TEXT
        CHECK (
            Phone IS NULL
            OR (Phone = trim(Phone) AND length(Phone) BETWEEN 1 AND 30)
        ),
    Address TEXT
        CHECK (
            Address IS NULL
            OR (Address = trim(Address) AND length(Address) BETWEEN 1 AND 200)
        ),
    TicketFooterMessage TEXT
        CHECK (
            TicketFooterMessage IS NULL
            OR (TicketFooterMessage = trim(TicketFooterMessage) AND length(TicketFooterMessage) BETWEEN 1 AND 250)
        ),
    PrinterName TEXT
        CHECK (
            PrinterName IS NULL
            OR (PrinterName = trim(PrinterName) AND length(PrinterName) BETWEEN 1 AND 260)
        )
) STRICT;

CREATE TABLE LicenseState (
    LicenseStateId INTEGER PRIMARY KEY CHECK (LicenseStateId = 1),
    PayloadVersion INTEGER NOT NULL CHECK (PayloadVersion > 0),
    ProtectedPayload BLOB NOT NULL CHECK (length(ProtectedPayload) > 0),
    UpdatedAtUtcMs INTEGER NOT NULL CHECK (UpdatedAtUtcMs > 0)
) STRICT;

CREATE UNIQUE INDEX UX_User_DisplayName_NoCase
    ON "User" (DisplayName COLLATE NOCASE);

CREATE INDEX IX_User_Active_DisplayName
    ON "User" (IsActive, DisplayName COLLATE NOCASE);

CREATE UNIQUE INDEX UX_Product_Plu_NoCase
    ON Product (Plu COLLATE NOCASE);

CREATE INDEX IX_Product_Active_Name
    ON Product (IsActive, Name COLLATE NOCASE);

CREATE UNIQUE INDEX UX_CashSession_OnlyOneOpen
    ON CashSession ((1))
    WHERE ClosedAtUtcMs IS NULL;

CREATE INDEX IX_CashSession_OpenedAt
    ON CashSession (OpenedAtUtcMs);

CREATE UNIQUE INDEX UX_Sale_SaleNumber
    ON Sale (SaleNumber);

CREATE INDEX IX_Sale_SoldAt
    ON Sale (SoldAtUtcMs);

CREATE INDEX IX_Sale_CashSession_SoldAt
    ON Sale (CashSessionId, SoldAtUtcMs);

CREATE INDEX IX_Sale_Cashier_SoldAt
    ON Sale (CashierUserId, SoldAtUtcMs);

CREATE UNIQUE INDEX UX_SaleLine_Sale_LineNumber
    ON SaleLine (SaleId, LineNumber);

CREATE INDEX IX_SaleLine_Product
    ON SaleLine (ProductId);

CREATE INDEX IX_SaleVoid_VoidedAt
    ON SaleVoid (VoidedAtUtcMs);

CREATE VIEW V_SaleHistory AS
SELECT
    s.SaleId,
    s.SaleNumber,
    s.CashSessionId,
    s.CashierUserId,
    s.CashierNameSnapshot,
    s.SoldAtUtcMs,
    s.SoldAtOffsetMinutes,
    s.SubtotalCop,
    s.DiscountCop,
    s.TotalCop,
    s.PaymentMethod,
    s.ReceivedCop,
    s.ChangeCop,
    CASE WHEN sv.SaleId IS NULL THEN 'ACTIVE' ELSE 'VOIDED' END AS SaleStatus,
    sv.VoidedAtUtcMs,
    sv.VoidedAtOffsetMinutes,
    sv.AdminUserId,
    sv.AdminNameSnapshot
FROM Sale AS s
LEFT JOIN SaleVoid AS sv ON sv.SaleId = s.SaleId;

CREATE VIEW V_OpenCashTotals AS
SELECT
    cs.CashSessionId,
    cs.OpeningBalanceCop,
    COALESCE(SUM(CASE
        WHEN sv.SaleId IS NULL AND s.PaymentMethod = 'CASH' THEN s.TotalCop
        ELSE 0
    END), 0) AS CashSalesCop,
    COALESCE(SUM(CASE
        WHEN sv.SaleId IS NULL AND s.PaymentMethod = 'TRANSFER' THEN s.TotalCop
        ELSE 0
    END), 0) AS TransferSalesCop,
    COALESCE(SUM(CASE
        WHEN sv.SaleId IS NULL THEN s.DiscountCop
        ELSE 0
    END), 0) AS DiscountCop,
    COALESCE(SUM(CASE
        WHEN sv.SaleId IS NULL THEN s.TotalCop
        ELSE 0
    END), 0) AS NetSalesCop,
    COALESCE(SUM(CASE
        WHEN s.SaleId IS NOT NULL AND sv.SaleId IS NULL THEN 1
        ELSE 0
    END), 0) AS SaleCount,
    cs.OpeningBalanceCop + COALESCE(SUM(CASE
        WHEN sv.SaleId IS NULL AND s.PaymentMethod = 'CASH' THEN s.TotalCop
        ELSE 0
    END), 0) AS ExpectedCashCop
FROM CashSession AS cs
LEFT JOIN Sale AS s ON s.CashSessionId = cs.CashSessionId
LEFT JOIN SaleVoid AS sv ON sv.SaleId = s.SaleId
WHERE cs.ClosedAtUtcMs IS NULL
GROUP BY cs.CashSessionId, cs.OpeningBalanceCop;

CREATE TRIGGER TR_Product_BlockUnitChange
BEFORE UPDATE OF Unit ON Product
FOR EACH ROW
WHEN OLD.UnitLocked = 1 AND NEW.Unit <> OLD.Unit
BEGIN
    SELECT RAISE(ABORT, 'La unidad del producto está bloqueada por historial o existencia');
END;

CREATE TRIGGER TR_Product_PreventUnitUnlock
BEFORE UPDATE OF UnitLocked ON Product
FOR EACH ROW
WHEN OLD.UnitLocked = 1 AND NEW.UnitLocked = 0
BEGIN
    SELECT RAISE(ABORT, 'El bloqueo de unidad es irreversible');
END;

CREATE TRIGGER TR_CashSession_ProtectOpeningFields
BEFORE UPDATE ON CashSession
FOR EACH ROW
WHEN
    NEW.OpenedAtUtcMs IS NOT OLD.OpenedAtUtcMs
    OR NEW.OpenedAtOffsetMinutes IS NOT OLD.OpenedAtOffsetMinutes
    OR NEW.OpenedByUserId IS NOT OLD.OpenedByUserId
    OR NEW.OpenedByNameSnapshot IS NOT OLD.OpenedByNameSnapshot
    OR NEW.OpeningBalanceCop IS NOT OLD.OpeningBalanceCop
BEGIN
    SELECT RAISE(ABORT, 'Los datos de apertura de caja son inmutables');
END;

CREATE TRIGGER TR_CashSession_NoUpdateAfterClose
BEFORE UPDATE ON CashSession
FOR EACH ROW
WHEN OLD.ClosedAtUtcMs IS NOT NULL
BEGIN
    SELECT RAISE(ABORT, 'Una caja cerrada es inmutable');
END;

CREATE TRIGGER TR_Sale_RequireOpenCashAndActiveUser
BEFORE INSERT ON Sale
FOR EACH ROW
BEGIN
    SELECT CASE WHEN NOT EXISTS (
        SELECT 1
        FROM CashSession AS cs
        WHERE cs.CashSessionId = NEW.CashSessionId
          AND cs.ClosedAtUtcMs IS NULL
    ) THEN RAISE(ABORT, 'La venta requiere una caja abierta') END;

    SELECT CASE WHEN NOT EXISTS (
        SELECT 1
        FROM "User" AS u
        WHERE u.UserId = NEW.CashierUserId
          AND u.IsActive = 1
          AND u.Role IN ('ADMIN', 'CASHIER')
    ) THEN RAISE(ABORT, 'El usuario de la venta no está activo o autorizado') END;
END;

CREATE TRIGGER TR_Sale_NoUpdate
BEFORE UPDATE ON Sale
FOR EACH ROW
BEGIN
    SELECT RAISE(ABORT, 'Sale es inmutable');
END;

CREATE TRIGGER TR_Sale_NoDelete
BEFORE DELETE ON Sale
FOR EACH ROW
BEGIN
    SELECT RAISE(ABORT, 'Sale no puede eliminarse');
END;

CREATE TRIGGER TR_SaleLine_NoUpdate
BEFORE UPDATE ON SaleLine
FOR EACH ROW
BEGIN
    SELECT RAISE(ABORT, 'SaleLine es inmutable');
END;

CREATE TRIGGER TR_SaleLine_NoDelete
BEFORE DELETE ON SaleLine
FOR EACH ROW
BEGIN
    SELECT RAISE(ABORT, 'SaleLine no puede eliminarse');
END;

CREATE TRIGGER TR_SaleVoid_RequireOpenCashAndAdmin
BEFORE INSERT ON SaleVoid
FOR EACH ROW
BEGIN
    SELECT CASE WHEN NOT EXISTS (
        SELECT 1
        FROM Sale AS s
        JOIN CashSession AS cs ON cs.CashSessionId = s.CashSessionId
        WHERE s.SaleId = NEW.SaleId
          AND cs.ClosedAtUtcMs IS NULL
    ) THEN RAISE(ABORT, 'Solo puede anularse una venta de una caja abierta') END;

    SELECT CASE WHEN NOT EXISTS (
        SELECT 1
        FROM "User" AS u
        WHERE u.UserId = NEW.AdminUserId
          AND u.IsActive = 1
          AND u.Role = 'ADMIN'
    ) THEN RAISE(ABORT, 'La anulación requiere un administrador activo') END;
END;

CREATE TRIGGER TR_SaleVoid_NoUpdate
BEFORE UPDATE ON SaleVoid
FOR EACH ROW
BEGIN
    SELECT RAISE(ABORT, 'SaleVoid es inmutable');
END;

CREATE TRIGGER TR_SaleVoid_NoDelete
BEFORE DELETE ON SaleVoid
FOR EACH ROW
BEGIN
    SELECT RAISE(ABORT, 'SaleVoid no puede eliminarse');
END;

INSERT INTO SystemSequence (SequenceName, LastValue)
VALUES ('SaleNumber', 0);

INSERT INTO BusinessSetting (BusinessSettingId)
VALUES (1);
