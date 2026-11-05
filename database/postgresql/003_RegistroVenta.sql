-- JHO-111 · Operación transaccional de venta para el esquema 2.0.
-- Se aplica solo después de 001_Baseline.sql y 002_Security.sql, por el
-- administrador del servidor. No concede acceso directo a tablas críticas.
-- La integración con la aplicación y el reintento idempotente (JHO-114) siguen
-- pendientes: un ClientRequestId repetido se rechaza, no se reintenta aquí.
-- El ejecutor debe aplicar este archivo dentro de su propia transacción.

CREATE FUNCTION public.app_registrar_venta(
    p_cash_session_id bigint,
    p_cashier_user_id bigint,
    p_discount_cop bigint,
    p_payment_method text,
    p_received_cop bigint,
    p_request_id uuid,
    p_lines jsonb
) RETURNS TABLE (sale_id bigint, sale_number bigint, total_cop bigint)
LANGUAGE plpgsql SECURITY DEFINER
SET search_path = pg_catalog, public
AS $function$
DECLARE
    v_terminal_id bigint;
    v_cashier_name text;
    v_line record;
    v_product public.product%ROWTYPE;
    v_subtotal bigint := 0;
    v_total bigint;
    v_number bigint;
    v_sale_id bigint;
    v_count integer;
    v_distinct integer;
    v_line_number integer := 0;
BEGIN
    -- La identidad procede de la conexión, no de un parámetro manipulable.
    IF session_user !~ '^puesto_[0-9a-f]{16}$' THEN
        RAISE EXCEPTION 'La sesión no corresponde a un puesto autorizado' USING ERRCODE = '42501';
    END IF;
    SELECT t.terminal_id INTO v_terminal_id
      FROM public.terminal t
     WHERE t.machine_code = upper(substr(session_user, 8)) AND t.is_active;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'El puesto no está activo' USING ERRCODE = '42501';
    END IF;

    -- Bloquea la caja para que no se cierre mientras se registra el cobro.
    PERFORM 1 FROM public.cash_session cs
     WHERE cs.cash_session_id = p_cash_session_id AND cs.terminal_id = v_terminal_id
       AND cs.closed_at IS NULL FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'La caja no está abierta en este puesto' USING ERRCODE = '23514';
    END IF;
    SELECT u.display_name INTO v_cashier_name FROM public.app_user u
     WHERE u.user_id = p_cashier_user_id AND u.is_active AND u.role IN ('ADMIN', 'CASHIER');
    IF NOT FOUND THEN
        RAISE EXCEPTION 'El usuario de la venta no está activo' USING ERRCODE = '23514';
    END IF;
    IF p_request_id IS NULL OR p_discount_cop IS NULL OR p_discount_cop < 0
       OR p_payment_method IS NULL OR p_payment_method NOT IN ('CASH', 'TRANSFER')
       OR p_lines IS NULL OR jsonb_typeof(p_lines) <> 'array' THEN
        RAISE EXCEPTION 'Datos de venta no válidos' USING ERRCODE = '23514';
    END IF;
    IF jsonb_array_length(p_lines) = 0 THEN
        RAISE EXCEPTION 'La venta no puede estar vacía' USING ERRCODE = '23514';
    END IF;
    SELECT count(*), count(DISTINCT l.product_id) INTO v_count, v_distinct
      FROM jsonb_to_recordset(p_lines) AS l(product_id bigint);
    IF v_count <> v_distinct THEN
        RAISE EXCEPTION 'Hay productos repetidos o sin identificar' USING ERRCODE = '23514';
    END IF;

    -- Orden fijo para evitar bloqueos cruzados con productos compartidos.
    PERFORM p.product_id FROM public.product p
     WHERE p.product_id IN (SELECT l.product_id FROM jsonb_to_recordset(p_lines) AS l(product_id bigint))
     ORDER BY p.product_id FOR UPDATE;
    FOR v_line IN SELECT * FROM jsonb_to_recordset(p_lines) AS l(
        product_id bigint, quantity_milli bigint, unit_price_cop bigint,
        plu text, name text, unit text)
    LOOP
        SELECT * INTO v_product FROM public.product p WHERE p.product_id = v_line.product_id;
        IF NOT FOUND OR NOT v_product.is_active
           OR v_line.quantity_milli IS NULL OR v_line.quantity_milli <= 0
           OR (v_product.unit = 'UNIT' AND v_line.quantity_milli % 1000 <> 0)
           OR v_line.unit_price_cop IS DISTINCT FROM v_product.price_cop
           OR v_line.plu IS DISTINCT FROM v_product.plu
           OR v_line.name IS DISTINCT FROM v_product.name
           OR v_line.unit IS DISTINCT FROM v_product.unit THEN
            RAISE EXCEPTION 'El producto, precio o cantidad de la venta no es válido' USING ERRCODE = '23514';
        END IF;
        v_subtotal := v_subtotal + (v_product.price_cop * v_line.quantity_milli + 500) / 1000;
    END LOOP;
    v_total := v_subtotal - p_discount_cop;
    IF v_total <= 0 OR (p_payment_method = 'CASH' AND (p_received_cop IS NULL OR p_received_cop < v_total))
       OR (p_payment_method = 'TRANSFER' AND p_received_cop IS NOT NULL) THEN
        RAISE EXCEPTION 'El total o el pago no es válido' USING ERRCODE = '23514';
    END IF;

    -- Número, venta, líneas e inventario se confirman o revierten juntos.
    v_number := public.next_ticket();
    INSERT INTO public.sale (sale_number, cash_session_id, cashier_user_id, cashier_name_snapshot,
        subtotal_cop, discount_cop, total_cop, payment_method, received_cop, change_cop, client_request_id)
    VALUES (v_number, p_cash_session_id, p_cashier_user_id, v_cashier_name, v_subtotal,
        p_discount_cop, v_total, p_payment_method, p_received_cop,
        CASE WHEN p_payment_method = 'CASH' THEN p_received_cop - v_total ELSE NULL END, p_request_id)
    RETURNING public.sale.sale_id INTO v_sale_id;

    FOR v_line IN SELECT * FROM jsonb_to_recordset(p_lines) AS l(
        product_id bigint, quantity_milli bigint, unit_price_cop bigint,
        plu text, name text, unit text)
    LOOP
        v_line_number := v_line_number + 1;
        INSERT INTO public.sale_line (sale_id, line_number, product_id, plu_snapshot,
            product_name_snapshot, unit_snapshot, unit_price_cop_snapshot, quantity_milli, line_total_cop)
        VALUES (v_sale_id, v_line_number, v_line.product_id, v_line.plu, v_line.name,
            v_line.unit, v_line.unit_price_cop, v_line.quantity_milli,
            (v_line.unit_price_cop * v_line.quantity_milli + 500) / 1000);
        UPDATE public.product SET stock_milli = stock_milli - v_line.quantity_milli, unit_locked = true
         WHERE product_id = v_line.product_id;
    END LOOP;
    RETURN QUERY SELECT v_sale_id, v_number, v_total;
END
$function$;

REVOKE ALL ON FUNCTION public.app_registrar_venta(bigint, bigint, bigint, text, bigint, uuid, jsonb) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.app_registrar_venta(bigint, bigint, bigint, text, bigint, uuid, jsonb) TO frubber_puesto;
