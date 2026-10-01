-- Migración: 003_OpeningBalanceCorrection.sql
-- Corrección del saldo inicial (JHO-61, regla acordada 2026-10-01): el
-- saldo de apertura solo puede cambiar mientras la sesión siga abierta
-- y no tenga ventas. Todo lo demás de la apertura sigue inmutable.

DROP TRIGGER TR_CashSession_ProtectOpeningFields;

CREATE TRIGGER TR_CashSession_ProtectOpeningFields
BEFORE UPDATE ON CashSession
FOR EACH ROW
WHEN
    NEW.OpenedAtUtcMs IS NOT OLD.OpenedAtUtcMs
    OR NEW.OpenedAtOffsetMinutes IS NOT OLD.OpenedAtOffsetMinutes
    OR NEW.OpenedByUserId IS NOT OLD.OpenedByUserId
    OR NEW.OpenedByNameSnapshot IS NOT OLD.OpenedByNameSnapshot
    OR (
        NEW.OpeningBalanceCop IS NOT OLD.OpeningBalanceCop
        AND (
            OLD.ClosedAtUtcMs IS NOT NULL
            OR EXISTS (
                SELECT 1
                FROM Sale AS s
                WHERE s.CashSessionId = OLD.CashSessionId
            )
        )
    )
BEGIN
    SELECT RAISE(ABORT, 'Los datos de apertura de caja son inmutables');
END;
