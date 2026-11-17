-- JHO-136. Aplicar en el servidor después de 001 y 002, en una transacción.
CREATE FUNCTION public.app_abrir_caja(p_user_id bigint, p_balance_cop bigint)
RETURNS bigint
LANGUAGE plpgsql SECURITY DEFINER
SET search_path = pg_catalog, public, pg_temp
AS $function$
DECLARE
    v_terminal_id bigint;
    v_user_name text;
    v_session_id bigint;
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

    IF p_balance_cop IS NULL OR p_balance_cop < 0 THEN
        RAISE EXCEPTION 'El saldo inicial no es válido' USING ERRCODE = '23514';
    END IF;

    INSERT INTO public.cash_session (terminal_id, opened_by_user_id, opened_by_name_snapshot, opening_balance_cop)
    VALUES (v_terminal_id, p_user_id, v_user_name, p_balance_cop)
    ON CONFLICT (terminal_id) WHERE closed_at IS NULL DO NOTHING
    RETURNING cash_session_id INTO v_session_id;
    RETURN v_session_id;
END
$function$;

REVOKE ALL ON FUNCTION public.app_abrir_caja(bigint, bigint) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.app_abrir_caja(bigint, bigint) TO frubber_puesto;
