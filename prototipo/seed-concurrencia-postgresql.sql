-- JHO-111: datos exclusivamente sintéticos para el ensayo aislado.
-- Solo se admite una base recién creada por el banco, con nombre aleatorio.
-- Nunca desactiva disparadores, borra datos ni reinicia el contador.
DO $$
BEGIN
    IF current_database() !~ '^frubber_jho111_[0-9a-f]{32}$' THEN
        RAISE EXCEPTION 'Esta semilla solo puede usarse en una base temporal frubber_jho111_<uuid>';
    END IF;
    IF EXISTS (SELECT FROM app_user)
       OR (SELECT count(*) FROM terminal) <> 1
       OR NOT EXISTS (SELECT FROM terminal WHERE machine_code = '0000000000000000' AND NOT is_active)
       OR EXISTS (SELECT FROM product) OR EXISTS (SELECT FROM cash_session)
       OR EXISTS (SELECT FROM sale) OR EXISTS (SELECT FROM sale_line)
       OR (SELECT last_number FROM ticket_counter WHERE scope = 'venta') IS DISTINCT FROM 0 THEN
        RAISE EXCEPTION 'La base de prueba no está vacía o el contador no está en cero';
    END IF;
END $$;

INSERT INTO app_user (display_name, role, is_active, pin_salt, pin_hash, pin_iterations, pin_algorithm_version)
VALUES ('Cajero de prueba', 'CASHIER', true, decode(repeat('00',16), 'hex'), decode(repeat('00',32), 'hex'), 210000, 1);
INSERT INTO terminal (name, machine_code, is_active)
VALUES ('Puesto de prueba 1', 'AAAAAAAA11111111', true), ('Puesto de prueba 2', 'BBBBBBBB22222222', true);
INSERT INTO product (plu, name, unit, price_cop, stock_milli, is_active, unit_locked)
VALUES ('VERIF29', 'Producto de prueba', 'KG', 1500, 1000000, true, true);
INSERT INTO cash_session (terminal_id, opened_by_user_id, opened_by_name_snapshot, opening_balance_cop)
SELECT t.terminal_id, u.user_id, u.display_name, 100000
FROM terminal t CROSS JOIN app_user u
WHERE u.display_name = 'Cajero de prueba' AND t.name IN ('Puesto de prueba 1', 'Puesto de prueba 2');
