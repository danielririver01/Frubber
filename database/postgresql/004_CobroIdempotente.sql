-- JHO-114 · PROTOTIPO: recuperación del mismo intento de cobro.
-- NO aplicar a bases de clientes, ni conectar a la aplicación ni distribuir.
-- Solo servidor en mantenimiento exclusivo; no puede haber puestos conectados.
-- El ejecutor abre UNA transacción para todo el archivo, con 001-003 previos.
-- Se rechazan ventas existentes: no es una migración ni hace backfill.
-- No autentica al cajero por PIN, ni aplica la licencia, ni persiste el intento
-- en el puesto; estos requisitos siguen pendientes antes del piloto.

LOCK TABLE public.sale IN ACCESS EXCLUSIVE MODE;
DO $guard$
DECLARE
    v_owner name;
    v_definer boolean;
BEGIN
    IF EXISTS (SELECT 1 FROM public.sale) THEN
        RAISE EXCEPTION 'Prototipo idempotente solo admite una base sin ventas' USING ERRCODE = '55000';
    END IF;
    SELECT pg_catalog.pg_get_userbyid(p.proowner), p.prosecdef
      INTO v_owner, v_definer
      FROM pg_catalog.pg_proc p
     WHERE p.oid = pg_catalog.to_regprocedure('public.app_registrar_venta(bigint,bigint,bigint,text,bigint,uuid,jsonb)');
    IF v_owner IS DISTINCT FROM current_user OR v_definer IS DISTINCT FROM true THEN
        RAISE EXCEPTION 'La operación de venta no existe o no pertenece al instalador' USING ERRCODE = '55000';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = 'frubber_puesto') THEN
        RAISE EXCEPTION 'Falta el rol de puestos' USING ERRCODE = '55000';
    END IF;
END
$guard$;

-- La fila es privada. Se inserta antes del cobro y la transacción no la
-- confirma sin la venta correspondiente; la unicidad bloquea los reintentos.
CREATE TABLE public.sale_request_guard (
    client_request_id uuid PRIMARY KEY,
    terminal_id bigint NOT NULL REFERENCES public.terminal(terminal_id) ON DELETE RESTRICT,
    request_payload jsonb NOT NULL,
    sale_id bigint UNIQUE REFERENCES public.sale(sale_id) ON DELETE RESTRICT
);
REVOKE ALL ON TABLE public.sale_request_guard FROM PUBLIC, frubber_puesto;

