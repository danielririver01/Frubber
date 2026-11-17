-- JHO-136. Aplicar en el servidor dentro de una transacción.
-- Las mutaciones administrativas comparten el bloqueo con las comprobaciones de los casos de uso.
CREATE FUNCTION public.require_removable_admin(p_id bigint) RETURNS void
LANGUAGE plpgsql
SET search_path = pg_catalog, public, pg_temp
AS $$
BEGIN
    IF EXISTS (SELECT FROM public.app_user WHERE user_id = p_id AND is_active AND role = 'ADMIN')
       AND NOT EXISTS (SELECT FROM public.app_user WHERE user_id <> p_id AND is_active AND role = 'ADMIN') THEN
        RAISE EXCEPTION 'Debe quedar al menos un administrador activo.' USING ERRCODE = '23514';
    END IF;
END;
$$;
REVOKE ALL ON FUNCTION public.require_removable_admin(bigint) FROM PUBLIC;

CREATE FUNCTION public.app_admin_products(p_token uuid)
RETURNS TABLE (product_id integer, plu text, name text, unit text, price_cop bigint, stock_milli bigint, is_active boolean, unit_locked boolean, has_sales_history boolean)
LANGUAGE plpgsql STABLE SECURITY DEFINER
SET search_path = pg_catalog, public, pg_temp
AS $$
BEGIN
    PERFORM public.require_workstation_admin(p_token);
    RETURN QUERY SELECT p.product_id::integer, p.plu, p.name, p.unit, p.price_cop,
        p.stock_milli, p.is_active, p.unit_locked,
        EXISTS (SELECT FROM public.sale_line l WHERE l.product_id = p.product_id)
    FROM public.product p;
END;
$$;

CREATE FUNCTION public.app_admin_product_create(p_token uuid, p_plu text, p_name text, p_unit text, p_price bigint, p_stock bigint, p_locked boolean)
RETURNS integer
LANGUAGE plpgsql SECURITY DEFINER
SET search_path = pg_catalog, public, pg_temp
AS $$
DECLARE
    v_id integer;
BEGIN
    PERFORM pg_advisory_xact_lock(136, 13);
    PERFORM public.require_workstation_admin(p_token);
    INSERT INTO public.product (plu, name, unit, price_cop, stock_milli, is_active, unit_locked)
    VALUES (p_plu, p_name, p_unit, p_price, p_stock, true, p_locked)
    RETURNING product_id::integer INTO v_id;
    RETURN v_id;
END;
$$;

CREATE FUNCTION public.app_admin_product_update(p_token uuid, p_id bigint, p_plu text, p_name text, p_unit text, p_price bigint)
RETURNS void
LANGUAGE plpgsql SECURITY DEFINER
SET search_path = pg_catalog, public, pg_temp
AS $$
BEGIN
    PERFORM pg_advisory_xact_lock(136, 13);
    PERFORM public.require_workstation_admin(p_token);
    UPDATE public.product SET plu = p_plu, name = p_name, unit = p_unit, price_cop = p_price
    WHERE product_id = p_id;
END;
$$;

CREATE FUNCTION public.app_admin_product_set_active(p_token uuid, p_id bigint, p_active boolean)
RETURNS void
LANGUAGE plpgsql SECURITY DEFINER
SET search_path = pg_catalog, public, pg_temp
AS $$
BEGIN
    PERFORM pg_advisory_xact_lock(136, 13);
    PERFORM public.require_workstation_admin(p_token);
    UPDATE public.product SET is_active = p_active WHERE product_id = p_id;
END;
$$;

CREATE FUNCTION public.app_admin_product_set_stock(p_token uuid, p_id bigint, p_stock bigint)
RETURNS void
LANGUAGE plpgsql SECURITY DEFINER
SET search_path = pg_catalog, public, pg_temp
AS $$
BEGIN
    PERFORM pg_advisory_xact_lock(136, 13);
    PERFORM public.require_workstation_admin(p_token);
    UPDATE public.product SET stock_milli = p_stock, unit_locked = unit_locked OR p_stock <> 0
    WHERE product_id = p_id;
END;
$$;

CREATE FUNCTION public.app_admin_product_delete(p_token uuid, p_id bigint)
RETURNS void
LANGUAGE plpgsql SECURITY DEFINER
SET search_path = pg_catalog, public, pg_temp
AS $$
BEGIN
    PERFORM pg_advisory_xact_lock(136, 13);
    PERFORM public.require_workstation_admin(p_token);
    DELETE FROM public.product WHERE product_id = p_id;
END;
$$;

CREATE FUNCTION public.app_admin_users(p_token uuid)
RETURNS TABLE (user_id integer, display_name text, role text, is_active boolean, has_history boolean)
LANGUAGE plpgsql STABLE SECURITY DEFINER
SET search_path = pg_catalog, public, pg_temp
AS $$
BEGIN
    PERFORM public.require_workstation_admin(p_token);
    RETURN QUERY SELECT u.user_id::integer, u.display_name, u.role, u.is_active,
        EXISTS (SELECT FROM public.sale s WHERE s.cashier_user_id = u.user_id)
        OR EXISTS (SELECT FROM public.cash_session c WHERE u.user_id IN (c.opened_by_user_id, c.closed_by_user_id))
        OR EXISTS (SELECT FROM public.sale_void v WHERE v.voided_by_user_id = u.user_id)
        OR EXISTS (SELECT FROM public.pin_attempt_log a WHERE a.user_id = u.user_id)
        OR EXISTS (SELECT FROM public.pin_lock_state l WHERE l.user_id = u.user_id)
    FROM public.app_user u;
END;
$$;

