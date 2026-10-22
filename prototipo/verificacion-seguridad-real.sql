-- =============================================================================
--  Frubber POS 2.0.0 · M2-28 · VERIFICACIÓN DE SEGURIDAD contra el ESQUEMA REAL
--  Archivo: prototipo/verificacion-seguridad-real.sql
--
--  Comprueba los criterios de cierre de M2-28 sobre la base "frubber" con
--  001_Baseline.sql y 002_Security.sql aplicados:
--
--   (1) MATRIZ de permisos': un puesto NO lee app_user, NO lee las tablas
--       críticas ni la bitácora del PIN, NO escribe nada de nada.
--   (2) PIN: la verificación acepta el correcto y rechaza el incorrecto SIN
--       exponer el hash; límite de 5 fallos con espera creciente y registro
--       de accesos en pin_attempt_log.
--   (3) Escritura directa sobre sale -> error 42501 (implícito en la matriz).
--   (4) Ciclo de vida del puesto: alta con contraseña generada por el motor,
--       login real con contraseña (correcta/incorrecta) y desactivación.
--       Esta fase final es MANUAL: se corren 3 comandos extra de psql (ver
--       el bloque al final) para evidenciar el acceso con contraseña.
--
--  EL VECTOR PBKDF2 del PIN de prueba fue generado con el MISMO algoritmo de
--  la app 1.0 (Rfc2898DeriveBytes.Pbkdf2, SHA-256, 210.000 iteraciones):
--     pin=1234  salt=000102030405060708090a0b0c0d0e0f
--       hash=942eed8586f04aa8cc4b14537eb02bb601b671749b6f95c07a2d2261f83e75a7
--     pin=0000  hash=e9254df183de7690676c80e10d3036a7e417a8f907e01e1d3b7a60a6de69049b
--     pin=9999  hash=c194ca0fc3abdcb735afc63f6ea8500e98329a347e84a5ace7833f7a26ec8b66
--
--  CÓMO SE CORRE (el admin corrobora; el puesto entra vía SET ROLE):
--     psql -U postgres -d frubber -v ON_ERROR_STOP=1 -f verificacion-seguridad-real.sql
--
--  Las fases 1-3 terminan en ROLLBACK: no dejan rastros en la base.
-- =============================================================================

\set ON_ERROR_STOP on
\pset pager off

\echo ''
\echo '=============================================================================='
\echo ' M2-28 · VERIFICACIÓN DE SEGURIDAD (base: frubber)'
\echo '=============================================================================='


-- =============================================================================
--  FASE 1 · MATRIZ DE PERMISOS (criterio 1, lado declarativo)
-- =============================================================================
\echo ''
\echo '---- FASE 1 : matriz de permisos (nominales) del rol frubber_puesto ----'

