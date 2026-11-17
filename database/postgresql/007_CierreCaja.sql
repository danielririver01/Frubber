-- JHO-136 / D-06. Aplicar en el servidor después de 001 y 002, en una transacción.
CREATE FUNCTION public.app_cerrar_caja(p_cash_session_id bigint, p_user_id bigint)
RETURNS TABLE (
    cash_id bigint, opening_balance bigint, cash_sales bigint, transfer_sales bigint,
    discount_total bigint, net_sales bigint, expected_cash bigint, sale_count integer
)
LANGUAGE plpgsql SECURITY DEFINER
SET search_path = pg_catalog, public, pg_temp
AS $function$
DECLARE
    v_terminal_id bigint;
    v_user_name text;
    v_cash public.cash_session%ROWTYPE;
BEGIN
    IF session_user !~ '^puesto_[0-9a-f]{16}$' THEN
        RAISE EXCEPTION 'La sesión no corresponde a un puesto autorizado' USING ERRCODE = '42501';
    END IF;

    SELECT terminal_id INTO v_terminal_id
      FROM public.terminal
     WHERE machine_code = upper(substr(session_user, 8)) AND is_active
       FOR SHARE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'El puesto no está activo' USING ERRCODE = '42501';
    END IF;

    SELECT display_name INTO v_user_name
      FROM public.app_user
     WHERE user_id = p_user_id AND is_active AND role IN ('ADMIN', 'CASHIER')
       FOR SHARE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'El usuario no está activo' USING ERRCODE = '42501';
    END IF;

    -- En READ COMMITTED, el resumen toma un snapshot nuevo después de esperar
    -- por una venta en curso. Ambos caminos bloquean la misma fila de caja.
    IF current_setting('transaction_isolation') <> 'read committed' THEN
        RAISE EXCEPTION 'El cierre requiere READ COMMITTED' USING ERRCODE = '25001';
    END IF;

    SELECT cs.* INTO v_cash FROM public.cash_session cs
     WHERE cs.cash_session_id = p_cash_session_id AND cs.terminal_id = v_terminal_id
       FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'La caja no pertenece a este puesto' USING ERRCODE = '42501';
    END IF;

    -- Repetir el cierre de la misma sesión devuelve su resumen congelado.
    IF v_cash.closed_at IS NULL THEN
        UPDATE public.cash_session cs
           SET closed_at = clock_timestamp(),
               closed_by_user_id = p_user_id,
               closed_by_name_snapshot = v_user_name,
               closed_cash_sales_cop = totals.cash_sales_cop,
               closed_transfer_sales_cop = totals.transfer_sales_cop,
               closed_discount_cop = totals.discount_cop,
               closed_net_sales_cop = totals.net_sales_cop,
               closed_expected_cash_cop = totals.expected_cash_cop,
               closed_sale_count = totals.sale_count
          FROM public.v_open_cash_totals totals
         WHERE cs.cash_session_id = p_cash_session_id
           AND totals.cash_session_id = cs.cash_session_id
         RETURNING cs.* INTO STRICT v_cash;
    END IF;

    RETURN QUERY SELECT v_cash.cash_session_id, v_cash.opening_balance_cop,
        v_cash.closed_cash_sales_cop, v_cash.closed_transfer_sales_cop,
        v_cash.closed_discount_cop, v_cash.closed_net_sales_cop,
        v_cash.closed_expected_cash_cop, v_cash.closed_sale_count;
END
$function$;

REVOKE ALL ON FUNCTION public.app_cerrar_caja(bigint, bigint) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.app_cerrar_caja(bigint, bigint) TO frubber_puesto;
