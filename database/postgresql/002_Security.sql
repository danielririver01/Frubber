-- =============================================================================
--  Frubber POS 2.0.0
--  Archivo: database/postgresql/002_Security.sql
--  M2-28 · Roles por puesto, permisos mínimos y PIN por función SECURITY DEFINER
--  (ADR-029 punto 10 · ADR-026 reformulada · decisión AD-03 · enmienda S-3)
--
--  QUÉ HACE:
--    1. Deja la base cerrada por omisión: sin CONNECT ni CREATE para el público.
--    2. Crea el rol de grupo "frubber_puesto" (NOLOGIN) con la MATRIZ DE
--       PERMISOS mínima. Cada puesto real es un rol LOGIN miembro de ese grupo.
--    3. REVOCA toda escritura directa sobre las tablas críticas (venta, caja,
--       licencia...): un puesto conectado con su rol NO puede insertar,
--       modificar ni borrar nada. Las rutas de escritura llegan después como
--       funciones SECURITY DEFINER (M2-29 numeración, M2-19 cobro idempotente).
--    4. Verifica el PIN dentro del motor con funciones SECURITY DEFINER, para
--       que el hash del PIN NUNCA viaje al cliente ni sea legible por él.
--       Límite de intentos fallos (5 en fila) con espera creciente y registro
--       de accesos fallidos en pin_attempt_log.
--    5. Progistra (alta y desactivación) de puestos por función: el rol de
--       cada puesto se crea con contraseña generada por el motor, que el
--       administrador guarda con DPAPI en el archivo de configuración del
--       puesto (nunca en texto plano).
--
--  CÓMO SE APLICA (psql, sobre la base con 001_Baseline aplicado):
--     psql -U postgres -d frubber -v ON_ERROR_STOP=1 -f 002_Security.sql
--
--  DISEÑO DEL PIN (decisión documentada, M2-28):
--    * 1. app_pin_challenge(user_id) -> sal, iteraciones, versión del algoritmo.
--      La sal NO es secreta (su defensa es el PIN de 4 dígitos + PBKDF2).
--    * 2. El puesto calcula PBKDF2-HMAC-SHA-256 LOCALMENTE (mismo algoritmo y
--      210 000 iteraciones de la 1.0, Pbkdf2PinHasher). El PIN en claro NUNCA
--      sale del puesto: ni siquiera viaja por la red en texto.
--    * 3. app_pin_verify(user_id, hash_candidato) -> lo compara el MOTOR
--      (SECURITY DEFINER) contra el hash guardado. El hash guardado nunca
--      vuelve al cliente. La comparación es por igualdad de bytes; se acepta
--      documentado el canal de tiempo, igual que el riesgo residual del issue.
--    * 4. Límite: 5 fallos consecutivos bloquean. Cada bloqueo duplica la
--      espera (2, 4, 8, 16, 30 min y tope 30). El éxito limpia el contador.
--    * 5. Cada intento queda en pin_attempt_log con resultado OK/FALLO/BLOQUEO
--      y el rol que lo hizo (current_user = puesto_xxx).
--    Riesgo residual registrado: un cliente SQL malicioso podría envolver los
--      intentos en transacciones que se deshacen (ROLLBACK) y con ello evadir
--      el contador; se acepta por escrito (AD-03 / H-07) porque la defensa de
--      fondo es el tamaño del PIN (4 dígitos) + PBKDF2, y el límite existe para
--      frenar el ataque en línea de clientes que no hacen eso.
--
--  El ejecutor abre la transacción. Este guion no hace BEGIN/COMMIT.
-- =============================================================================

SET client_min_messages = WARNING;

-- -----------------------------------------------------------------------------
-- 0 · EXTENSIÓN PARA LA CONTRASEÑA ALEATORIA DEL PUESTO (pgcrypto: gen_random_bytes)
-- -----------------------------------------------------------------------------
CREATE EXTENSION IF NOT EXISTS pgcrypto;


