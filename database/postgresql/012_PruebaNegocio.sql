-- JHO-115. Aplicar después de 002 en una transacción. Conserva el estado importado por 011.
CREATE TABLE IF NOT EXISTS public.trial_state (
    trial_state_id smallint PRIMARY KEY CHECK (trial_state_id = 1),
    first_run_at timestamptz NOT NULL,
    last_seen_at timestamptz NOT NULL
);

REVOKE ALL ON public.trial_state FROM PUBLIC, frubber_puesto;

-- Sample the clock after locking the row so concurrent checks cannot look like a rollback.
CREATE FUNCTION public.advance_business_trial()
RETURNS TABLE (server_time timestamptz, trial_start timestamptz, trial_last_seen timestamptz)
LANGUAGE plpgsql
SET search_path = pg_catalog, public, pg_temp
AS $function$
BEGIN
    SELECT first_run_at, last_seen_at INTO trial_start, trial_last_seen
      FROM public.trial_state WHERE trial_state_id = 1 FOR UPDATE;

    server_time := clock_timestamp();
    IF server_time > trial_last_seen THEN
        UPDATE public.trial_state SET last_seen_at = server_time WHERE trial_state_id = 1;
    END IF;
    RETURN NEXT;
END;
$function$;

REVOKE ALL ON FUNCTION public.advance_business_trial() FROM PUBLIC;

CREATE FUNCTION public.app_estado_operativo_puesto()
RETURNS TABLE (signed_payload text, workstation_slot bigint, server_time timestamptz,
               trial_start timestamptz, trial_last_seen timestamptz)
LANGUAGE plpgsql SECURITY DEFINER
SET search_path = pg_catalog, public, pg_temp
AS $function$
BEGIN
    SELECT license.signed_payload,
           (SELECT count(*) FROM public.terminal authorized
             WHERE authorized.is_active AND authorized.terminal_id <= t.terminal_id)
      INTO signed_payload, workstation_slot
      FROM public.terminal t
      LEFT JOIN public.license_state license ON license.license_state_id = 1
     WHERE session_user ~ '^puesto_[0-9a-f]{16}$'
       AND t.machine_code = upper(substr(session_user, 8))
       AND t.is_active;

    IF NOT FOUND THEN
        RETURN;
    END IF;

    IF signed_payload IS NULL THEN
        SELECT trial.server_time, trial.trial_start, trial.trial_last_seen
          INTO server_time, trial_start, trial_last_seen
          FROM public.advance_business_trial() trial;
    ELSE
        server_time := clock_timestamp();
    END IF;
    RETURN NEXT;
END;
$function$;

REVOKE ALL ON FUNCTION public.app_estado_operativo_puesto() FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.app_estado_operativo_puesto() TO frubber_puesto;
