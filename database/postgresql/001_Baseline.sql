-- =============================================================================
--  Frubber POS 2.0.0
--  Archivo: database/postgresql/001_Baseline.sql
--  Línea base del esquema en PostgreSQL 18.6 (M2-27 · ADR-029, enmiendas B-1/B-2/B-3)
--
--  QUÉ ES: el esquema de la 2.0, creado DESDE CERO sobre un motor limpio.
--  Las migraciones 001-003 de SQLite quedan como historia de la 1.0 y NO se
--  reutilizan (ADR-029, consecuencias "en contra"). Esto NO contradice la regla
--  de migraciones inmutables: es una línea base nueva, escrita así a propósito.
--
--  CÓMO SE APLICA (psql, sobre una base vacía):
--     createdb -U postgres frubber
--     psql -U postgres -d frubber -v ON_ERROR_STOP=1 -f 001_Baseline.sql
--
--  TIPOS (decisión de diseño, M2-27):
--    * booleanos reales        -> boolean       (antes 0/1 en SQLite)
--    * marcas de tiempo        -> timestamptz   (antes ms UTC + offset; la hora la pone el motor)
--    * dinero (COP enteros)    -> bigint        (ADR-009: sin double/float/decimal; sin redondeo binario)
--    * cantidades (milésimas)  -> bigint        (1.5 kg = 1500; aritmética exacta en enteros)
--  Nota: el documento 2.2 v2 menciona "numeric para dinero". Se usa bigint para
--  cumplir ADR-009 (prohibición de decimal persistido) y mantener el Domain (long)
--  reutilizable sin tocar las 462 pruebas. Si se requiere soporte de centavos,
--  cambiar a numeric(14,2) es un ajuste acotado.
--
--  REGLAS DE ORO que este esquema debe garantizar (2.2 v2 §4):
--    3. Una caja abierta por puesto  -> índice único parcial (terminal_id) WHERE closed_at IS NULL
--    4. Numeración sin huecos        -> contador con bloqueo de fila (ticket_counter + next_ticket())
--    5. Inmutabilidad de las ventas  -> los 12 disparadores en PL/pgSQL
--
--  El ejecutor (o psql con ON_ERROR_STOP) abre la transacción. Este script no
--  ejecuta BEGIN/COMMIT: lo maneja quien lo aplica.
-- =============================================================================

SET client_min_messages = WARNING;


-- =============================================================================
--  TABLA 1 · schema_migration
--  Catálogo de versiones aplicadas de la línea base de la 2.0 (con checksum).
-- =============================================================================
CREATE TABLE schema_migration (
    version       integer     NOT NULL PRIMARY KEY CHECK (version > 0),
    script_name   text        NOT NULL UNIQUE
        CHECK (script_name = btrim(script_name) AND length(script_name) BETWEEN 1 AND 120),
    checksum      text        NOT NULL
        CHECK (length(checksum) = 64 AND checksum = upper(checksum) AND checksum !~ '[^0-9A-F]'),
    applied_at    timestamptz NOT NULL DEFAULT now()
);


-- =============================================================================
--  TABLA 2 · terminal  (NUEVA en la 2.0)
--  Registra los puestos (cajas). El código de equipo es el mismo de 16
--  caracteres que ya se usa para la licencia (ADR-013 / ADR-025).
-- =============================================================================
CREATE TABLE terminal (
    terminal_id    bigserial  PRIMARY KEY,
    name           text       NOT NULL
        CHECK (name = btrim(name) AND length(name) BETWEEN 1 AND 60),
    machine_code   char(16)   NOT NULL UNIQUE
        CHECK (machine_code = upper(machine_code)),
    is_active      boolean    NOT NULL DEFAULT true,
    registered_at  timestamptz NOT NULL DEFAULT now(),
    last_seen_at   timestamptz
);