-- -----------------------------------------------------------------------------
-- 1 · LIMPIEZA DEL BANCO DE PROTOTIPO ( quedó del guion verificacion-postgresql.sql
--     de M2-27, cuya limpieza final quedó comentada. El esquema "frubber_test"
--     nunca se creó en esta base; el rol sí. )
-- -----------------------------------------------------------------------------
DO $todo$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'frubber_puesto_test') THEN
        DROP ROLE frubber_puesto_test;
        RAISE NOTICE 'Prototipo: rol frubber_puesto_test eliminado';
    END IF;
END
$todo$;


-- -----------------------------------------------------------------------------
-- 2 · FRONTERA DE LA BASE: cerrar el acceso por omisión
-- -----------------------------------------------------------------------------
DO $todo$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'frubber_puesto') THEN
        CREATE ROLE frubber_puesto NOLOGIN;
        RAISE NOTICE 'Rol de grupo frubber_puesto creado';
    ELSE
        RAISE NOTICE 'Rol de grupo frubber_puesto ya existia';
    END IF;
END
$todo$;

REVOKE ALL ON DATABASE frubber FROM PUBLIC;
GRANT CONNECT ON DATABASE frubber TO postgres;
GRANT CONNECT ON DATABASE frubber TO frubber_puesto;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT USAGE ON SCHEMA public TO PUBLIC;

-- -----------------------------------------------------------------------------
-- 3 · MATRIZ DE PERMISOS DEL PUESTO (rol de grupo)
-- -----------------------------------------------------------------------------
GRANT USAGE ON SCHEMA public TO frubber_puesto;


-- LECTURAS de trabajo del TPV (mínimo):
GRANT SELECT ON product            TO frubber_puesto;   -- catalogo para vender
GRANT SELECT ON business_setting   TO frubber_puesto;   -- encabezado del ticket
-- HISTORIAL, TOTALES y ESTADO DE CAJA: por VISTAS (así el puesto no lee las
-- tablas críticas; cash_session queda sin grants de lectura directa):
GRANT SELECT ON v_sale_history     TO frubber_puesto;
GRANT SELECT ON v_open_cash_totals TO frubber_puesto;

-- ESCRITURA: ninguna tabla es escrita directamente por un puesto.
REVOKE ALL ON sale        FROM frubber_puesto;
REVOKE ALL ON sale_line   FROM frubber_puesto;
REVOKE ALL ON sale_void   FROM frubber_puesto;
REVOKE ALL ON cash_session FROM frubber_puesto;
REVOKE ALL ON license_state FROM frubber_puesto;
REVOKE ALL ON app_user    FROM frubber_puesto;
REVOKE ALL ON terminal    FROM frubber_puesto;
REVOKE ALL ON ticket_counter FROM frubber_puesto;
REVOKE ALL ON schema_migration FROM frubber_puesto;

-- La tabla de usuarios se consulta SOLO por la función del punto 4 (para la
-- pantalla de login). Cualquier SELECT directo falla.
REVOKE SELECT, UPDATE, DELETE, TRUNCATE ON app_user FROM frubber_puesto;

-- El puesto NO puede definir sus propios objetos en el motor.
REVOKE CREATE ON SCHEMA public FROM frubber_puesto;


