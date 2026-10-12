-- Frubber POS
-- Migración: 004_Scale.sql
-- Cambio de alcance aprobado por el dueño el 2026-10-12 (issue JHO-86):
-- lectura de báscula por puerto COM (RS232 nativo o USB con HL-340/CH340).
-- Este script no ejecuta BEGIN/COMMIT; lo abre el ejecutor de migraciones.

CREATE TABLE ScaleSetting (
    ScaleSettingId INTEGER PRIMARY KEY CHECK (ScaleSettingId = 1),
    IsEnabled INTEGER NOT NULL DEFAULT 0 CHECK (IsEnabled IN (0, 1)),
    PortName TEXT CHECK (
        PortName IS NULL
        OR (PortName = trim(PortName) AND length(PortName) BETWEEN 1 AND 40)
    ),
    BaudRate INTEGER NOT NULL DEFAULT 9600
        CHECK (BaudRate IN (1200, 2400, 4800, 9600, 19200, 38400, 57600, 115200)),
    Protocol TEXT NOT NULL DEFAULT 'CONTINUOUS'
        CHECK (Protocol IN ('CONTINUOUS', 'TORREY_P', 'CAS_W', 'SYSTEL_ENQ')),
    ScaleUnit TEXT NOT NULL DEFAULT 'KG' CHECK (ScaleUnit IN ('KG', 'LB')),
    ReadTimeoutMs INTEGER NOT NULL DEFAULT 1200
        CHECK (ReadTimeoutMs BETWEEN 200 AND 10000)
) STRICT;

INSERT INTO ScaleSetting (ScaleSettingId) VALUES (1);