CREATE FUNCTION public.app_cobrar_idempotente(
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
    v_saved_terminal bigint;
    v_saved_payload jsonb;
    v_saved_sale_id bigint;
    v_payload jsonb;
    v_inserted boolean;
    v_sale record;
BEGIN
    IF session_user !~ '^puesto_[0-9a-f]{16}$' THEN
        RAISE EXCEPTION 'La sesión no corresponde a un puesto autorizado' USING ERRCODE = '42501';
    END IF;
    SELECT t.terminal_id INTO v_terminal_id
      FROM public.terminal AS t
     WHERE t.machine_code = upper(substr(session_user, 8)) AND t.is_active;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'El puesto no está activo' USING ERRCODE = '42501';
    END IF;
    IF p_request_id IS NULL THEN
        RAISE EXCEPTION 'Falta el identificador del intento' USING ERRCODE = '23514';
    END IF;
    IF p_lines IS NULL OR pg_catalog.jsonb_typeof(p_lines) IS DISTINCT FROM 'array' THEN
        RAISE EXCEPTION 'Las líneas de venta no son válidas' USING ERRCODE = '23514';
    END IF;
    -- Never persist arbitrary extra fields (e.g. PINs or credentials). Only
    -- the six keys that the underlying sale function consumes are allowed.
    IF EXISTS (
        SELECT 1 FROM pg_catalog.jsonb_array_elements(p_lines) AS element(value)
         WHERE CASE WHEN pg_catalog.jsonb_typeof(element.value) = 'object' THEN
              EXISTS (SELECT 1 FROM pg_catalog.jsonb_object_keys(element.value) AS k(key)
                       WHERE k.key NOT IN ('product_id', 'quantity_milli', 'unit_price_cop', 'plu', 'name', 'unit'))
              OR pg_catalog.jsonb_typeof(element.value -> 'product_id') IS DISTINCT FROM 'number'
              OR pg_catalog.jsonb_typeof(element.value -> 'quantity_milli') IS DISTINCT FROM 'number'
              OR pg_catalog.jsonb_typeof(element.value -> 'unit_price_cop') IS DISTINCT FROM 'number'
              OR pg_catalog.jsonb_typeof(element.value -> 'plu') IS DISTINCT FROM 'string'
              OR pg_catalog.jsonb_typeof(element.value -> 'name') IS DISTINCT FROM 'string'
              OR pg_catalog.jsonb_typeof(element.value -> 'unit') IS DISTINCT FROM 'string'
              ELSE true END
    ) THEN
        RAISE EXCEPTION 'La línea contiene datos no permitidos' USING ERRCODE = '23514';
    END IF;

    -- JSONB normaliza el orden de propiedades, pero conserva orden de líneas
    -- y valores. Nunca incluir PIN, hash o credenciales en este documento.
    v_payload := pg_catalog.jsonb_build_object(
        'cash_session_id', p_cash_session_id, 'cashier_user_id', p_cashier_user_id,
        'discount_cop', p_discount_cop, 'payment_method', p_payment_method,
        'received_cop', p_received_cop, 'lines', p_lines);

    INSERT INTO public.sale_request_guard AS g (client_request_id, terminal_id, request_payload)
        VALUES (p_request_id, v_terminal_id, v_payload)
        ON CONFLICT (client_request_id) DO NOTHING
        RETURNING true INTO v_inserted;

    -- El conflicto espera al primer intento; si se deshizo, el segundo
    -- inserta su propia fila. Si confirmó, devuelve su venta sin repetirla.
    SELECT g.terminal_id, g.request_payload, g.sale_id
      INTO v_saved_terminal, v_saved_payload, v_saved_sale_id
      FROM public.sale_request_guard AS g
     WHERE g.client_request_id = p_request_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'No se pudo resolver el intento' USING ERRCODE = '55000';
    END IF;
    IF v_saved_terminal IS DISTINCT FROM v_terminal_id OR v_saved_payload IS DISTINCT FROM v_payload THEN
        RAISE EXCEPTION 'El identificador del intento no corresponde a esta solicitud' USING ERRCODE = '42501';
    END IF;

    IF v_inserted IS NOT TRUE THEN
        IF v_saved_sale_id IS NULL THEN
            RAISE EXCEPTION 'El intento no tiene una venta confirmada' USING ERRCODE = '55000';
        END IF;
        RETURN QUERY SELECT s.sale_id, s.sale_number, s.total_cop
          FROM public.sale AS s
         WHERE s.sale_id = v_saved_sale_id AND s.client_request_id = p_request_id;
        IF NOT FOUND THEN
            RAISE EXCEPTION 'No se pudo recuperar la venta del intento' USING ERRCODE = '55000';
        END IF;
        RETURN;
    END IF;

    SELECT * INTO v_sale FROM public.app_registrar_venta(
        p_cash_session_id, p_cashier_user_id, p_discount_cop,
        p_payment_method, p_received_cop, p_request_id, p_lines);
    UPDATE public.sale_request_guard AS g SET sale_id = v_sale.sale_id
     WHERE g.client_request_id = p_request_id;
    RETURN QUERY SELECT v_sale.sale_id::bigint, v_sale.sale_number::bigint, v_sale.total_cop::bigint;
END
$function$;

-- Consulta provisional del comprobante inmutable SOLO desde su puesto activo.
-- Sin resultado no significa que el cobro haya fallado: podría estar en curso.
-- El cliente debe repetir siempre el mismo UUID y datos ante resultado incierto.
-- Aún falta vincular la autorización de cajero/PIN y licencia antes de usarla.
CREATE FUNCTION public.app_comprobante_por_intento(p_request_id uuid)
RETURNS TABLE (
    sale_id bigint, sale_number bigint, sold_at timestamptz,
    cashier_name text, subtotal_cop bigint, discount_cop bigint, total_cop bigint,
    payment_method text, received_cop bigint, change_cop bigint, lines jsonb
)
LANGUAGE plpgsql SECURITY DEFINER
SET search_path = pg_catalog, public
AS $receipt$
DECLARE
    v_terminal_id bigint;
BEGIN
    IF session_user !~ '^puesto_[0-9a-f]{16}$' THEN
        RAISE EXCEPTION 'La sesión no corresponde a un puesto autorizado' USING ERRCODE = '42501';
    END IF;
    SELECT t.terminal_id INTO v_terminal_id FROM public.terminal AS t
     WHERE t.machine_code = upper(substr(session_user, 8)) AND t.is_active;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'El puesto no está activo' USING ERRCODE = '42501';
    END IF;
    RETURN QUERY
    SELECT s.sale_id, s.sale_number, s.sold_at, s.cashier_name_snapshot,
           s.subtotal_cop, s.discount_cop, s.total_cop, s.payment_method,
           s.received_cop, s.change_cop,
           (SELECT pg_catalog.jsonb_agg(pg_catalog.jsonb_build_object(
                'line_number', l.line_number, 'name', l.product_name_snapshot,
                'unit', l.unit_snapshot, 'quantity_milli', l.quantity_milli,
                'unit_price_cop', l.unit_price_cop_snapshot, 'line_total_cop', l.line_total_cop)
                ORDER BY l.line_number)
              FROM public.sale_line AS l WHERE l.sale_id = s.sale_id)
      FROM public.sale_request_guard AS g
      JOIN public.sale AS s ON s.sale_id = g.sale_id AND s.client_request_id = g.client_request_id
     WHERE g.client_request_id = p_request_id AND g.terminal_id = v_terminal_id;
END
$receipt$;
REVOKE ALL ON FUNCTION public.app_comprobante_por_intento(uuid) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.app_comprobante_por_intento(uuid) TO frubber_puesto;

-- El puesto no puede saltarse la ruta que guarda el identificador.
REVOKE ALL ON FUNCTION public.app_registrar_venta(bigint,bigint,bigint,text,bigint,uuid,jsonb) FROM PUBLIC, frubber_puesto;
REVOKE ALL ON FUNCTION public.app_cobrar_idempotente(bigint,bigint,bigint,text,bigint,uuid,jsonb) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.app_cobrar_idempotente(bigint,bigint,bigint,text,bigint,uuid,jsonb) TO frubber_puesto;