-- -----------------------------------------------------------------------------
-- 4 · TABLAS DE SEGURIDAD DEL PIN
-- -----------------------------------------------------------------------------
CREATE TABLE pin_attempt_log (
    attempt_id    bigserial   PRIMARY KEY,
    user_id       bigint      NOT NULL REFERENCES app_user (user_id) ON DELETE RESTRICT,
    outcome       text        NOT NULL CHECK (outcome IN ('OK', 'FALLO', 'BLOQUEO')),
    attempted_by  name        NOT NULL DEFAULT current_user,
    attempted_at  timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX ix_pin_attempt_log_user_time ON pin_attempt_log (user_id, attempted_at);

CREATE TABLE pin_lock_state (
    user_id               bigint PRIMARY KEY REFERENCES app_user (user_id) ON DELETE RESTRICT,
    consecutive_failures  integer     NOT NULL DEFAULT 0 CHECK (consecutive_failures >= 0),
    blocked_count         integer     NOT NULL DEFAULT 0 CHECK (blocked_count >= 0),
    blocked_until         timestamptz,
    updated_at            timestamptz NOT NULL DEFAULT now()
);

REVOKE ALL ON pin_attempt_log FROM frubber_puesto;
REVOKE ALL ON pin_lock_state  FROM frubber_puesto;
REVOKE SELECT, UPDATE, DELETE, TRUNCATE ON pin_attempt_log FROM frubber_puesto;
REVOKE SELECT, UPDATE, DELETE, TRUNCATE ON pin_lock_state  FROM frubber_puesto;


-- -----------------------------------------------------------------------------
-- 5 · FUNCIONES DE PIN Y DE TRABAJO (SECURITY DEFINER: corren con los permisos
--     del dueño)
--     search_path anclado a public, pg_temp: nadie puede secuestrar nombres.
--     Todas REVOGAN EXECUTE de PUBLIC; al puesto se le devuelven solo las que
--     necesita (challenge, verify, directorio y terminal_info).
-- -----------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION app_pin_challenge(p_user_id bigint)
RETURNS TABLE (pin_salt bytea, pin_iterations integer, pin_algorithm_version integer)
LANGUAGE sql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $$
    SELECT pin_salt, pin_iterations, pin_algorithm_version
      FROM app_user
     WHERE user_id = p_user_id;
$$;

CREATE OR REPLACE FUNCTION app_pin_verify(p_user_id bigint, p_candidate bytea)
RETURNS TABLE (authenticated boolean, blocked boolean, wait_seconds integer)
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $func$
DECLARE
    v_is_active      boolean;
    v_stored_hash    bytea;
    v_failures       integer;
    v_blocks         integer;
    v_blocked_until  timestamptz;
    v_wait_seconds   integer;
    v_remaining      integer;
    v_match          boolean;
BEGIN
    SELECT is_active, pin_hash INTO v_is_active, v_stored_hash
      FROM app_user
     WHERE user_id = p_user_id;

    IF NOT FOUND THEN
        -- Usuario desconocido: no hay estado que contar (no existe fila).
        RETURN QUERY SELECT false, false, 0::integer;
        RETURN;
    END IF;

    SELECT consecutive_failures, blocked_count, blocked_until
      INTO v_failures, v_blocks, v_blocked_until
      FROM pin_lock_state
     WHERE user_id = p_user_id;
    IF NOT FOUND THEN
        v_failures := 0; v_blocks := 0; v_blocked_until := NULL;
    END IF;

    -- ¿Sigue bloqueado?
    IF v_blocked_until IS NOT NULL AND v_blocked_until > clock_timestamp() THEN
        v_remaining := CEIL(EXTRACT(EPOCH FROM (v_blocked_until - clock_timestamp())))::integer;
        INSERT INTO pin_attempt_log (user_id, outcome) VALUES (p_user_id, 'BLOQUEO');
        RETURN QUERY SELECT false, true, v_remaining;
        RETURN;
    END IF;

    IF p_candidate IS NULL OR octet_length(p_candidate) <> 32 THEN
        v_match := false;
    ELSE
        v_match := (v_stored_hash = p_candidate);
    END IF;

    IF v_is_active AND v_match THEN
        INSERT INTO pin_attempt_log (user_id, outcome) VALUES (p_user_id, 'OK');
        -- Éxito: limpia el contador y cualquier bloqueo pendiente.
        INSERT INTO pin_lock_state (user_id, consecutive_failures, blocked_count, blocked_until, updated_at)
            VALUES (p_user_id, 0, 0, NULL, now())
        ON CONFLICT (user_id) DO UPDATE
            SET consecutive_failures = 0,
                blocked_count        = 0,
                blocked_until        = NULL,
                updated_at           = now();
        RETURN QUERY SELECT true, false, 0::integer;
        RETURN;
    END IF;

    -- Fallo: acumula y decide bloqueo.
    v_failures := v_failures + 1;
    IF v_failures >= 5 THEN
        -- Espera creciente: 2^bloques previos * 2 minutos, tope 30 minutos.
        v_wait_seconds := LEAST(120 * (2 ^ LEAST(v_blocks, 5)), 1800)::integer;
        v_blocks := v_blocks + 1;
        v_blocked_until := clock_timestamp() + make_interval(secs => v_wait_seconds);
        UPDATE pin_lock_state
           SET consecutive_failures = v_failures,
               blocked_count        = v_blocks,
               blocked_until        = v_blocked_until,
               updated_at           = now()
         WHERE user_id = p_user_id;
        IF NOT FOUND THEN
            INSERT INTO pin_lock_state (user_id, consecutive_failures, blocked_count, blocked_until, updated_at)
                VALUES (p_user_id, v_failures, v_blocks, v_blocked_until, now());
        END IF;
        -- El intento falló Y dispara el bloqueo: queda como FALLO con retorno bloqueado.
        INSERT INTO pin_attempt_log (user_id, outcome) VALUES (p_user_id, 'FALLO');
        RETURN QUERY SELECT false, true, v_wait_seconds;
        RETURN;
    ELSE
        UPDATE pin_lock_state
           SET consecutive_failures = v_failures,
               blocked_until        = NULL,
               updated_at           = now()
         WHERE user_id = p_user_id;
        IF NOT FOUND THEN
            INSERT INTO pin_lock_state (user_id, consecutive_failures, blocked_count, blocked_until, updated_at)
                VALUES (p_user_id, v_failures, 0, NULL, now());
        END IF;
        INSERT INTO pin_attempt_log (user_id, outcome) VALUES (p_user_id, 'FALLO');
        RETURN QUERY SELECT false, false, 0::integer;
        RETURN;
    END IF;
END
$func$;


-- Directorio de usuarios para la pantalla de login (nombres y rol, sin hash):
CREATE OR REPLACE FUNCTION app_user_directory()
RETURNS TABLE (user_id bigint, display_name text, user_role text)
LANGUAGE sql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $$
    SELECT user_id, display_name, role
      FROM app_user
     WHERE is_active
     ORDER BY display_name;
$$;


-- -----------------------------------------------------------------------------
-- 6 · CICLO DE VIDA DEL PUESTO (solo administrador del motor)
-- -----------------------------------------------------------------------------
-- Alta: crea la fila terminal y el rol LOGIN del puesto, miembro de
-- frubber_puesto. Devuelve rol y contraseña UNA VEZ; quien ejecuta la guarda
-- con DPAPI en el archivo de configuración del puesto (sin texto plano).
CREATE OR REPLACE FUNCTION register_terminal(p_name text, p_machine_code char(16))
RETURNS TABLE (db_role text, generated_password text)
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $func$
DECLARE
    v_machine_clean char(16) := upper(btrim(p_machine_code::text));
    v_role_name     text;
    v_password      text;
BEGIN
    IF p_name IS NULL OR btrim(p_name) = '' OR length(btrim(p_name)) > 60 THEN
        RAISE EXCEPTION 'El nombre del puesto es obligatorio (1 a 60 caracteres)';
    END IF;
    IF v_machine_clean !~ '^[0-9A-F]{16}$' THEN
        RAISE EXCEPTION 'El código de equipo debe ser de 16 caracteres hexadecimales';
    END IF;
    IF v_machine_clean = '0000000000000000' THEN
        RAISE EXCEPTION 'El código 0000000000000000 es el marcador del puesto desconocido (1.0)';
    END IF;
    IF EXISTS (SELECT 1 FROM terminal WHERE machine_code = v_machine_clean) THEN
        RAISE EXCEPTION 'Ya esta autorizado un puesto con el codigo de equipo %', v_machine_clean;
    END IF;

    v_role_name := 'puesto_' || lower(v_machine_clean::text);
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = v_role_name) THEN
        RAISE EXCEPTION 'El rol de base de datos % ya existe; resolver antes de repetir la alta', v_role_name;
    END IF;

    v_password := encode(gen_random_bytes(24), 'hex');

    INSERT INTO terminal (name, machine_code, is_active)
        VALUES (btrim(p_name), v_machine_clean, true);

    EXECUTE format('CREATE ROLE %I LOGIN PASSWORD %L IN ROLE frubber_puesto CONNECTION LIMIT 12',
                   v_role_name, v_password);

    RETURN QUERY SELECT v_role_name, v_password;
