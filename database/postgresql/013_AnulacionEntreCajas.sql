-- JHO-126. Aplicar después de 008 en una transacción.
CREATE FUNCTION public.app_admin_sale_cash_open(p_token uuid, p_sale_number bigint)
RETURNS boolean
LANGUAGE plpgsql STABLE SECURITY DEFINER
SET search_path = pg_catalog, public, pg_temp
AS $function$
BEGIN
    PERFORM public.require_workstation_admin(p_token);
    RETURN EXISTS (
        SELECT 1 FROM public.sale s
        JOIN public.cash_session cs USING (cash_session_id)
        WHERE s.sale_number = p_sale_number AND cs.closed_at IS NULL
    );
END;
$function$;

CREATE FUNCTION public.app_admin_void_sale(p_token uuid, p_sale_number bigint)
RETURNS text
LANGUAGE plpgsql SECURITY DEFINER
SET search_path = pg_catalog, public, pg_temp
AS $function$
DECLARE
    v_admin_id bigint;
    v_admin_name text;
    v_sale_id bigint;
    v_cash_closed_at timestamptz;
BEGIN
    PERFORM pg_advisory_xact_lock(136, 13);
    v_admin_id := public.require_workstation_admin(p_token);
    SELECT display_name INTO STRICT v_admin_name FROM public.app_user WHERE user_id = v_admin_id;

    -- Closing the sale's register takes the same lock before calculating its totals.
    SELECT s.sale_id, cs.closed_at INTO v_sale_id, v_cash_closed_at
      FROM public.sale s JOIN public.cash_session cs USING (cash_session_id)
     WHERE s.sale_number = p_sale_number
       FOR UPDATE OF cs;
    IF NOT FOUND THEN
        RETURN 'MISSING';
    END IF;
    IF EXISTS (SELECT 1 FROM public.sale_void WHERE sale_id = v_sale_id) THEN
        RETURN 'ALREADY_VOIDED';
    END IF;
    IF v_cash_closed_at IS NOT NULL THEN
        RETURN 'CASH_CLOSED';
    END IF;

    PERFORM p.product_id FROM public.product p
     WHERE p.product_id IN (SELECT l.product_id FROM public.sale_line l WHERE l.sale_id = v_sale_id)
     ORDER BY p.product_id FOR UPDATE;
    INSERT INTO public.sale_void (sale_id, voided_at, voided_by_user_id, voided_by_name_snapshot)
    VALUES (v_sale_id, clock_timestamp(), v_admin_id, v_admin_name);
    UPDATE public.product p SET stock_milli = p.stock_milli + items.quantity_milli
      FROM (SELECT product_id, sum(quantity_milli) AS quantity_milli FROM public.sale_line
             WHERE sale_id = v_sale_id GROUP BY product_id) items
     WHERE p.product_id = items.product_id;
    RETURN 'VOIDED';
END;
$function$;

REVOKE ALL ON FUNCTION public.app_admin_sale_cash_open(uuid, bigint) FROM PUBLIC;
REVOKE ALL ON FUNCTION public.app_admin_void_sale(uuid, bigint) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.app_admin_sale_cash_open(uuid, bigint) TO frubber_puesto;
GRANT EXECUTE ON FUNCTION public.app_admin_void_sale(uuid, bigint) TO frubber_puesto;