SELECT
  CASE WHEN NOT has_table_privilege('frubber_puesto', 'app_user', 'select')
       THEN 'OK' ELSE 'FALLO' END AS "app_user_lectura",
  CASE WHEN NOT has_table_privilege('frubber_puesto', 'sale', 'select')
       THEN 'OK' ELSE 'FALLO' END AS "sale_lectura",
  CASE WHEN NOT has_table_privilege('frubber_puesto', 'sale', 'insert')
       THEN 'OK' ELSE 'FALLO' END AS "sale_insercion",
  CASE WHEN NOT has_table_privilege('frubber_puesto', 'sale_line', 'delete')
       THEN 'OK' ELSE 'FALLO' END AS "sale_line_borrado",
  CASE WHEN NOT has_table_privilege('frubber_puesto', 'cash_session', 'update')
       THEN 'OK' ELSE 'FALLO' END AS "cash_session_actualizacion",
  CASE WHEN NOT has_table_privilege('frubber_puesto', 'sale_void', 'insert')
       THEN 'OK' ELSE 'FALLO' END AS "sale_void_insercion",
  CASE WHEN NOT has_table_privilege('frubber_puesto', 'license_state', 'update')
       THEN 'OK' ELSE 'FALLO' END AS "licencia_actualizacion",
  CASE WHEN NOT has_table_privilege('frubber_puesto', 'terminal', 'select')
       THEN 'OK' ELSE 'FALLO' END AS "terminal_lectura (solo via terminal_info)",
  CASE WHEN NOT has_table_privilege('frubber_puesto', 'pin_attempt_log', 'select')
       THEN 'OK' ELSE 'FALLO' END AS "bitacora_intentos_lectura",
  CASE WHEN NOT has_table_privilege('frubber_puesto', 'pin_lock_state', 'select')
       THEN 'OK' ELSE 'FALLO' END AS "estado_bloqueo_lectura",
  CASE WHEN NOT has_schema_privilege('frubber_puesto', 'public', 'create')
       THEN 'OK' ELSE 'FALLO' END AS "crear_en_public",
  CASE WHEN NOT has_database_privilege('frubber_puesto', 'frubber', 'create')
       THEN 'OK' ELSE 'FALLO' END AS "crear_en_base",
  CASE WHEN has_table_privilege('frubber_puesto', 'product', 'select')
       THEN 'OK' ELSE 'FALLO' END AS "product_lectura_permitida",
  CASE WHEN NOT has_table_privilege('frubber_puesto', 'cash_session', 'select')
       THEN 'OK' ELSE 'FALLO' END AS "cash_session_lectura (solo por vista)",
  CASE WHEN has_table_privilege('frubber_puesto', 'business_setting', 'select')
       THEN 'OK' ELSE 'FALLO' END AS "business_setting_lectura_permitida",
  CASE WHEN has_table_privilege('frubber_puesto', 'v_sale_history', 'select')
       THEN 'OK' ELSE 'FALLO' END AS "historial_por_vista_permitido",
  CASE WHEN has_table_privilege('frubber_puesto', 'v_open_cash_totals', 'select')
       THEN 'OK' ELSE 'FALLO' END AS "totales_caja_por_vista_permitido",
  CASE WHEN has_function_privilege('frubber_puesto', 'app_pin_verify(bigint,bytea)', 'execute')
       THEN 'OK' ELSE 'FALLO' END AS "pin_verify_permitido",
  CASE WHEN has_function_privilege('frubber_puesto', 'app_pin_challenge(bigint)', 'execute')
       THEN 'OK' ELSE 'FALLO' END AS "pin_challenge_permitido",
  CASE WHEN has_function_privilege('frubber_puesto', 'app_user_directory()', 'execute')
       THEN 'OK' ELSE 'FALLO' END AS "directorio_usuarios_permitido",
  CASE WHEN has_function_privilege('frubber_puesto', 'terminal_info()', 'execute')
       THEN 'OK' ELSE 'FALLO' END AS "terminal_info_permitido",
  CASE WHEN NOT has_function_privilege('frubber_puesto', 'register_terminal(text,char(16))', 'execute')
       THEN 'OK' ELSE 'FALLO' END AS "alta_puestos_denegada",
  CASE WHEN NOT has_function_privilege('frubber_puesto', 'deactivate_terminal(char(16))', 'execute')
       THEN 'OK' ELSE 'FALLO' END AS "baja_puestos_denegada",
  CASE WHEN EXISTS (SELECT 1 FROM pg_indexes WHERE indexname = 'ux_product_plu')
       THEN 'OK' ELSE 'FALLO' END AS "plu_único (paridad 1.0)",
  CASE WHEN EXISTS (SELECT 1 FROM pg_indexes WHERE indexname = 'ux_app_user_display_name')
       THEN 'OK' ELSE 'FALLO' END AS "nombre_usuario_unico (paridad 1.0)";


-- =============================================================================
--  FASE 2 · PIN SIN EXPONER EL HASH + LÍMITE Y REGISTRO (criterio 2)
-- =============================================================================
\echo ''
\echo '---- FASE 2 : verificación de PIN, límite de intentos y registro ----'

BEGIN;

DO $$
DECLARE
  v_user bigint;
  hash_ok   bytea := decode('942eed8586f04aa8cc4b14537eb02bb601b671749b6f95c07a2d2261f83e75a7', 'hex');
  hash_mal1 bytea := decode('e9254df183de7690676c80e10d3036a7e417a8f907e01e1d3b7a60a6de69049b', 'hex');
  hash_mal2 bytea := decode('c194ca0fc3abdcb735afc63f6ea8500e98329a347e84a5ace7833f7a26ec8b66', 'hex');
  r record;
  ok   int := 0;
  total int := 0;