-- =============================================================================
--  TABLA 3 · app_user  (antes "User"; renombrada para evitar palabra reservada)
--  PIN con PBKDF2: sal 16 bytes, hash 32 bytes. Nunca se loguea (ADR-011).
-- =============================================================================
CREATE TABLE app_user (
    user_id                bigserial PRIMARY KEY,
    display_name           text NOT NULL
        CHECK (display_name = btrim(display_name) AND length(display_name) BETWEEN 1 AND 60),
    role                   text NOT NULL CHECK (role IN ('ADMIN', 'CASHIER')),
    is_active              boolean NOT NULL,
    pin_salt               bytea NOT NULL CHECK (octet_length(pin_salt) = 16),
    pin_hash               bytea NOT NULL CHECK (octet_length(pin_hash) = 32),
    pin_iterations         integer NOT NULL CHECK (pin_iterations > 0),
    pin_algorithm_version  integer NOT NULL CHECK (pin_algorithm_version > 0)
);


-- =============================================================================
--  TABLA 4 · product
--  Unidad bloqueada por historial o existencia: irreversible (disparadores 1 y 2).
-- =============================================================================
CREATE TABLE product (
    product_id    bigserial PRIMARY KEY,
    plu           text NOT NULL
        CHECK (plu = btrim(plu) AND plu = upper(plu) AND plu ~ '^[A-Z0-9]{1,32}$'),
    name          text NOT NULL
        CHECK (name = btrim(name) AND length(name) BETWEEN 1 AND 120),
    unit          text NOT NULL CHECK (unit IN ('KG', 'LB', 'UNIT')),
    price_cop     bigint NOT NULL CHECK (price_cop > 0),
    stock_milli   bigint NOT NULL,
    is_active     boolean NOT NULL,
    unit_locked   boolean NOT NULL,
    CHECK (unit <> 'UNIT' OR stock_milli % 1000 = 0),
    CHECK (stock_milli = 0 OR unit_locked)
);


-- =============================================================================
--  TABLA 5 · cash_session
--  Una caja abierta POR PUESTO (TerminalId + índice único parcial, más abajo).
--  Las cajas heredadas de la 1.0 quedan asignadas al terminal "puesto
--  desconocido (versión 1.0)" que siembra este guion.
-- =============================================================================
CREATE TABLE cash_session (
    cash_session_id           bigserial PRIMARY KEY,
    terminal_id               bigint NOT NULL REFERENCES terminal (terminal_id) ON DELETE RESTRICT,
    opened_at                 timestamptz NOT NULL DEFAULT now(),
    opened_by_user_id         bigint NOT NULL REFERENCES app_user (user_id) ON DELETE RESTRICT,
    opened_by_name_snapshot   text NOT NULL
        CHECK (opened_by_name_snapshot = btrim(opened_by_name_snapshot)
               AND length(opened_by_name_snapshot) BETWEEN 1 AND 60),
    opening_balance_cop       bigint NOT NULL CHECK (opening_balance_cop >= 0),
    closed_at                 timestamptz,
    closed_by_user_id         bigint REFERENCES app_user (user_id) ON DELETE RESTRICT,
    closed_by_name_snapshot   text
        CHECK (closed_by_name_snapshot IS NULL
               OR (closed_by_name_snapshot = btrim(closed_by_name_snapshot)
                   AND length(closed_by_name_snapshot) BETWEEN 1 AND 60)),
    closed_cash_sales_cop     bigint,
    closed_transfer_sales_cop bigint,
    closed_discount_cop       bigint,
    closed_net_sales_cop      bigint,
    closed_expected_cash_cop  bigint,
    closed_sale_count         integer,
    -- Apertura y cierre son exclusivos: o está abierta (todo de cierre NULL) o cerrada.
    CHECK (
        (
            closed_at IS NULL
            AND closed_by_user_id IS NULL
            AND closed_by_name_snapshot IS NULL
            AND closed_cash_sales_cop IS NULL
            AND closed_transfer_sales_cop IS NULL
            AND closed_discount_cop IS NULL
            AND closed_net_sales_cop IS NULL
            AND closed_expected_cash_cop IS NULL
            AND closed_sale_count IS NULL
        )
        OR
        (
            closed_at IS NOT NULL
            AND closed_by_user_id IS NOT NULL
            AND closed_by_name_snapshot IS NOT NULL
            AND closed_cash_sales_cop IS NOT NULL AND closed_cash_sales_cop >= 0
            AND closed_transfer_sales_cop IS NOT NULL AND closed_transfer_sales_cop >= 0
            AND closed_discount_cop IS NOT NULL AND closed_discount_cop >= 0
            AND closed_net_sales_cop IS NOT NULL AND closed_net_sales_cop >= 0
            AND closed_expected_cash_cop IS NOT NULL AND closed_expected_cash_cop >= 0
            AND closed_sale_count IS NOT NULL AND closed_sale_count >= 0
            AND closed_net_sales_cop = closed_cash_sales_cop + closed_transfer_sales_cop
            AND closed_expected_cash_cop = opening_balance_cop + closed_cash_sales_cop
        )
    )
);

