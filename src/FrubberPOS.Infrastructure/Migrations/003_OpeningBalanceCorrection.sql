-- Migración: 003_OpeningBalanceCorrection.sql
-- Corrección del saldo inicial (JHO-61, regla acordada 2026-10-01): el
-- saldo de apertura solo puede cambiar mientras la sesión siga abierta
-- y no tenga ventas. Todo lo demás de la apertura sigue inmutable.
-- Portado a PostgreSQL (JHO-113).

DROP TRIGGER IF EXISTS TR_CashSession_ProtectOpeningFields ON CashSession;

CREATE OR REPLACE FUNCTION tr_cash_session_protect_opening_fields() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF
        NEW.OpenedAtUtcMs IS DISTINCT FROM OLD.OpenedAtUtcMs
        OR NEW.OpenedAtOffsetMinutes IS DISTINCT FROM OLD.OpenedAtOffsetMinutes
        OR NEW.OpenedByUserId IS DISTINCT FROM OLD.OpenedByUserId
        OR NEW.OpenedByNameSnapshot IS DISTINCT FROM OLD.OpenedByNameSnapshot
        OR (
            NEW.OpeningBalanceCop IS DISTINCT FROM OLD.OpeningBalanceCop
            AND (
                OLD.ClosedAtUtcMs IS NOT NULL
                OR EXISTS (
                    SELECT 1
                    FROM Sale AS s
                    WHERE s.CashSessionId = OLD.CashSessionId
                )
            )
        )
    THEN
        RAISE EXCEPTION 'Los datos de apertura de caja son inmutables';
    END IF;
    RETURN NEW;
END;
$$;

CREATE TRIGGER TR_CashSession_ProtectOpeningFields
BEFORE UPDATE ON CashSession
FOR EACH ROW
EXECUTE FUNCTION tr_cash_session_protect_opening_fields();