CREATE FUNCTION public.app_admin_user_create(p_token uuid, p_name text, p_role text, p_salt bytea, p_hash bytea, p_iterations integer, p_version integer)
RETURNS integer
LANGUAGE plpgsql SECURITY DEFINER
SET search_path = pg_catalog, public, pg_temp
AS $$
DECLARE
    v_id integer;
BEGIN
    PERFORM pg_advisory_xact_lock(136, 13);
    PERFORM public.require_workstation_admin(p_token);
    INSERT INTO public.app_user (display_name, role, is_active, pin_salt, pin_hash, pin_iterations, pin_algorithm_version)
    VALUES (p_name, p_role, true, p_salt, p_hash, p_iterations, p_version)
    RETURNING user_id::integer INTO v_id;
    RETURN v_id;
END;
$$;

CREATE FUNCTION public.app_admin_user_update(p_token uuid, p_id bigint, p_name text, p_role text)
RETURNS void
LANGUAGE plpgsql SECURITY DEFINER
SET search_path = pg_catalog, public, pg_temp
AS $$
BEGIN
    PERFORM pg_advisory_xact_lock(136, 13);
    PERFORM public.require_workstation_admin(p_token);
    IF p_role <> 'ADMIN' THEN
        PERFORM public.require_removable_admin(p_id);
    END IF;
    UPDATE public.app_user SET display_name = p_name, role = p_role WHERE user_id = p_id;
    IF p_role <> 'ADMIN' THEN
        DELETE FROM public.workstation_admin_session WHERE user_id = p_id;
    END IF;
END;
$$;

CREATE FUNCTION public.app_admin_user_set_active(p_token uuid, p_id bigint, p_active boolean)
RETURNS void
LANGUAGE plpgsql SECURITY DEFINER
SET search_path = pg_catalog, public, pg_temp
AS $$
BEGIN
    PERFORM pg_advisory_xact_lock(136, 13);
    PERFORM public.require_workstation_admin(p_token);
    IF NOT p_active THEN
        PERFORM public.require_removable_admin(p_id);
    END IF;
    UPDATE public.app_user SET is_active = p_active WHERE user_id = p_id;
    IF NOT p_active THEN
        DELETE FROM public.workstation_admin_session WHERE user_id = p_id;
    END IF;
END;
$$;

CREATE FUNCTION public.app_admin_user_set_pin(p_token uuid, p_id bigint, p_salt bytea, p_hash bytea, p_iterations integer, p_version integer)
RETURNS void
LANGUAGE plpgsql SECURITY DEFINER
SET search_path = pg_catalog, public, pg_temp
AS $$
BEGIN
    PERFORM pg_advisory_xact_lock(136, 13);
    PERFORM public.require_workstation_admin(p_token);
    UPDATE public.app_user SET pin_salt = p_salt, pin_hash = p_hash, pin_iterations = p_iterations,
        pin_algorithm_version = p_version WHERE user_id = p_id;
    DELETE FROM public.workstation_admin_session WHERE user_id = p_id;
END;
$$;

CREATE FUNCTION public.app_admin_user_delete(p_token uuid, p_id bigint)
RETURNS void
LANGUAGE plpgsql SECURITY DEFINER
SET search_path = pg_catalog, public, pg_temp
AS $$
BEGIN
    PERFORM pg_advisory_xact_lock(136, 13);
    PERFORM public.require_workstation_admin(p_token);
    PERFORM public.require_removable_admin(p_id);
    DELETE FROM public.app_user WHERE user_id = p_id;
END;
$$;

REVOKE ALL ON FUNCTION public.app_admin_products(uuid) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.app_admin_products(uuid) TO frubber_puesto;
REVOKE ALL ON FUNCTION public.app_admin_product_create(uuid, text, text, text, bigint, bigint, boolean) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.app_admin_product_create(uuid, text, text, text, bigint, bigint, boolean) TO frubber_puesto;
REVOKE ALL ON FUNCTION public.app_admin_product_update(uuid, bigint, text, text, text, bigint) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.app_admin_product_update(uuid, bigint, text, text, text, bigint) TO frubber_puesto;
REVOKE ALL ON FUNCTION public.app_admin_product_set_active(uuid, bigint, boolean) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.app_admin_product_set_active(uuid, bigint, boolean) TO frubber_puesto;
REVOKE ALL ON FUNCTION public.app_admin_product_set_stock(uuid, bigint, bigint) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.app_admin_product_set_stock(uuid, bigint, bigint) TO frubber_puesto;
REVOKE ALL ON FUNCTION public.app_admin_product_delete(uuid, bigint) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.app_admin_product_delete(uuid, bigint) TO frubber_puesto;
REVOKE ALL ON FUNCTION public.app_admin_users(uuid) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.app_admin_users(uuid) TO frubber_puesto;
REVOKE ALL ON FUNCTION public.app_admin_user_create(uuid, text, text, bytea, bytea, integer, integer) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.app_admin_user_create(uuid, text, text, bytea, bytea, integer, integer) TO frubber_puesto;
REVOKE ALL ON FUNCTION public.app_admin_user_update(uuid, bigint, text, text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.app_admin_user_update(uuid, bigint, text, text) TO frubber_puesto;
REVOKE ALL ON FUNCTION public.app_admin_user_set_active(uuid, bigint, boolean) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.app_admin_user_set_active(uuid, bigint, boolean) TO frubber_puesto;
REVOKE ALL ON FUNCTION public.app_admin_user_set_pin(uuid, bigint, bytea, bytea, integer, integer) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.app_admin_user_set_pin(uuid, bigint, bytea, bytea, integer, integer) TO frubber_puesto;
REVOKE ALL ON FUNCTION public.app_admin_user_delete(uuid, bigint) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.app_admin_user_delete(uuid, bigint) TO frubber_puesto;
