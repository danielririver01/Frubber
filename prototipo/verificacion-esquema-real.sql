-- =============================================================================
--  Frubber POS 2.0.0 · M2-27 · Pruebas explícitas contra el ESQUEMA REAL
--  Archivo: prototipo/verificacion-esquema-real.sql
--
--  Estas pruebas NO usan el banco de pruebas (frubber_test): corren sobre las
--  tablas REALES creadas por database/postgresql/001_Baseline.sql en la base
--  "frubber". Comprueban los dos criterios de cierre de M2-27 que el banco de
--  pruebas no cubre contra el esquema real:
--
--   (3) Dos cajas abiertas del MISMO puesto fallan; de puestos distintos pasan.
--   (4) Una venta no se puede modificar ni borrar, NI con cliente SQL externo
--       (el disparador actúa por encima de cualquier conexión, incluida la de
--       superusuario).
--
--  CÓMO SE CORRE:
--     psql -U postgres -d frubber -v ON_ERROR_STOP=1 -f verificacion-esquema-real.sql
--
--  Cada bloque captura los IDs reales con RETURNING (no asume numeración):
--  el esquema real ya trae el terminal "puesto desconocido (versión 1.0)".
--  Todo se hace dentro de DO ... y cada sección termina en ROLLBACK para no
--  ensuciar la base.
-- =============================================================================

\set ON_ERROR_STOP on
\pset pager off

\echo ''
\echo '=============================================================================='
\echo ' M2-27 · VERIFICACIÓN CONTRA EL ESQUEMA REAL (base: frubber)'
\echo '=============================================================================='

-- =============================================================================
--  (3) UNA CAJA ABIERTA POR PUESTO · índice único parcial sobre el esquema real
-- =============================================================================
\echo ''
\echo '--- (3) Una caja abierta por puesto (esquema real) ---'

BEGIN;

DO $$
DECLARE
  ok int := 0;
  id_user bigint;
  id_caja1 bigint;
  id_caja2 bigint;
  id_sesion1 bigint;
BEGIN
  -- Puestos reales de la prueba
  INSERT INTO terminal (name, machine_code) VALUES ('Caja 1', 'A1B2C3D4E5F60718')
    RETURNING terminal_id INTO id_caja1;
  INSERT INTO terminal (name, machine_code) VALUES ('Caja 2', '1122334455667788')
    RETURNING terminal_id INTO id_caja2;

  INSERT INTO app_user (display_name, role, is_active, pin_salt, pin_hash, pin_iterations, pin_algorithm_version)
    VALUES ('verif_admin', 'ADMIN', true,
            decode(repeat('11', 16), 'hex'), decode(repeat('22', 32), 'hex'), 1, 1)
    RETURNING user_id INTO id_user;

  -- 3a: abrir caja en Caja 1 -> debe permitir
  BEGIN
    INSERT INTO cash_session (terminal_id, opened_by_user_id, opened_by_name_snapshot, opening_balance_cop)
      VALUES (id_caja1, id_user, 'verif_admin', 50000)
      RETURNING cash_session_id INTO id_sesion1;
    ok := ok + 1;
  EXCEPTION WHEN OTHERS THEN
    RAISE NOTICE 'FALLO 3a: no permitio abrir la primera caja (%)', SQLERRM;
  END;

  -- 3b: abrir caja en Caja 2 -> debe permitir (es otro puesto)
  BEGIN
    INSERT INTO cash_session (terminal_id, opened_by_user_id, opened_by_name_snapshot, opening_balance_cop)
      VALUES (id_caja2, id_user, 'verif_admin', 30000);
    ok := ok + 1;
  EXCEPTION WHEN OTHERS THEN
    RAISE NOTICE 'FALLO 3b: no permitio abrir caja en el segundo puesto (%)', SQLERRM;
  END;

  -- 3c: abrir una SEGUNDA caja en Caja 1 -> debe FALLAR (indice parcial)
  BEGIN
    INSERT INTO cash_session (terminal_id, opened_by_user_id, opened_by_name_snapshot, opening_balance_cop)
      VALUES (id_caja1, id_user, 'verif_admin', 10000);
    RAISE NOTICE 'FALLO 3c: permitio dos cajas abiertas en el mismo puesto';
  EXCEPTION WHEN unique_violation THEN
    ok := ok + 1;
  END;

  -- 3d: cerrar la caja de Caja 1 y abrir otra -> debe permitir
  UPDATE cash_session
     SET closed_at = now(), closed_by_user_id = id_user,
         closed_by_name_snapshot = 'verif_admin', closed_cash_sales_cop = 0,
         closed_transfer_sales_cop = 0, closed_discount_cop = 0,
         closed_net_sales_cop = 0, closed_expected_cash_cop = 50000, closed_sale_count = 0
   WHERE cash_session_id = id_sesion1;
  BEGIN
    INSERT INTO cash_session (terminal_id, opened_by_user_id, opened_by_name_snapshot, opening_balance_cop)
      VALUES (id_caja1, id_user, 'verif_admin', 20000);
    ok := ok + 1;
  EXCEPTION WHEN OTHERS THEN
    RAISE NOTICE 'FALLO 3d: no permitio reabrir tras cerrar (%)', SQLERRM;
  END;

  IF ok = 4 THEN
    RAISE NOTICE 'OK (3): una caja abierta por puesto en el esquema real (4/4)';
  ELSE
    RAISE NOTICE 'FALLO (3): pasaron % de 4 comprobaciones', ok;
  END IF;