-- REGLA DE ORO 3 · una caja abierta por puesto. Índice ÚNICO PARCIAL: solo mira
-- las filas con closed_at IS NULL. Nativo en PostgreSQL; lo impide la base.
CREATE UNIQUE INDEX ux_cash_session_abierta_por_puesto
    ON cash_session (terminal_id)
    WHERE closed_at IS NULL;

CREATE INDEX ix_cash_session_opened_at ON cash_session (opened_at);


-- =============================================================================
--  TABLA 6 · ticket_counter  (NUEVA; reemplaza a SystemSequence para tickets)
--  REGLA DE ORO 4 · numeración sin huecos. El número se consume con
--  UPDATE ... RETURNING (bloqueo de fila) DENTRO de la transacción de la venta:
--  si la venta falla, la transacción se deshace y el número se libera.
-- =============================================================================
CREATE TABLE ticket_counter (
    scope       text    PRIMARY KEY CHECK (scope = btrim(scope) AND length(scope) BETWEEN 1 AND 40),
    last_number bigint  NOT NULL DEFAULT 0 CHECK (last_number >= 0)
);

INSERT INTO ticket_counter (scope, last_number) VALUES ('venta', 0);

-- Consume el siguiente número de ticket con bloqueo de fila.
CREATE FUNCTION next_ticket() RETURNS bigint
LANGUAGE plpgsql AS $$
DECLARE n bigint;
BEGIN
    UPDATE ticket_counter
       SET last_number = last_number + 1
     WHERE scope = 'venta'
    RETURNING last_number INTO n;

    IF n IS NULL THEN
        RAISE EXCEPTION 'Contador de tickets no inicializado';
    END IF;
    RETURN n;
END $$;


-- =============================================================================
--  TABLA 7 · sale
--  ClientRequestId (uuid único) da IDEMPOTENCIA al cobro (ADR-029, enmienda B-1):
--  un corte de red en pleno cobro no duplica ni pierde la venta.
--  "¿Desde qué puesto se vendió?" se resuelve por la caja (venta -> caja -> puesto),
--  sin duplicar el puesto dentro de la venta inmutable.
-- =============================================================================
CREATE TABLE sale (
    sale_id             bigserial PRIMARY KEY,
    sale_number         bigint NOT NULL CHECK (sale_number > 0),
    cash_session_id     bigint NOT NULL REFERENCES cash_session (cash_session_id) ON DELETE RESTRICT,
    cashier_user_id     bigint NOT NULL REFERENCES app_user (user_id) ON DELETE RESTRICT,
    cashier_name_snapshot text NOT NULL
        CHECK (cashier_name_snapshot = btrim(cashier_name_snapshot)
               AND length(cashier_name_snapshot) BETWEEN 1 AND 60),
    sold_at             timestamptz NOT NULL DEFAULT now(),
    subtotal_cop        bigint NOT NULL CHECK (subtotal_cop > 0),
    discount_cop        bigint NOT NULL CHECK (discount_cop >= 0 AND discount_cop <= subtotal_cop),
    total_cop           bigint NOT NULL CHECK (total_cop = subtotal_cop - discount_cop),
    payment_method      text NOT NULL CHECK (payment_method IN ('CASH', 'TRANSFER')),
    received_cop        bigint,
    change_cop          bigint,
    client_request_id   uuid NOT NULL UNIQUE,   -- idempotencia del cobro
    CHECK (
        (
            payment_method = 'CASH'
            AND received_cop IS NOT NULL AND received_cop >= total_cop
            AND change_cop IS NOT NULL AND change_cop = received_cop - total_cop
        )
        OR
        (
            payment_method = 'TRANSFER'
            AND received_cop IS NULL
            AND change_cop IS NULL
        )
    )
);

