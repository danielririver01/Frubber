-- Migración: 002_Trial.sql
-- Estado de la prueba de 30 días y último instante observado (JHO-27).
-- Fila única (TrialStateId = 1): primera ejecución y último now visto.

CREATE TABLE TrialState (
    TrialStateId INTEGER PRIMARY KEY CHECK (TrialStateId = 1),
    FirstRunUtcMs INTEGER NOT NULL CHECK (FirstRunUtcMs > 0),
    LastSeenUtcMs INTEGER NOT NULL CHECK (LastSeenUtcMs > 0)
) STRICT;
