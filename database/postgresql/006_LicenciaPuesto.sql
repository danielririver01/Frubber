-- JHO-136. Aplicar en el servidor después de 002, dentro de una transacción.
CREATE FUNCTION public.app_licencia_puesto()
RETURNS TABLE (signed_payload text, workstation_slot bigint)
LANGUAGE sql STABLE SECURITY DEFINER
SET search_path = pg_catalog, public, pg_temp
AS $function$
    SELECT license.signed_payload,
           (SELECT count(*) FROM public.terminal authorized
             WHERE authorized.is_active AND authorized.terminal_id <= t.terminal_id)
      FROM public.terminal t
      CROSS JOIN public.license_state license
     WHERE session_user ~ '^puesto_[0-9a-f]{16}$'
       AND t.machine_code = upper(substr(session_user, 8))
       AND t.is_active
       AND license.license_state_id = 1;
$function$;

REVOKE ALL ON FUNCTION public.app_licencia_puesto() FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.app_licencia_puesto() TO frubber_puesto;