CREATE UNIQUE INDEX ux_sale_sale_number ON sale (sale_number);
CREATE INDEX ix_sale_sold_at ON sale (sold_at);
CREATE INDEX ix_sale_cash_session_sold_at ON sale (cash_session_id, sold_at);
CREATE INDEX ix_sale_cashier_sold_at ON sale (cashier_user_id, sold_at);


-- =============================================================================
--  TABLA 8 · sale_line
--  La línea congela plu, nombre, unidad y precio al momento de vender.
--  Aritmética exacta en enteros: LineTotal = (precio * cantidad_milli + 500) / 1000.
-- =============================================================================
CREATE TABLE sale_line (
    sale_line_id             bigserial PRIMARY KEY,
    sale_id                  bigint NOT NULL REFERENCES sale (sale_id) ON DELETE RESTRICT,
    line_number              integer NOT NULL CHECK (line_number > 0),
    product_id               bigint NOT NULL REFERENCES product (product_id) ON DELETE RESTRICT,
    plu_snapshot             text NOT NULL
        CHECK (plu_snapshot = btrim(plu_snapshot) AND length(plu_snapshot) BETWEEN 1 AND 32),
    product_name_snapshot    text NOT NULL
        CHECK (product_name_snapshot = btrim(product_name_snapshot)
               AND length(product_name_snapshot) BETWEEN 1 AND 120),
    unit_snapshot            text NOT NULL CHECK (unit_snapshot IN ('KG', 'LB', 'UNIT')),
    unit_price_cop_snapshot  bigint NOT NULL CHECK (unit_price_cop_snapshot > 0),
    quantity_milli           bigint NOT NULL CHECK (quantity_milli > 0),
    line_total_cop           bigint NOT NULL CHECK (line_total_cop >= 0),
    CHECK (unit_snapshot <> 'UNIT' OR quantity_milli % 1000 = 0),
    CHECK (line_total_cop = (unit_price_cop_snapshot * quantity_milli + 500) / 1000)
);

CREATE UNIQUE INDEX ux_sale_line_sale_line_number ON sale_line (sale_id, line_number);
CREATE INDEX ix_sale_line_product ON sale_line (product_id);


-- =============================================================================
--  TABLA 9 · sale_void
--  Anulación: requiere caja abierta y administrador activo (disparador 10).
-- =============================================================================
CREATE TABLE sale_void (
    sale_id                bigint PRIMARY KEY REFERENCES sale (sale_id) ON DELETE RESTRICT,
    voided_at              timestamptz NOT NULL DEFAULT now(),
    voided_by_user_id      bigint NOT NULL REFERENCES app_user (user_id) ON DELETE RESTRICT,
    voided_by_name_snapshot text NOT NULL
        CHECK (voided_by_name_snapshot = btrim(voided_by_name_snapshot)
               AND length(voided_by_name_snapshot) BETWEEN 1 AND 60)
);

CREATE INDEX ix_sale_void_voided_at ON sale_void (voided_at);


