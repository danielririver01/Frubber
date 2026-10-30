-- =============================================================================
--  Frubber POS 2.0.0
--  Archivo: prototipo/seed-concurrencia-postgresql.sql
--  JHO-111 (M2-29) · Semilla de datos para la prueba de concurrencia del
--  contador de tickets (numeración sin huecos) sobre la base real "frubber".
--
--  Carga el escenario mínimo para que una transaccion de venta sea valida:
--    * 1 ADMIN, 1 CASHIER (activos)
--    * 1 producto (KG, activo, unit_locked)
--    * 1 terminal
--    * 1 caja abierta (cash_session abierta)
--    * contador de tickets en 0
--
--  CÓMO SE APLICA (verificacion de M2-27 ya confirmo 001 en exit 0):
--     psql -U postgres -d frubber -v ON_ERROR_STOP=1 -f seed-concurrencia-postgresql.sql
--
--  El script NO es idempotente completo: hace DELETE de los datos de prueba
--  (patron de limpieza del escrow) y siembra el escenario. Al terminar la
--  prueba la base se limpia con seed-concurrencia-postgresql.sql en modo "vacío"
--  (comentar los INSERT y no los DELETE).
-- =============================================================================

BEGIN;

-- Limpieza del escrow de pruebas: los triggers de inmutabilidad (M2-27) bloquean
-- DELETE/UPDATE sobre venta/línea/caja; para el reset de datos de prueba del
-- banco de pruebas se desactiva la aplicacion de triggers (modo replica), que es
-- el mecanismo de limpieza que usaria el restaurado de backup en el motor real.
SET LOCAL session_replication_role = replica;

DELETE FROM sale_void;
DELETE FROM sale_line;
DELETE FROM sale;
DELETE FROM cash_session;
DELETE FROM app_user;
DELETE FROM product;
DELETE FROM terminal;
DELETE FROM ticket_counter;

INSERT INTO ticket_counter (scope, last_number) VALUES ('venta', 0);

INSERT INTO app_user (display_name, role, is_active, pin_salt, pin_hash, pin_iterations, pin_algorithm_version)
VALUES ('Verif Admin', 'ADMIN', true, gen_random_bytes(16), gen_random_bytes(32), 210000, 1);

INSERT INTO app_user (display_name, role, is_active, pin_salt, pin_hash, pin_iterations, pin_algorithm_version)
VALUES ('Verif Cajero', 'CASHIER', true, gen_random_bytes(16), gen_random_bytes(32), 210000, 1);

INSERT INTO terminal (name, machine_code, is_active)
VALUES ('Caja verif M2-29', 'AAAAAAAA11111111', true);

INSERT INTO product (plu, name, unit, price_cop, stock_milli, is_active, unit_locked)
VALUES ('VERIF29', 'Producto verif M2-29', 'KG', 1500, 1000000, true, true);

INSERT INTO cash_session (terminal_id, opened_by_user_id, opened_by_name_snapshot, opening_balance_cop)
SELECT t.terminal_id, u.user_id, u.display_name, 100000
  FROM terminal t
  JOIN app_user u ON u.display_name = 'Verif Admin'
 WHERE t.name = 'Caja verif M2-29';

COMMIT;
