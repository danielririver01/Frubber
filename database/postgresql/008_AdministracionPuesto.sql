-- JHO-136. Aplicar en el servidor dentro de una transacción.
CREATE TABLE public.workstation_admin_session (
    token_hash bytea PRIMARY KEY,
    terminal_id bigint NOT NULL REFERENCES public.terminal,
    user_id bigint NOT NULL REFERENCES public.app_user ON DELETE CASCADE,
    expires_at timestamptz NOT NULL
);
REVOKE ALL ON public.workstation_admin_session FROM PUBLIC, frubber_puesto;

CREATE FUNCTION public.app_admin_login(p_user_id bigint, p_candidate bytea)
RETURNS TABLE (authenticated boolean, wait_seconds integer, admin_token uuid)
LANGUAGE plpgsql SECURITY DEFINER
SET search_path = pg_catalog, public, pg_temp
AS $$
DECLARE
    v_terminal bigint;
    v_ok boolean;
    v_wait integer;
    v_token uuid;
BEGIN
    PERFORM pg_advisory_xact_lock(136, 13);
    SELECT terminal_id INTO v_terminal FROM public.terminal
    WHERE is_active AND machine_code = upper(substr(session_user, 8))
      AND session_user ~ '^puesto_[0-9a-f]{16}$';
    IF v_terminal IS NULL THEN
        RETURN QUERY SELECT false, 0, NULL::uuid;
        RETURN;
    END IF;

    SELECT p.authenticated, p.wait_seconds INTO v_ok, v_wait
    FROM public.app_pin_verify(p_user_id, p_candidate) p;
    IF v_ok AND EXISTS (
        SELECT 1 FROM public.app_user
        WHERE user_id = p_user_id AND is_active AND role = 'ADMIN'
    ) THEN
        v_token := gen_random_uuid();
        DELETE FROM public.workstation_admin_session WHERE expires_at <= clock_timestamp();
        INSERT INTO public.workstation_admin_session
        VALUES (sha256(convert_to(v_token::text, 'UTF8')), v_terminal, p_user_id,
                clock_timestamp() + interval '8 hours');
    END IF;
    RETURN QUERY SELECT v_ok, v_wait, v_token;
END;
$$;

CREATE FUNCTION public.require_workstation_admin(p_token uuid) RETURNS bigint
LANGUAGE plpgsql STABLE
SET search_path = pg_catalog, public, pg_temp
AS $$
DECLARE
    v_user bigint;
BEGIN
    SELECT s.user_id INTO v_user
    FROM public.workstation_admin_session s
    JOIN public.terminal t USING (terminal_id)
    JOIN public.app_user u USING (user_id)
    WHERE s.token_hash = sha256(convert_to(p_token::text, 'UTF8'))
      AND s.expires_at > statement_timestamp()
      AND session_user ~ '^puesto_[0-9a-f]{16}$'
      AND t.machine_code = upper(substr(session_user, 8))
      AND t.is_active AND u.is_active AND u.role = 'ADMIN';
    IF v_user IS NULL THEN
        RAISE EXCEPTION 'Se requiere una sesión administrativa vigente.' USING ERRCODE = '42501';
    END IF;
    RETURN v_user;
END;
$$;

CREATE FUNCTION public.app_admin_sale_lines(p_token uuid, p_sale_number bigint)
RETURNS TABLE (line_number integer, plu text, name text, unit_code text,
               quantity_milli bigint, unit_price_cop bigint, line_total_cop bigint)
LANGUAGE plpgsql STABLE SECURITY DEFINER
SET search_path = pg_catalog, public, pg_temp
AS $$
BEGIN
    PERFORM public.require_workstation_admin(p_token);
    RETURN QUERY
        SELECT l.line_number, l.plu_snapshot, l.product_name_snapshot, l.unit_snapshot,
               l.quantity_milli, l.unit_price_cop_snapshot, l.line_total_cop
        FROM public.sale s JOIN public.sale_line l USING (sale_id)
        WHERE s.sale_number = p_sale_number
        ORDER BY l.line_number;
END;
$$;

REVOKE ALL ON FUNCTION public.app_admin_login(bigint, bytea) FROM PUBLIC;
REVOKE ALL ON FUNCTION public.require_workstation_admin(uuid) FROM PUBLIC, frubber_puesto;
REVOKE ALL ON FUNCTION public.app_admin_sale_lines(uuid, bigint) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.app_admin_login(bigint, bytea) TO frubber_puesto;
GRANT EXECUTE ON FUNCTION public.app_admin_sale_lines(uuid, bigint) TO frubber_puesto;