END $$;

ROLLBACK;

-- =============================================================================
--  (4) INMUTABILIDAD DE LA VENTA · disparadores sobre el esquema real,
--       probados con una conexión EXTERNA (superusuario) como manda el criterio
-- =============================================================================
\echo ''
\echo '--- (4) Venta inmutable, incluso con cliente SQL externo (esquema real) ---'

BEGIN;

DO $$
DECLARE
  id_terminal bigint;
  id_user bigint;
  id_producto bigint;
  id_sesion bigint;
  id_venta bigint;
  ok int := 0;
BEGIN
  INSERT INTO terminal (name, machine_code) VALUES ('Caja Inv', '9988776655443322')
    RETURNING terminal_id INTO id_terminal;

  INSERT INTO app_user (display_name, role, is_active, pin_salt, pin_hash, pin_iterations, pin_algorithm_version)
    VALUES ('verif_caj', 'CASHIER', true,
            decode(repeat('33', 16), 'hex'), decode(repeat('44', 32), 'hex'), 1, 1)
    RETURNING user_id INTO id_user;

  INSERT INTO product (plu, name, unit, price_cop, stock_milli, is_active, unit_locked)
    VALUES ('VERIF02', 'Producto inv', 'KG', 2000, 5000, true, true)
    RETURNING product_id INTO id_producto;

  INSERT INTO cash_session (terminal_id, opened_by_user_id, opened_by_name_snapshot, opening_balance_cop)
    VALUES (id_terminal, id_user, 'verif_caj', 10000)
    RETURNING cash_session_id INTO id_sesion;

  -- Registrar una venta directamente (simula al programa).
  INSERT INTO sale (sale_number, cash_session_id, cashier_user_id, cashier_name_snapshot,
                    subtotal_cop, discount_cop, total_cop, payment_method, received_cop, change_cop,
                    client_request_id)
    VALUES (next_ticket(), id_sesion, id_user, 'verif_caj', 8000, 0, 8000, 'CASH', 10000, 2000,
            gen_random_uuid())
    RETURNING sale_id INTO id_venta;

  INSERT INTO sale_line (sale_id, line_number, product_id, plu_snapshot, product_name_snapshot,
                         unit_snapshot, unit_price_cop_snapshot, quantity_milli, line_total_cop)
    VALUES (id_venta, 1, id_producto, 'VERIF02', 'Producto inv', 'KG', 2000, 4000, 8000);

  -- 4a: intentar MODIFICAR la venta desde una conexión externa -> debe fallar
  BEGIN
    UPDATE sale SET total_cop = 1 WHERE sale_id = id_venta;
    RAISE NOTICE 'FALLO 4a: permitio modificar una venta';
  EXCEPTION WHEN restrict_violation THEN ok := ok + 1; END;

  -- 4b: intentar BORRAR la venta -> debe fallar
  BEGIN
    DELETE FROM sale WHERE sale_id = id_venta;
    RAISE NOTICE 'FALLO 4b: permitio borrar una venta';
  EXCEPTION WHEN restrict_violation THEN ok := ok + 1; END;

  -- 4c: intentar BORRAR la linea -> debe fallar
  BEGIN
    DELETE FROM sale_line WHERE sale_id = id_venta;
    RAISE NOTICE 'FALLO 4c: permitio borrar la linea de la venta';
  EXCEPTION WHEN restrict_violation THEN ok := ok + 1; END;

  IF ok = 3 THEN
    RAISE NOTICE 'OK (4): la venta es inmutable incluso desde cliente externo (3/3)';
  ELSE
    RAISE NOTICE 'FALLO (4): pasaron % de 3 comprobaciones', ok;
  END IF;
END $$;

ROLLBACK;

\echo ''
\echo '=============================================================================='
\echo ' FIN · si (3) y (4) imprimieron OK, el esquema real cumple los criterios.'
\echo '=============================================================================='
\echo ''