-- =============================================================================
--  TABLA 10 · business_setting  (fila única, id = 1)
-- =============================================================================
CREATE TABLE business_setting (
    business_setting_id   smallint PRIMARY KEY CHECK (business_setting_id = 1),
    business_name         text
        CHECK (business_name IS NULL
               OR (business_name = btrim(business_name) AND length(business_name) BETWEEN 1 AND 120)),
    tax_identifier        text
        CHECK (tax_identifier IS NULL
               OR (tax_identifier = btrim(tax_identifier) AND length(tax_identifier) BETWEEN 1 AND 32)),
    phone                 text
        CHECK (phone IS NULL OR (phone = btrim(phone) AND length(phone) BETWEEN 1 AND 30)),
    address               text
        CHECK (address IS NULL OR (address = btrim(address) AND length(address) BETWEEN 1 AND 200)),
    ticket_footer_message text
        CHECK (ticket_footer_message IS NULL
               OR (ticket_footer_message = btrim(ticket_footer_message)
                   AND length(ticket_footer_message) BETWEEN 1 AND 250)),
    printer_name          text
        CHECK (printer_name IS NULL
               OR (printer_name = btrim(printer_name) AND length(printer_name) BETWEEN 1 AND 260))
);

INSERT INTO business_setting (business_setting_id) VALUES (1);


-- =============================================================================
--  TABLA 11 · license_state  (fila única, id = 1)
--  La 2.0 guarda la licencia como CONTENIDO FIRMADO (ECDSA, formato FRB2 o FRB1)
--  verificable en cada arranque con la clave pública embebida (ADR-030).
--  Sin sello DPAPI: se elimina el bloqueante H-01.
-- =============================================================================
CREATE TABLE license_state (
    license_state_id   smallint PRIMARY KEY CHECK (license_state_id = 1),
    payload_version    integer NOT NULL CHECK (payload_version > 0),
    signed_payload     text    NOT NULL CHECK (length(signed_payload) > 0),  -- FRB2|keyId|MACHINE|fecha|cupo.firma
    updated_at         timestamptz NOT NULL DEFAULT now()
);


-- =============================================================================
--  SEMILLA · terminal heredado de la 1.0
--  Las cajas anteriores a la 2.0 quedan como "puesto desconocido (versión 1.0)"
--  (documento 2.3). Así no hay que tocar una caja cerrada ni apagar el disparador
--  que la protege. El código "0000000000000000" es el marcador de "desconocido".
-- =============================================================================
INSERT INTO terminal (name, machine_code, is_active)
VALUES ('Puesto desconocido (version 1.0)', '0000000000000000', false);


-- =============================================================================
--  LOS 12 DISPARADORES · PL/pgSQL  (REGLA DE ORO 5)
--  Misma intención que en la 1.0 (001_Initial.sql): la protección vive en el
--  motor, por encima de cualquier cliente, incluso de alguien conectado por fuera.
-- =============================================================================

-- Disparador 1 · la unidad bloqueada no cambia.
CREATE FUNCTION trg_product_block_unit_change() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    IF OLD.unit_locked AND NEW.unit IS DISTINCT FROM OLD.unit THEN
        RAISE EXCEPTION 'La unidad del producto esta bloqueada por historial o existencia'
            USING ERRCODE = 'restrict_violation';
    END IF;
    RETURN NEW;
END $$;

CREATE TRIGGER tr_product_block_unit_change
    BEFORE UPDATE OF unit ON product
    FOR EACH ROW EXECUTE FUNCTION trg_product_block_unit_change();


-- Disparador 2 · el bloqueo de unidad es irreversible.
CREATE FUNCTION trg_product_prevent_unit_unlock() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    IF OLD.unit_locked AND NOT NEW.unit_locked THEN
        RAISE EXCEPTION 'El bloqueo de unidad es irreversible'
            USING ERRCODE = 'restrict_violation';
    END IF;
    RETURN NEW;
END $$;

CREATE TRIGGER tr_product_prevent_unit_unlock
    BEFORE UPDATE OF unit_locked ON product
    FOR EACH ROW EXECUTE FUNCTION trg_product_prevent_unit_unlock();


-- Disparador 3 · los datos de apertura de caja son inmutables.
CREATE FUNCTION trg_cash_session_protect_opening_fields() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    IF NEW.opened_at IS DISTINCT FROM OLD.opened_at
       OR NEW.opened_by_user_id IS DISTINCT FROM OLD.opened_by_user_id
       OR NEW.opened_by_name_snapshot IS DISTINCT FROM OLD.opened_by_name_snapshot
       OR NEW.opening_balance_cop IS DISTINCT FROM OLD.opening_balance_cop THEN
        RAISE EXCEPTION 'Los datos de apertura de caja son inmutables'
            USING ERRCODE = 'restrict_violation';
    END IF;
    RETURN NEW;
