-- =============================================================================
--  Frubber POS 2.0.0 · Verificación del diseño sobre PostgreSQL 18.6
--  Archivo: prototipo/verificacion-postgresql.sql
--  Fecha: 20-oct-2026
--
--  QUÉ HACE: comprueba las CUATRO piezas de riesgo del diseño antes de
--  escribir el esquema definitivo. No es el esquema real: es un banco de
--  pruebas. Crea todo en el esquema "frubber_test" y lo borra al final.
--
--  CÓMO SE CORRE (con psql, no con pgAdmin, porque usa \echo):
--     psql -U postgres -f verificacion-postgresql.sql
--
--  QUÉ MIRAR: cada bloque imprime "OK" o "FALLO". Al final hay un resumen.
--  Si algo imprime FALLO, esa pieza del diseño no está probada.
--
--  AVISO HONESTO: este guion se escribió SIN poder ejecutarlo (el entorno
--  donde se redactó no tiene PostgreSQL). Si algún bloque falla por un error
--  de sintaxis, es un error de este archivo, no del diseño: avísalo y se
--  corrige. La lógica de cada prueba sí es la correcta.
-- =============================================================================

\set ON_ERROR_STOP on
\pset pager off

\echo ''
\echo '=============================================================='
\echo ' 0 · VERSION DEL MOTOR'
\echo '=============================================================='
SELECT version();

DROP SCHEMA IF EXISTS frubber_test CASCADE;
CREATE SCHEMA frubber_test;
SET search_path TO frubber_test, public;

-- Contador de resultados
CREATE TABLE frubber_test.resultado (
  n        int primary key,
  pieza    text not null,
  estado   text not null default 'pendiente'
);

INSERT INTO frubber_test.resultado (n, pieza) VALUES
  (1, 'Indice unico parcial: una caja abierta por puesto'),
  (2, 'Numeracion de tickets sin huecos'),
  (3, 'Cobro idempotente: sin ventas duplicadas'),
  (4, 'Inmutabilidad de la venta por disparador'),
  (5, 'Roles y PIN por funcion SECURITY DEFINER');


-- =============================================================================
\echo ''
\echo '=============================================================='
\echo ' 1 · UNA CAJA ABIERTA POR PUESTO (indice unico parcial)'
\echo '=============================================================='

CREATE TABLE terminal (
  id           bigserial primary key,
  name         text not null,
  machine_code text not null unique,
  is_active    boolean not null default true,
  registered_at timestamptz not null default now()
);

CREATE TABLE cash_session (
  id          bigserial primary key,
  terminal_id bigint not null references terminal(id),
  opened_at   timestamptz not null default now(),
  closed_at   timestamptz null
);

-- La pieza clave: indice UNICO PARCIAL. Solo mira las filas con closed_at NULL.
CREATE UNIQUE INDEX ux_cash_session_abierta_por_puesto
  ON cash_session (terminal_id)
  WHERE closed_at IS NULL;

INSERT INTO terminal (name, machine_code) VALUES
  ('Caja 1', 'A1B2C3D4E5F60718'),
  ('Caja 2', '1122334455667788');

DO $$
DECLARE ok int := 0;
BEGIN
  -- 1a: abrir caja en Caja 1 -> debe permitir
  BEGIN
    INSERT INTO cash_session (terminal_id) VALUES (1);
    ok := ok + 1;
  EXCEPTION WHEN OTHERS THEN
    RAISE NOTICE 'FALLO 1a: no permitio abrir la primera caja (%)', SQLERRM;
  END;

  -- 1b: abrir caja en Caja 2 -> debe permitir (es otro puesto)
  BEGIN
    INSERT INTO cash_session (terminal_id) VALUES (2);
    ok := ok + 1;
  EXCEPTION WHEN OTHERS THEN
    RAISE NOTICE 'FALLO 1b: no permitio abrir caja en el segundo puesto (%)', SQLERRM;
  END;

  -- 1c: abrir una SEGUNDA caja en Caja 1 -> debe FALLAR
  BEGIN
    INSERT INTO cash_session (terminal_id) VALUES (1);
    RAISE NOTICE 'FALLO 1c: permitio dos cajas abiertas en el mismo puesto';
  EXCEPTION WHEN unique_violation THEN
    ok := ok + 1;
  END;

  -- 1d: cerrar la caja de Caja 1 y abrir otra -> debe permitir
  UPDATE cash_session SET closed_at = now() WHERE terminal_id = 1 AND closed_at IS NULL;
  BEGIN
    INSERT INTO cash_session (terminal_id) VALUES (1);
    ok := ok + 1;
  EXCEPTION WHEN OTHERS THEN
    RAISE NOTICE 'FALLO 1d: no permitio reabrir tras cerrar (%)', SQLERRM;
  END;

  IF ok = 4 THEN
    RAISE NOTICE 'OK 1: el indice parcial hace exactamente lo necesario (4/4)';
    UPDATE resultado SET estado = 'OK' WHERE n = 1;
  ELSE
    RAISE NOTICE 'FALLO 1: pasaron % de 4 comprobaciones', ok;
    UPDATE resultado SET estado = 'FALLO' WHERE n = 1;
  END IF;