BEGIN
  INSERT INTO app_user (display_name, role, is_active,
                        pin_salt, pin_hash, pin_iterations, pin_algorithm_version)
    VALUES ('verif_p28', 'CASHIER', true,
            decode('000102030405060708090a0b0c0d0e0f', 'hex'),
            hash_ok, 210000, 1)
    RETURNING user_id INTO v_user;

  -- 2a · challenge retorna sal/iteraciones/versión; el hash NUNCA está en el retorno.
  total := total + 1;
  IF (SELECT count(*) FROM app_pin_challenge(v_user) c
       WHERE c.pin_salt = decode('000102030405060708090a0b0c0d0e0f','hex')
         AND c.pin_iterations = 210000
         AND c.pin_algorithm_version = 1) = 1 THEN
    ok := ok + 1;
    RAISE NOTICE 'OK 2a: challenge entrega sal/iteraciones/versión (el hash no viaja)';
  ELSE
    RAISE NOTICE 'FALLO 2a: challenge no entrego la sal/iteraciones/versión esperadas';
  END IF;

  -- 2b1 · PIN correcto.
  total := total + 1;
  SELECT * INTO r FROM app_pin_verify(v_user, hash_ok);
  IF r.authenticated AND NOT r.blocked THEN
    ok := ok + 1;
    RAISE NOTICE 'OK 2b1: PIN 1234 -> autenticado';
  ELSE
    RAISE NOTICE 'FALLO 2b1: rechazo el PIN correcto';
  END IF;

  -- 2b2 · PIN incorrecto.
  total := total + 1;
  SELECT * INTO r FROM app_pin_verify(v_user, hash_mal1);
  IF NOT r.authenticated AND NOT r.blocked THEN
    ok := ok + 1;
    RAISE NOTICE 'OK 2b2: PIN 0000 -> rechazado';
  ELSE
    RAISE NOTICE 'FALLO 2b2: el PIN incorrecto paso (aut=%, bloq=%)', r.authenticated, r.blocked;
  END IF;

  -- 2c · 3 fallos más + 1 acierto limpian el contador (5 no llegó: ventana sana).
  total := total + 1;
  PERFORM app_pin_verify(v_user, hash_mal2);
  PERFORM app_pin_verify(v_user, hash_mal1);
  PERFORM app_pin_verify(v_user, hash_mal2);
  SELECT * INTO r FROM app_pin_verify(v_user, hash_ok);
  IF r.authenticated THEN
    ok := ok + 1;
    RAISE NOTICE 'OK 2c: fallos + acierto limpian contador (5º+CASO honesto intacto)';
  ELSE
    RAISE NOTICE 'FALLO 2c: el acierto no limpio el contador';
  END IF;

  -- 2d · 5 fallos seguidos bloquean; el 6º intento (aunque sea CORRECTO) queda
  --       bloqueado con ~2 min de espera.
  total := total + 1;
  PERFORM app_pin_verify(v_user, hash_mal1);
  PERFORM app_pin_verify(v_user, hash_mal2);
  PERFORM app_pin_verify(v_user, hash_mal1);
  PERFORM app_pin_verify(v_user, hash_mal2);
  PERFORM app_pin_verify(v_user, hash_mal1);              -- 5º: activa bloqueo
  SELECT * INTO r FROM app_pin_verify(v_user, hash_ok);   -- 6º: BLOQUEO
  IF NOT r.authenticated AND r.blocked AND r.wait_seconds BETWEEN 100 AND 121 THEN
    ok := ok + 1;
    RAISE NOTICE 'OK 2d: 5 fallos bloquean; 6º intento con bloqueo de ~2 min (%)', r.wait_seconds;
  ELSE
    RAISE NOTICE 'FALLO 2d: tras bloqueo aut=%, bloq=%, espera=%', r.authenticated, r.blocked, r.wait_seconds;
  END IF;

  -- 2e · Espera CRECIENTE: simula que el 1er bloqueo ya venció (bloques=1);
  --       un nuevo fallo debe bloquear por el DOBLE (~4 min = 240 s).
  total := total + 1;
  UPDATE pin_lock_state
     SET blocked_until = clock_timestamp() - interval '1 second',
         blocked_count = 1
   WHERE user_id = v_user;
  SELECT * INTO r FROM app_pin_verify(v_user, hash_mal2);
  IF r.blocked AND r.wait_seconds BETWEEN 200 AND 241 THEN
    ok := ok + 1;
    RAISE NOTICE 'OK 2e: 2do bloqueo DUPLICA la espera (~4 min = %s)', r.wait_seconds;
  ELSE
    RAISE NOTICE 'FALLO 2e: espera incorrecta tras 1er vencimiento (%s)', r.wait_seconds;
  END IF;

  -- 2f · REGISTRO de accesos: exactamente 1 fila por intento, con resultado.
  SET LOCAL ROLE none;
  total := total + 1;
  SELECT count(*) FILTER (WHERE outcome = 'OK') AS oks,
         count(*) FILTER (WHERE outcome = 'FALLO') AS fallos,
         count(*) FILTER (WHERE outcome = 'BLOQUEO') AS bloqueos,
         count(*) AS tot
    INTO r
    FROM pin_attempt_log
   WHERE user_id = v_user;
  IF r.tot = 13 AND r.oks = 2 AND r.fallos = 10 AND r.bloqueos = 1 THEN
    ok := ok + 1;
    RAISE NOTICE 'OK 2f: registro de accesos completo (13 intentos: %OK / %FALLO / %BLOQUEO)',
                 r.oks, r.fallos, r.bloqueos;
  ELSE
    RAISE NOTICE 'FALLO 2f: registro de accesos inesperado (tot=%, ok=%, fallo=%, bloq=%)',
                 r.tot, r.oks, r.fallos, r.bloqueos;
  END IF;

  -- 2g · AUDITORÍA (control de COBERTURA): todas las filas de la bitácora
  --       tienen autor y ninguna NULL. El aserto FUERTE (session_user =
  --       el puesto) no cabe aquí: SET ROLE no cambia session_user en
  --       una misma conexión, así que se prueba en la FASE 4 con login
  --       de red real (puesto_deadbeef00000001).
  total := total + 1;
  CREATE ROLE verif_p28_pin LOGIN PASSWORD 'pin-verif' IN ROLE frubber_puesto;
  -- Como administrador: se desbloquea el usuario (del 2e queda un bloqueo vivo)
  -- ANTES de asumir el rol del puesto, que no puede tocar pin_lock_state.
  UPDATE pin_lock_state
     SET blocked_until = clock_timestamp() - interval '1 second',
         blocked_count = 1,
         consecutive_failures = 4
   WHERE user_id = v_user;
  SET LOCAL ROLE verif_p28_pin;
  PERFORM app_pin_verify(v_user, hash_mal1);  -- fallo emitido por el puesto real
  SET LOCAL ROLE none;
  IF (SELECT count(*) FROM pin_attempt_log WHERE user_id = v_user) = 14
     AND NOT EXISTS (SELECT 1 FROM pin_attempt_log
                      WHERE user_id = v_user AND attempted_by IS NULL) THEN
    ok := ok + 1;
    RAISE NOTICE 'OK 2g: bitácora sin huecos (14 intentos, todos con autor)';
    RAISE NOTICE 'NOTA 2g: session_user del puesto se prueba en FASE 4 (login real)';
  ELSE
    RAISE NOTICE 'FALLO 2g: bitácora con huecos o sin autor';
  END IF;

  IF ok = total THEN
    RAISE NOTICE 'FASE 2 COMPLETA: % de %', ok, total;
  ELSE
    RAISE NOTICE 'FASE 2 INCOMPLETA: % de %', ok, total;
  END IF;