END $$;

CREATE TRIGGER tr_cash_session_protect_opening_fields
    BEFORE UPDATE ON cash_session
    FOR EACH ROW EXECUTE FUNCTION trg_cash_session_protect_opening_fields();


-- Disparador 4 · una caja cerrada es inmutable.
CREATE FUNCTION trg_cash_session_no_update_after_close() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    IF OLD.closed_at IS NOT NULL THEN
        RAISE EXCEPTION 'Una caja cerrada es inmutable'
            USING ERRCODE = 'restrict_violation';
    END IF;
    RETURN NEW;
END $$;

CREATE TRIGGER tr_cash_session_no_update_after_close
    BEFORE UPDATE ON cash_session
    FOR EACH ROW EXECUTE FUNCTION trg_cash_session_no_update_after_close();


-- Disparador 5 · la venta exige caja abierta y usuario activo autorizado.
CREATE FUNCTION trg_sale_require_open_cash_and_active_user() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM cash_session cs
         WHERE cs.cash_session_id = NEW.cash_session_id
           AND cs.closed_at IS NULL
    ) THEN
        RAISE EXCEPTION 'La venta requiere una caja abierta'
            USING ERRCODE = 'restrict_violation';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM app_user u
         WHERE u.user_id = NEW.cashier_user_id
           AND u.is_active
           AND u.role IN ('ADMIN', 'CASHIER')
    ) THEN
        RAISE EXCEPTION 'El usuario de la venta no esta activo o autorizado'
            USING ERRCODE = 'restrict_violation';
    END IF;

    RETURN NEW;
END $$;

CREATE TRIGGER tr_sale_require_open_cash_and_active_user
    BEFORE INSERT ON sale
    FOR EACH ROW EXECUTE FUNCTION trg_sale_require_open_cash_and_active_user();


-- Disparador 6 · la venta es inmutable (no se modifica).
CREATE FUNCTION trg_sale_no_update() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'Sale es inmutable'
        USING ERRCODE = 'restrict_violation';
END $$;

CREATE TRIGGER tr_sale_no_update
    BEFORE UPDATE ON sale
    FOR EACH ROW EXECUTE FUNCTION trg_sale_no_update();


-- Disparador 7 · la venta no se borra.
CREATE FUNCTION trg_sale_no_delete() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'Sale no puede eliminarse'
        USING ERRCODE = 'restrict_violation';
END $$;

CREATE TRIGGER tr_sale_no_delete
    BEFORE DELETE ON sale
    FOR EACH ROW EXECUTE FUNCTION trg_sale_no_delete();


-- Disparador 8 · la línea de venta es inmutable.
CREATE FUNCTION trg_sale_line_no_update() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'SaleLine es inmutable'
        USING ERRCODE = 'restrict_violation';
END $$;

CREATE TRIGGER tr_sale_line_no_update
    BEFORE UPDATE ON sale_line
    FOR EACH ROW EXECUTE FUNCTION trg_sale_line_no_update();


-- Disparador 9 · la línea de venta no se borra.
CREATE FUNCTION trg_sale_line_no_delete() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'SaleLine no puede eliminarse'
        USING ERRCODE = 'restrict_violation';
END $$;

CREATE TRIGGER tr_sale_line_no_delete
    BEFORE DELETE ON sale_line
    FOR EACH ROW EXECUTE FUNCTION trg_sale_line_no_delete();