END
$func$;

-- Desactivación: el puesto deja de poder conectarse (y su fila queda marcada).
CREATE OR REPLACE FUNCTION deactivate_terminal(p_machine_code char(16))
RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $func$
DECLARE
    v_machine_clean char(16) := upper(btrim(p_machine_code::text));
    v_role_name     text;
BEGIN
    UPDATE terminal SET is_active = false WHERE machine_code = v_machine_clean;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'No existe un puesto con el codigo de equipo %', v_machine_clean;
    END IF;

    v_role_name := 'puesto_' || lower(v_machine_clean::text);
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = v_role_name) THEN
        EXECUTE format('ALTER ROLE %I NOLOGIN', v_role_name);
    END IF;
END
$func$;

-- Diagnóstico y latido: el puesto consulta su PROPIO registro (nombre, estado)
-- y de paso actualiza última vez visto. Solo ve su terminal, no los demás.
CREATE OR REPLACE FUNCTION terminal_info()
RETURNS TABLE (terminal_id bigint, terminal_name text, is_active boolean, last_seen_at timestamptz)
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $func$
DECLARE
    v_user  text := session_user;
    v_code  text;
BEGIN
    IF v_user !~ '^puesto_[0-9a-f]{16}$' THEN
        RAISE EXCEPTION 'La sesion no es de un puesto del negocio';
    END IF;
    v_code := upper(substr(v_user, 8));
    UPDATE terminal SET last_seen_at = clock_timestamp() WHERE machine_code = v_code;
    RETURN QUERY
        SELECT t.terminal_id, t.name, t.is_active, t.last_seen_at
          FROM terminal t
         WHERE t.machine_code = v_code;