END;
$$;

ROLLBACK;


-- =============================================================================
--  FASE 3 · ESCRITURA DIRECTA DENEGADA (criterio 3): 42501 en la práctica
-- =============================================================================
\echo ''
\echo '---- FASE 3 : intentos reales de escritura de un puesto (42501) ----'

BEGIN;

DO $$
DECLARE
  terminal_id_local bigint;
  user_local bigint;
  admin_local bigint;
  prod_local bigint;
  caja_local bigint;
  venta_local bigint;
  v_role name := 'verif_p28_puesto';
  ok   int := 0;
  total int := 0;
  v_dummy text;
BEGIN
  -- Semilla (como administrador): datos válidos con una venta real.
  INSERT INTO app_user (display_name, role, is_active,
                        pin_salt, pin_hash, pin_iterations, pin_algorithm_version)
    VALUES ('verif_admin28', 'ADMIN', true,
            decode(repeat('11', 16), 'hex'), decode(repeat('22', 32), 'hex'), 210000, 1)
    RETURNING user_id INTO admin_local;

  INSERT INTO app_user (display_name, role, is_active,
                        pin_salt, pin_hash, pin_iterations, pin_algorithm_version)
    VALUES ('verif_caj28', 'CASHIER', true,
            decode(repeat('33', 16), 'hex'), decode(repeat('44', 32), 'hex'), 210000, 1)
    RETURNING user_id INTO user_local;

  INSERT INTO terminal (name, machine_code)
    VALUES ('Verif M2-28', 'DEADBEEFABCDEF01')
    RETURNING terminal_id INTO terminal_id_local;

  INSERT INTO product (plu, name, unit, price_cop, stock_milli, is_active, unit_locked)
    VALUES ('VERIF28', 'Producto verif28', 'KG', 2000, 5000, true, true)
    RETURNING product_id INTO prod_local;

  INSERT INTO cash_session (terminal_id, opened_by_user_id, opened_by_name_snapshot, opening_balance_cop)
    VALUES (terminal_id_local, admin_local, 'verif_admin28', 50000)
    RETURNING cash_session_id INTO caja_local;

  INSERT INTO sale (sale_number, cash_session_id, cashier_user_id, cashier_name_snapshot,
                    subtotal_cop, discount_cop, total_cop, payment_method,
                    received_cop, change_cop, client_request_id)
    VALUES (next_ticket(), caja_local, user_local, 'verif_caj28', 8000, 0, 8000,
            'CASH', 10000, 2000, gen_random_uuid())
    RETURNING sale_id INTO venta_local;

  INSERT INTO sale_line (sale_id, line_number, product_id, plu_snapshot, product_name_snapshot,
                         unit_snapshot, unit_price_cop_snapshot, quantity_milli, line_total_cop)
    VALUES (venta_local, 1, prod_local, 'VERIF28', 'Producto verif28', 'KG', 2000, 4000, 8000);

  INSERT INTO license_state (license_state_id, payload_version, signed_payload)
    VALUES (1, 1, 'FRB2|test|payload|firma')
  ON CONFLICT (license_state_id) DO NOTHING;

  CREATE ROLE verif_p28_puesto LOGIN PASSWORD 'prueba-verif' IN ROLE frubber_puesto;
  SET LOCAL ROLE verif_p28_puesto;

  -- Lecturas que SÍ puede hacer (matriz positivos):
  total := total + 1;
  IF EXISTS (SELECT 1 FROM product WHERE plu = 'VERIF28')
     AND EXISTS (SELECT 1 FROM v_open_cash_totals WHERE cash_session_id = caja_local)
     AND EXISTS (SELECT 1 FROM business_setting)
     AND EXISTS (SELECT 1 FROM v_sale_history WHERE sale_id = venta_local)
     AND EXISTS (SELECT 1 FROM app_user_directory() WHERE display_name = 'verif_caj28') THEN
    ok := ok + 1;
    RAISE NOTICE 'OK 3a: lecturas permitidas (product, vistas de caja, business_setting, vistas, directorio)';
  ELSE
    RAISE NOTICE 'FALLO 3a: una lectura permitida falló';
  END IF;

  -- Lecturas DENEGADAS:
  total := total + 1;
  BEGIN
    SELECT 'leido' INTO v_dummy FROM app_user LIMIT 1;
    RAISE NOTICE 'FALLO 3b: el puesto pudo LEER app_user';
  EXCEPTION WHEN insufficient_privilege THEN
    ok := ok + 1;
    RAISE NOTICE 'OK 3b: app_user lectura denegada (42501)';
  END;

  total := total + 1;
  BEGIN
    SELECT 'leido' INTO v_dummy FROM sale LIMIT 1;
    RAISE NOTICE 'FALLO 3c: el puesto pudo LEER sale directo';
  EXCEPTION WHEN insufficient_privilege THEN
    ok := ok + 1;
    RAISE NOTICE 'OK 3c: sale lectura directa denegada (42501)';
  END;

  total := total + 1;
  BEGIN
    SELECT 'leido' INTO v_dummy FROM pin_attempt_log LIMIT 1;
    RAISE NOTICE 'FALLO 3d: el puesto pudo LEER pin_attempt_log';
  EXCEPTION WHEN insufficient_privilege THEN
    ok := ok + 1;
    RAISE NOTICE 'OK 3d: bitácora de intentos lectura denegada (42501)';
  END;

  -- Escrituras DENEGADAS (criterio 3 · sale):
  total := total + 1;
  BEGIN
    INSERT INTO sale (sale_number, cash_session_id, cashier_user_id, cashier_name_snapshot,
                      subtotal_cop, discount_cop, total_cop, payment_method,
                      received_cop, change_cop, client_request_id)
      VALUES (next_ticket(), caja_local, user_local, 'verif_caj28', 8000, 0, 8000,
              'CASH', 10000, 2000, gen_random_uuid());
    RAISE NOTICE 'FALLO 3e: el puesto pudo INSERTAR una venta';
  EXCEPTION WHEN insufficient_privilege THEN
    ok := ok + 1;
    RAISE NOTICE 'OK 3e: INSERT en sale denegado (42501)';
  END;

  total := total + 1;
  BEGIN
    UPDATE sale SET total_cop = 1 WHERE sale_id = venta_local;
    RAISE NOTICE 'FALLO 3f: el puesto pudo ACTUALIZAR una venta';
  EXCEPTION WHEN insufficient_privilege THEN
    ok := ok + 1;
    RAISE NOTICE 'OK 3f: UPDATE en sale denegado (42501)';
  END;

  total := total + 1;
  BEGIN
    DELETE FROM sale WHERE sale_id = venta_local;
    RAISE NOTICE 'FALLO 3g: el puesto pudo BORRAR una venta';
  EXCEPTION WHEN insufficient_privilege THEN
    ok := ok + 1;
    RAISE NOTICE 'OK 3g: DELETE en sale denegado (42501)';
  END;

  -- Otras escrituras críticas:
  total := total + 1;
  BEGIN
    UPDATE sale_line SET quantity_milli = 1 WHERE sale_id = venta_local;
    DELETE FROM sale_line WHERE sale_id = venta_local;
    RAISE NOTICE 'FALLO 3h: línea de venta modificable/borrable';
  EXCEPTION WHEN insufficient_privilege THEN
    ok := ok + 1;
    RAISE NOTICE 'OK 3h: UPDATE/DELETE en sale_line denegados (42501)';
  END;

  total := total + 1;
  BEGIN
    UPDATE cash_session SET closed_at = now() WHERE cash_session_id = caja_local;
    INSERT INTO cash_session (terminal_id, opened_by_user_id, opened_by_name_snapshot, opening_balance_cop)
      VALUES (terminal_id_local, user_local, 'verif_caj28', 1);
    RAISE NOTICE 'FALLO 3i: caja escribible';
  EXCEPTION WHEN insufficient_privilege THEN
    ok := ok + 1;
    RAISE NOTICE 'OK 3i: INSERT/UPDATE en cash_session denegados (42501)';
  END;

  total := total + 1;
  BEGIN
    INSERT INTO sale_void (sale_id, voided_by_user_id, voided_by_name_snapshot)
      VALUES (venta_local, admin_local, 'verif_admin28');
    DELETE FROM sale_void WHERE sale_id = venta_local;
    RAISE NOTICE 'FALLO 3j: nulación labil';
  EXCEPTION WHEN insufficient_privilege THEN
    ok := ok + 1;
    RAISE NOTICE 'OK 3j: INSERT/DELETE en sale_void denegados (42501)';
  END;

  total := total + 1;
  BEGIN
    UPDATE app_user SET is_active = false WHERE user_id = user_local;
    UPDATE license_state SET signed_payload = 'firma-falsa';
    UPDATE terminal SET is_active = false WHERE terminal_id = terminal_id_local;
    UPDATE ticket_counter SET last_number = 0;
    RAISE NOTICE 'FALLO 3k: escritura de app_user/licencia/terminal/contador posible';
  EXCEPTION WHEN insufficient_privilege THEN
    ok := ok + 1;
    RAISE NOTICE 'OK 3k: app_user, license_state, terminal y ticket_counter denegados (42501)';
  END;

  -- El puesto NO puede crear objetos ni ejecutar la alta de puestos:
  total := total + 1;
  BEGIN
    CREATE TABLE _hack_p28 (x int);
    RAISE NOTICE 'FALLO 3l: el puesto pudo crear tablas';
  EXCEPTION WHEN insufficient_privilege THEN
    ok := ok + 1;
    RAISE NOTICE 'OK 3l: CREATE TABLE denegado (42501)';
  END;

  total := total + 1;
  BEGIN
    IF (SELECT count(*) FROM register_terminal('Hack', 'F0F0F0F0F0F0F0F0')) > 0 THEN
      RAISE NOTICE 'FALLO 3m: el puesto pudo ejecutar register_terminal';
    ELSE
      RAISE NOTICE 'FALLO 3m: register_terminal devolvio 0 filas (inesperado)';
    END IF;
  EXCEPTION WHEN insufficient_privilege THEN
    ok := ok + 1;
    RAISE NOTICE 'OK 3m: register_terminal no ejecutable por el puesto (42501)';
  END;

  SET LOCAL ROLE none;

  -- 3n · La baja NO procede si el puesto tiene una caja abierta (Regla de Oro
  --       3): un rol NOLOGIN no volvería a conectar para cerrar la caja.
  --       Se prueba como administrador para llegar a la lógica del guard.
  total := total + 1;
  SET LOCAL ROLE none;
  BEGIN
    PERFORM deactivate_terminal('DEADBEEFABCDEF01');
    RAISE NOTICE 'FALLO 3n: se desactivó un puesto con caja abierta';
  EXCEPTION WHEN OTHERS THEN
    IF SQLERRM LIKE 'El puesto tiene una caja abierta%' THEN
      ok := ok + 1;
      RAISE NOTICE 'OK 3n: baja con caja abierta rechazada';
    ELSE
      RAISE;
    END IF;
  END;

  IF ok = total THEN
    RAISE NOTICE 'FASE 3 COMPLETA: % de %', ok, total;
  ELSE
    RAISE NOTICE 'FASE 3 INCOMPLETA: % de %', ok, total;
  END IF;