-- Disparador 10 · la anulación exige caja abierta y administrador activo.
CREATE FUNCTION trg_sale_void_require_open_cash_and_admin() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
          FROM sale s
          JOIN cash_session cs ON cs.cash_session_id = s.cash_session_id
         WHERE s.sale_id = NEW.sale_id
           AND cs.closed_at IS NULL
    ) THEN
        RAISE EXCEPTION 'Solo puede anularse una venta de una caja abierta'
            USING ERRCODE = 'restrict_violation';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM app_user u
         WHERE u.user_id = NEW.voided_by_user_id
           AND u.is_active
           AND u.role = 'ADMIN'
    ) THEN
        RAISE EXCEPTION 'La anulacion requiere un administrador activo'
            USING ERRCODE = 'restrict_violation';
    END IF;

    RETURN NEW;
END $$;

CREATE TRIGGER tr_sale_void_require_open_cash_and_admin
    BEFORE INSERT ON sale_void
    FOR EACH ROW EXECUTE FUNCTION trg_sale_void_require_open_cash_and_admin();


-- Disparador 11 · la anulación es inmutable.
CREATE FUNCTION trg_sale_void_no_update() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'SaleVoid es inmutable'
        USING ERRCODE = 'restrict_violation';
END $$;

CREATE TRIGGER tr_sale_void_no_update
    BEFORE UPDATE ON sale_void
    FOR EACH ROW EXECUTE FUNCTION trg_sale_void_no_update();


-- Disparador 12 · la anulación no se borra.
CREATE FUNCTION trg_sale_void_no_delete() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'SaleVoid no puede eliminarse'
        USING ERRCODE = 'restrict_violation';
END $$;

CREATE TRIGGER tr_sale_void_no_delete
    BEFORE DELETE ON sale_void
    FOR EACH ROW EXECUTE FUNCTION trg_sale_void_no_delete();


-- =============================================================================
--  VISTAS  (misma intención que la 1.0; funcionan por caja)
-- =============================================================================
CREATE VIEW v_sale_history AS
SELECT
    s.sale_id,
    s.sale_number,
    s.cash_session_id,
    s.cashier_user_id,
    s.cashier_name_snapshot,
    s.sold_at,
    s.subtotal_cop,
    s.discount_cop,
    s.total_cop,
    s.payment_method,
    s.received_cop,
    s.change_cop,
    cs.terminal_id,
    CASE WHEN sv.sale_id IS NULL THEN 'ACTIVE' ELSE 'VOIDED' END AS sale_status,
    sv.voided_at,
    sv.voided_by_user_id,
    sv.voided_by_name_snapshot
FROM sale AS s
JOIN cash_session AS cs ON cs.cash_session_id = s.cash_session_id
LEFT JOIN sale_void AS sv ON sv.sale_id = s.sale_id;


CREATE VIEW v_open_cash_totals AS
SELECT
    cs.cash_session_id,
    cs.terminal_id,
    cs.opening_balance_cop,
    COALESCE(SUM(CASE WHEN sv.sale_id IS NULL AND s.payment_method = 'CASH'
                      THEN s.total_cop ELSE 0 END), 0) AS cash_sales_cop,
    COALESCE(SUM(CASE WHEN sv.sale_id IS NULL AND s.payment_method = 'TRANSFER'
                      THEN s.total_cop ELSE 0 END), 0) AS transfer_sales_cop,
    COALESCE(SUM(CASE WHEN sv.sale_id IS NULL THEN s.discount_cop ELSE 0 END), 0) AS discount_cop,
    COALESCE(SUM(CASE WHEN sv.sale_id IS NULL THEN s.total_cop ELSE 0 END), 0) AS net_sales_cop,
    COUNT(s.sale_id) FILTER (WHERE sv.sale_id IS NULL) AS sale_count,
    cs.opening_balance_cop
        + COALESCE(SUM(CASE WHEN sv.sale_id IS NULL AND s.payment_method = 'CASH'
                            THEN s.total_cop ELSE 0 END), 0) AS expected_cash_cop
FROM cash_session AS cs
LEFT JOIN sale AS s ON s.cash_session_id = cs.cash_session_id
LEFT JOIN sale_void AS sv ON sv.sale_id = s.sale_id
WHERE cs.closed_at IS NULL
GROUP BY cs.cash_session_id, cs.terminal_id, cs.opening_balance_cop;