END
$func$;


-- -----------------------------------------------------------------------------
-- 7 · PERMISOS DE EJECUCIÓN DE LAS FUNCIONES
-- -----------------------------------------------------------------------------
REVOKE EXECUTE ON FUNCTION app_pin_challenge(bigint)      FROM PUBLIC;
REVOKE EXECUTE ON FUNCTION app_pin_verify(bigint, bytea)  FROM PUBLIC;
REVOKE EXECUTE ON FUNCTION app_user_directory()           FROM PUBLIC;
REVOKE EXECUTE ON FUNCTION register_terminal(text, char(16))      FROM PUBLIC;
REVOKE EXECUTE ON FUNCTION deactivate_terminal(char(16))          FROM PUBLIC;
REVOKE EXECUTE ON FUNCTION terminal_info()                        FROM PUBLIC;

GRANT EXECUTE ON FUNCTION app_pin_challenge(bigint)     TO frubber_puesto;
GRANT EXECUTE ON FUNCTION app_pin_verify(bigint, bytea) TO frubber_puesto;
GRANT EXECUTE ON FUNCTION app_user_directory()          TO frubber_puesto;
GRANT EXECUTE ON FUNCTION terminal_info()               TO frubber_puesto;
-- La alta y baja de puestos queda SOLO para el dueño (postgres, y más adelante
-- el rol de administración que decidan M2-10/M2-32).

-- -----------------------------------------------------------------------------
-- 8 · PG_HBA.CONF — BLOQUE A PEGAR EN EL SERVIDOR DEL LOCAL (M2-32 lo instala):
--
--   local   all             postgres                                scram-sha-256
--   host    frubber         all             127.0.0.1/32            scram-sha-256
--   host    frubber         all             192.168.1.0/24          scram-sha-256
--   host    all             all             127.0.0.1/32            scram-sha-256
--   host    all             all             ::1/128                 scram-sha-256
--
--   Con solo esas líneas: nadie de la subred puede usar otro método de
--   autenticación scram, nadie de fuera de la subred entra, y las bases de
--   otros dominios (los clientes de asistentes de código, etc) siguen
--   conectando igual. 192.168.1.0/24 es EJEMPLO: el instalador pone la subred
--   real del local.
-- -----------------------------------------------------------------------------
