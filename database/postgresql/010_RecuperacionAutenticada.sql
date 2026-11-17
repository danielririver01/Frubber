-- JHO-114. Aplicar en un servidor de pruebas nuevo tras 004 y 008.
CREATE TABLE public.sale_recovery_database (
    singleton boolean PRIMARY KEY CHECK (singleton),
    database_id uuid NOT NULL UNIQUE
);
INSERT INTO public.sale_recovery_database VALUES (true, gen_random_uuid());

CREATE TABLE public.workstation_sale_session (
    token_hash bytea PRIMARY KEY,
    terminal_id bigint NOT NULL REFERENCES public.terminal ON DELETE CASCADE,
    user_id bigint NOT NULL REFERENCES public.app_user ON DELETE CASCADE,
    pin_salt bytea NOT NULL,
    expires_at timestamptz NOT NULL
);
REVOKE ALL ON public.sale_recovery_database, public.workstation_sale_session FROM PUBLIC, frubber_puesto;

CREATE FUNCTION public.app_sale_recovery_database() RETURNS uuid
LANGUAGE sql STABLE SECURITY DEFINER
SET search_path = pg_catalog, public, pg_temp
AS $$
    SELECT d.database_id FROM public.sale_recovery_database d
    WHERE EXISTS (
        SELECT FROM public.terminal t
        WHERE t.is_active AND t.machine_code = upper(substr(session_user, 8))
          AND session_user ~ '^puesto_[0-9a-f]{16}$'
    );
$$;

CREATE FUNCTION public.app_sale_login(p_user_id bigint, p_candidate bytea)
RETURNS TABLE (authenticated boolean, wait_seconds integer, admin_token uuid, sale_token uuid)
LANGUAGE plpgsql SECURITY DEFINER
SET search_path = pg_catalog, public, pg_temp
AS $$
DECLARE
    v_terminal bigint;
    v_salt bytea;
    v_ok boolean;
    v_wait integer;
    v_admin uuid;
    v_token uuid;
BEGIN
    PERFORM pg_advisory_xact_lock(136, 13);
    SELECT terminal_id INTO v_terminal FROM public.terminal
    WHERE is_active AND machine_code = upper(substr(session_user, 8))
      AND session_user ~ '^puesto_[0-9a-f]{16}$';
    IF v_terminal IS NULL THEN
        RETURN QUERY SELECT false, 0, NULL::uuid, NULL::uuid;
        RETURN;
    END IF;

    SELECT a.authenticated, a.wait_seconds, a.admin_token INTO v_ok, v_wait, v_admin
    FROM public.app_admin_login(p_user_id, p_candidate) a;
    IF v_ok THEN
        SELECT u.pin_salt INTO v_salt FROM public.app_user u WHERE u.user_id = p_user_id AND u.is_active;
        IF FOUND THEN
            v_token := gen_random_uuid();
            DELETE FROM public.workstation_sale_session WHERE expires_at <= clock_timestamp();
            INSERT INTO public.workstation_sale_session
            VALUES (sha256(convert_to(v_token::text, 'UTF8')), v_terminal, p_user_id, v_salt,
                    clock_timestamp() + interval '8 hours');
        END IF;
    END IF;
    RETURN QUERY SELECT v_ok AND v_token IS NOT NULL, v_wait, v_admin, v_token;
END;
$$;

CREATE FUNCTION public.app_sale_submit(
    p_token uuid, p_database uuid, p_cash_id bigint, p_user_id bigint, p_discount bigint,
    p_payment text, p_received bigint, p_request_id uuid, p_lines jsonb
) RETURNS TABLE (sale_id bigint, sale_number bigint, total_cop bigint)
LANGUAGE plpgsql SECURITY DEFINER
SET search_path = pg_catalog, public, pg_temp
AS $$
BEGIN
    IF p_database IS DISTINCT FROM public.app_sale_recovery_database() OR NOT EXISTS (
        SELECT FROM public.workstation_sale_session s
        JOIN public.terminal t USING (terminal_id)
        JOIN public.app_user u USING (user_id)
        WHERE s.token_hash = sha256(convert_to(p_token::text, 'UTF8'))
          AND s.expires_at > statement_timestamp() AND s.user_id = p_user_id
          AND t.is_active AND u.is_active AND u.pin_salt = s.pin_salt
          AND session_user ~ '^puesto_[0-9a-f]{16}$'
          AND t.machine_code = upper(substr(session_user, 8))
    ) THEN
        RAISE EXCEPTION 'El cobro requiere la sesión PIN del puesto y el cajero.' USING ERRCODE = '42501';
    END IF;
    RETURN QUERY SELECT * FROM public.app_cobrar_idempotente(
        p_cash_id, p_user_id, p_discount, p_payment, p_received, p_request_id, p_lines);
END;
$$;

REVOKE ALL ON FUNCTION public.app_sale_recovery_database() FROM PUBLIC;
REVOKE ALL ON FUNCTION public.app_sale_login(bigint, bytea) FROM PUBLIC;
REVOKE ALL ON FUNCTION public.app_sale_submit(uuid, uuid, bigint, bigint, bigint, text, bigint, uuid, jsonb) FROM PUBLIC;
REVOKE ALL ON FUNCTION public.app_cobrar_idempotente(bigint,bigint,bigint,text,bigint,uuid,jsonb) FROM PUBLIC, frubber_puesto;
REVOKE ALL ON FUNCTION public.app_comprobante_por_intento(uuid) FROM PUBLIC, frubber_puesto;
GRANT EXECUTE ON FUNCTION public.app_sale_recovery_database(), public.app_sale_login(bigint, bytea),
    public.app_sale_submit(uuid, uuid, bigint, bigint, bigint, text, bigint, uuid, jsonb) TO frubber_puesto;
