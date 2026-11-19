-- JHO-127. Aplicar después de 008 en una transacción.
CREATE FUNCTION public.app_admin_daily_sales(p_token uuid)
RETURNS TABLE (business_date date, terminal_id bigint, terminal_name text,
               total_cop bigint, business_total_cop bigint)
LANGUAGE plpgsql STABLE SECURITY DEFINER
SET search_path = pg_catalog, public, pg_temp
AS $function$
BEGIN
    PERFORM public.require_workstation_admin(p_token);

    RETURN QUERY
    WITH business_day AS (
        SELECT statement_timestamp()::date AS day
    ), sales_by_terminal AS (
        SELECT s.terminal_id, sum(s.total_cop) FILTER (WHERE s.sale_status = 'ACTIVE') AS net
        FROM business_day d
        JOIN public.v_sale_history s
          ON s.sold_at >= d.day::timestamptz AND s.sold_at < (d.day + 1)::timestamptz
        GROUP BY s.terminal_id
    ), terminal_totals AS (
        SELECT t.terminal_id, t.name, coalesce(s.net, 0)::bigint AS net
        FROM public.terminal t
        LEFT JOIN sales_by_terminal s ON s.terminal_id = t.terminal_id
        WHERE t.is_active OR s.terminal_id IS NOT NULL
    )
    SELECT d.day, t.terminal_id, t.name, coalesce(t.net, 0),
           sum(coalesce(t.net, 0)) OVER ()::bigint
    FROM business_day d
    LEFT JOIN terminal_totals t ON true
    ORDER BY t.name, t.terminal_id;
END;
$function$;

REVOKE ALL ON FUNCTION public.app_admin_daily_sales(uuid) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.app_admin_daily_sales(uuid) TO frubber_puesto;