END $$;


-- =============================================================================
\echo ''
\echo '=============================================================='
\echo ' 2 · NUMERACION DE TICKETS SIN HUECOS'
\echo '=============================================================='

CREATE TABLE ticket_counter (
  scope       text primary key,
  last_number bigint not null default 0
);
INSERT INTO ticket_counter (scope, last_number) VALUES ('venta', 0);

-- El numero se consume DENTRO de la transaccion de la venta. Si la venta
-- falla, la transaccion se deshace y el numero se libera: por eso no hay huecos.
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

DO $$
DECLARE
  i int; n bigint; anterior bigint := 0; saltos int := 0; devueltos int := 0;
BEGIN
  -- 2a: 100 numeros seguidos, sin saltos
  FOR i IN 1..100 LOOP
    n := next_ticket();
    IF n <> anterior + 1 THEN saltos := saltos + 1; END IF;
    anterior := n;
  END LOOP;

  IF saltos = 0 THEN
    RAISE NOTICE 'OK 2a: 100 tickets consecutivos, 0 saltos';
  ELSE
    RAISE NOTICE 'FALLO 2a: % saltos en 100 tickets', saltos;
  END IF;

  -- 2b: una venta que falla no deja hueco
  BEGIN
    PERFORM next_ticket();               -- se consume...
    RAISE EXCEPTION 'fallo simulado de la venta';
  EXCEPTION WHEN OTHERS THEN
    devueltos := 1;                      -- ...y al deshacerse, se libera
  END;

  n := next_ticket();
  IF n = anterior + 1 THEN
    RAISE NOTICE 'OK 2b: el numero de la venta fallida se libero (siguiente = %)', n;
  ELSE
    RAISE NOTICE 'FALLO 2b: quedo un hueco. Esperaba %, obtuve %', anterior + 1, n;
  END IF;

  -- Corregido (M2-27): la variable PL/pgSQL "n" hace ambigua la columna "n" de
  -- "resultado". Se califica la columna como "resultado.n" para que PostgreSQL
  -- no la confunda con la variable del contador de tickets.
  IF saltos = 0 AND n = anterior + 1 THEN
    UPDATE resultado SET estado = 'OK' WHERE resultado.n = 2;
  ELSE
    UPDATE resultado SET estado = 'FALLO' WHERE resultado.n = 2;
  END IF;
END $$;

\echo '  (la prueba de concurrencia real se hace con DOS ventanas de psql:'
\echo '   ver instrucciones al final de este archivo)'


-- =============================================================================
\echo ''
\echo '=============================================================='
\echo ' 3 · COBRO IDEMPOTENTE (corte de red en pleno cobro)'
\echo '=============================================================='

CREATE TABLE sale (
  id                bigserial primary key,
  ticket_number     bigint not null,
  client_request_id uuid not null unique,      -- <- la pieza que evita duplicados
  total             numeric(12,2) not null check (total >= 0),
  created_at        timestamptz not null default now()
);

-- Cobrar: si el mismo intento llega dos veces, devuelve la venta YA registrada.
CREATE FUNCTION cobrar(p_request uuid, p_total numeric) RETURNS TABLE (sale_id bigint, era_nueva boolean)
LANGUAGE plpgsql AS $$
DECLARE v_id bigint;
BEGIN
  -- ¿ya existe este intento?
  SELECT id INTO v_id FROM sale WHERE client_request_id = p_request;
  IF v_id IS NOT NULL THEN
    RETURN QUERY SELECT v_id, false;
    RETURN;
  END IF;

  INSERT INTO sale (ticket_number, client_request_id, total)
  VALUES (next_ticket(), p_request, p_total)
  RETURNING id INTO v_id;

  RETURN QUERY SELECT v_id, true;