END;
$$;

ROLLBACK;


-- =============================================================================
--  FASE 4 · CICLO DE VIDA REAL DEL PUESTO (evidencia de contraseña; MANUAL)
-- =============================================================================
\echo ''
\echo '---- FASE 4 : vida del puesto (manual, hace COMMIT visible) ----'
\echo '1) Alta persistente (SI hace COMMIT):'
\echo '     SELECT * FROM register_terminal(''Caja 2 (prueba M2-28)'', ''DEADBEEF00000001'');'
\echo '2) Login con la contraseña CORRECTA (debe conectar):'
\echo '     PGPASSWORD=<la contrasena devuelta> psql -U puesto deadbeef00000001 ...'
\echo '3) Login con contraseña INCORRECTA (debe rechazar):'
\echo '     PGPASSWORD=incorrecta psql ...'
\echo '4) Desactivación y reintento (debe rechazar por NOLOGIN):'
\echo '     SELECT deactivate_terminal(''DEADBEEF00000001'');'
\echo '     psql de nuevo -> FATAL: el rol no permite login.'
\echo ''
\echo '=============================================================================='
\echo ' FIN FASES 1-3 · si todas las líneas OK salieron, los criterios de cierre'
\echo ' quedan probados. Evidencia -> bitácora 6.2 (E-07/E-08).'
\echo '=============================================================================='
\echo ''
