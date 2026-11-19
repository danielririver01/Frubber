-- JHO-124. Conserva el periodo de prueba y la configuración de báscula de la 1.0.
-- Aplicar dentro de la transacción de importación. La línea base 001 permanece intacta.
CREATE TABLE IF NOT EXISTS public.trial_state (
    trial_state_id smallint PRIMARY KEY CHECK (trial_state_id = 1),
    first_run_at timestamptz NOT NULL,
    last_seen_at timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS public.scale_setting (
    scale_setting_id smallint PRIMARY KEY CHECK (scale_setting_id = 1),
    is_enabled boolean NOT NULL,
    port_name text CHECK (port_name IS NULL OR (port_name = btrim(port_name) AND length(port_name) BETWEEN 1 AND 40)),
    baud_rate integer NOT NULL CHECK (baud_rate IN (1200,2400,4800,9600,19200,38400,57600,115200)),
    protocol text NOT NULL CHECK (protocol IN ('CONTINUOUS','TORREY_P','CAS_W','SYSTEL_ENQ')),
    scale_unit text NOT NULL CHECK (scale_unit IN ('KG','LB')),
    read_timeout_ms integer NOT NULL CHECK (read_timeout_ms BETWEEN 200 AND 10000)
);

REVOKE ALL ON public.trial_state, public.scale_setting FROM PUBLIC;

ALTER TABLE public.cash_session
    ADD COLUMN IF NOT EXISTS legacy_opened_at_offset_minutes smallint CHECK (legacy_opened_at_offset_minutes BETWEEN -840 AND 840),
    ADD COLUMN IF NOT EXISTS closed_at_legacy_offset_minutes smallint CHECK (closed_at_legacy_offset_minutes BETWEEN -840 AND 840);
ALTER TABLE public.sale
    ADD COLUMN IF NOT EXISTS legacy_sold_at_offset_minutes smallint CHECK (legacy_sold_at_offset_minutes BETWEEN -840 AND 840);
ALTER TABLE public.sale_void
    ADD COLUMN IF NOT EXISTS legacy_voided_at_offset_minutes smallint CHECK (legacy_voided_at_offset_minutes BETWEEN -840 AND 840);