END $$;

DO $$
DECLARE
  r1 record; r2 record; total_ventas int; n_tras_fallo bigint; n_antes bigint;
BEGIN
  -- 3a: primer intento crea la venta
  SELECT * INTO r1 FROM cobrar('11111111-1111-1111-1111-111111111111', 25000);
  -- 3b: el mismo intento repetido NO duplica
  SELECT * INTO r2 FROM cobrar('11111111-1111-1111-1111-111111111111', 25000);

  SELECT count(*) INTO total_ventas FROM sale;

  IF r1.era_nueva AND NOT r2.era_nueva AND r1.sale_id = r2.sale_id AND total_ventas = 1 THEN
    RAISE NOTICE 'OK 3a/3b: el reintento devolvio la misma venta (id=%), sin duplicar', r1.sale_id;
    UPDATE resultado SET estado = 'OK' WHERE n = 3;
  ELSE
    RAISE NOTICE 'FALLO 3: nueva=%/% id=%/% ventas=%', r1.era_nueva, r2.era_nueva, r1.sale_id, r2.sale_id, total_ventas;
    UPDATE resultado SET estado = 'FALLO' WHERE n = 3;
  END IF;
END $$;


-- =============================================================================
\echo ''
\echo '=============================================================='
\echo ' 4 · INMUTABILIDAD DE LA VENTA (disparador)'
\echo '=============================================================='

CREATE FUNCTION sale_es_inmutable() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
  RAISE EXCEPTION 'Las ventas son inmutables: no se permite % sobre sale', TG_OP
    USING ERRCODE = 'restrict_violation';
END $$;

CREATE TRIGGER trg_sale_inmutable
  BEFORE UPDATE OR DELETE ON sale
  FOR EACH ROW EXECUTE FUNCTION sale_es_inmutable();

DO $$
DECLARE ok int := 0;
BEGIN
  BEGIN
    UPDATE sale SET total = 1 WHERE id = 1;
    RAISE NOTICE 'FALLO 4a: permitio modificar una venta';
  EXCEPTION WHEN restrict_violation THEN ok := ok + 1;
  END;

  BEGIN
    DELETE FROM sale WHERE id = 1;
    RAISE NOTICE 'FALLO 4b: permitio borrar una venta';
  EXCEPTION WHEN restrict_violation THEN ok := ok + 1;
  END;

  IF ok = 2 THEN
    RAISE NOTICE 'OK 4: la venta no se puede modificar ni borrar (2/2)';
    UPDATE resultado SET estado = 'OK' WHERE n = 4;
  ELSE
    RAISE NOTICE 'FALLO 4: solo % de 2', ok;
    UPDATE resultado SET estado = 'FALLO' WHERE n = 4;
  END IF;
END $$;


-- =============================================================================
\echo ''
\echo '=============================================================='
\echo ' 5 · ROLES Y PIN SIN EXPONER EL HASH'
\echo '=============================================================='

-- Rol de un puesto (NOLOGIN aqui solo para la prueba; en produccion es LOGIN)
DO $$ BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'frubber_puesto_test') THEN
    CREATE ROLE frubber_puesto_test NOLOGIN;
  END IF;
END $$;

CREATE TABLE app_user (
  id       bigserial primary key,
  username text not null unique,
  pin_hash text not null          -- en la app real: PBKDF2 con sal, no un sha256 simple
);

-- Solo para la prueba: '1234' en sha256. NO usar asi en produccion.
INSERT INTO app_user (username, pin_hash)
VALUES ('ana', encode(sha256(convert_to('1234', 'UTF8')), 'hex'));

-- La funcion corre con los permisos de su dueno (SECURITY DEFINER),
-- asi el puesto NUNCA necesita leer la tabla de usuarios.
CREATE FUNCTION verificar_pin(p_user text, p_pin text) RETURNS boolean
LANGUAGE sql
SECURITY DEFINER
SET search_path = frubber_test, pg_temp
AS $$
  SELECT EXISTS (
    SELECT 1 FROM app_user
     WHERE username = p_user
       AND pin_hash = encode(sha256(convert_to(p_pin, 'UTF8')), 'hex')
  );
$$;

GRANT USAGE ON SCHEMA frubber_test TO frubber_puesto_test;
REVOKE ALL ON app_user FROM frubber_puesto_test;
GRANT EXECUTE ON FUNCTION verificar_pin(text, text) TO frubber_puesto_test;
GRANT SELECT ON sale TO frubber_puesto_test;
REVOKE INSERT, UPDATE, DELETE ON sale FROM frubber_puesto_test;

DO $$
DECLARE
  puede_leer_hash boolean;
  puede_insertar_venta boolean;
  puede_verificar boolean;
  r_ok boolean; r_mal boolean;
  es_definer boolean;
BEGIN
  puede_leer_hash       := has_table_privilege('frubber_puesto_test', 'frubber_test.app_user', 'SELECT');
  puede_insertar_venta  := has_table_privilege('frubber_puesto_test', 'frubber_test.sale', 'INSERT');
  puede_verificar       := has_function_privilege('frubber_puesto_test', 'frubber_test.verificar_pin(text,text)', 'EXECUTE');
  es_definer            := (SELECT prosecdef FROM pg_proc WHERE proname = 'verificar_pin' AND pronamespace = 'frubber_test'::regnamespace);

  r_ok  := verificar_pin('ana', '1234');
  r_mal := verificar_pin('ana', '0000');

  IF NOT puede_leer_hash THEN
    RAISE NOTICE 'OK 5a: el puesto NO puede leer el hash del PIN';
  ELSE
    RAISE NOTICE 'FALLO 5a: el puesto SI puede leer el hash del PIN';
  END IF;

  IF r_ok AND NOT r_mal AND puede_verificar THEN
    RAISE NOTICE 'OK 5b: verificar_pin() valida el PIN correcto y rechaza el incorrecto';
  ELSE
    RAISE NOTICE 'FALLO 5b: ok=% mal=% execute=%', r_ok, r_mal, puede_verificar;
  END IF;

  IF NOT puede_insertar_venta THEN
    RAISE NOTICE 'OK 5c: el puesto NO puede insertar ventas por fuera del programa';
  ELSE
    RAISE NOTICE 'FALLO 5c: el puesto SI puede insertar ventas directamente';
  END IF;

  IF es_definer AND NOT puede_leer_hash AND r_ok AND NOT r_mal AND NOT puede_insertar_venta THEN
    UPDATE resultado SET estado = 'OK' WHERE n = 5;
  ELSE
    UPDATE resultado SET estado = 'FALLO' WHERE n = 5;
  END IF;
END $$;


-- =============================================================================
\echo ''
\echo '=============================================================='
\echo ' RESUMEN'
\echo '=============================================================='
SELECT n, estado, pieza FROM frubber_test.resultado ORDER BY n;

\echo ''
\echo ' Si las 5 piezas salieron OK, el diseno esta listo para el esquema real.'
\echo ' Si alguna salio FALLO, esa pieza NO debe darse por buena.'
\echo ''

-- -----------------------------------------------------------------------------
-- LIMPIEZA (dejar comentado si quieres inspeccionar despues)
-- -----------------------------------------------------------------------------
-- DROP SCHEMA IF EXISTS frubber_test CASCADE;
-- DROP ROLE IF EXISTS frubber_puesto_test;

-- =============================================================================
-- PRUEBA DE CONCURRENCIA REAL (manual, 2 ventanas de psql)
-- =============================================================================
--  Ventana A:   psql -U postgres
--               SET search_path TO frubber_test, public;
--               BEGIN;
--               SELECT next_ticket();      -- se queda con el bloqueo
--               -- no hagas COMMIT todavia
--
--  Ventana B:   psql -U postgres
--               SET search_path TO frubber_test, public;
--               BEGIN;
--               SELECT next_ticket();      -- debe ESPERAR a que A termine
--
--  Lo que debe pasar:
--   * B se queda esperando mientras A tenga la transaccion abierta.
--   * A hace COMMIT -> B recibe el numero SIGUIENTE, sin repetir y sin salto.
--   * Ninguna de las dos puede obtener el mismo numero.
--
--  Para la prueba de las 500 ventas concurrentes (M2-29), pgbench o un
--  script con 2+ procesos es la forma correcta de medirlo.
-- =============================================================================
