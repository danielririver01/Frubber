#!/usr/bin/env python3
"""Comprueba el protocolo SQL del instalador, no ejecuta .NET.

Solo para un clúster de prueba desechable y dedicado. Crea bases con nombres
aleatorios propios y las elimina al terminar. Requiere psycopg 3 y CREATEDB.
"""
import concurrent.futures
import hashlib
import os
from pathlib import Path
import re
import threading
import uuid

import psycopg
from psycopg import sql

ROOT = Path(__file__).resolve().parents[1]
if os.environ.get("FRUBBER_JHO136_ISOLATED") != "1":
    raise SystemExit("Use solo un clúster desechable: configure FRUBBER_JHO136_ISOLATED=1.")
DSN = os.environ.get("FRUBBER_JHO136_ADMIN_DSN")
if not DSN:
    raise SystemExit("Falta FRUBBER_JHO136_ADMIN_DSN del servidor de pruebas aislado.")

source = (ROOT / "src/FrubberPOS.Infrastructure/Migrations/Postgres20BaselineInstaller.cs").read_text(encoding="utf-8")
checks = re.findall(r'check.CommandText = """\n(.*?)\n\s*""";', source, re.S)
if len(checks) != 1:
    raise SystemExit("No se pudo identificar de forma unívoca la consulta de precondiciones.")
check_sql = checks[0]
baseline = (ROOT / "database/postgresql/001_Baseline.sql").read_bytes().decode("utf-8")
checksum = hashlib.sha256(baseline.encode("utf-8")).hexdigest().upper()


def protocol(dsn):
    # Emulación deliberada: los resultados no demuestran que el C# compile.
    with psycopg.connect(dsn) as c:
        c.execute("SELECT pg_catalog.pg_advisory_xact_lock(1179796818, 20); SET LOCAL search_path = public, pg_temp;")
        if c.execute("SELECT pg_catalog.to_regclass('public.schema_migration') IS NOT NULL").fetchone()[0]:
            if not c.execute("""SELECT COALESCE((SELECT c.relkind = 'r' AND c.relpersistence = 'p' AND NOT c.relrowsecurity
                                  FROM pg_catalog.pg_class c
                                 WHERE c.oid = pg_catalog.to_regclass('public.schema_migration')), false)""").fetchone()[0]:
                return -1
            if c.execute("SELECT pg_catalog.to_regclass('public.schemamigration') IS NOT NULL").fetchone()[0]:
                return -1
            records = c.execute("SELECT version, script_name, checksum FROM public.schema_migration ORDER BY version").fetchall()
            return 0 if records == [(1, "001_Baseline.sql", checksum)] else -1
        if c.execute(check_sql).fetchone()[0]:
            return -1
        c.execute(baseline)
        c.execute("INSERT INTO public.schema_migration(version, script_name, checksum) VALUES (1,%s,%s)",
                  ("001_Baseline.sql", checksum))
        return 1


def main():
    created = []
    with psycopg.connect(DSN, autocommit=True) as admin:
        print("PostgreSQL", admin.execute("SHOW server_version").fetchone()[0])
        def create():
            name = "frubber_jho136_" + uuid.uuid4().hex
            admin.execute(sql.SQL("CREATE DATABASE {}").format(sql.Identifier(name)))
            created.append(name)
            return psycopg.conninfo.make_conninfo(DSN, dbname=name)
        try:
            empty = create()
            barrier = threading.Barrier(2, timeout=30)
            def worker():
                barrier.wait()
                return protocol(empty)
            with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
                results = list(pool.map(lambda _: worker(), range(2)))
            assert sorted(results) == [0, 1], results
            with psycopg.connect(empty) as c:
                assert c.execute("SELECT count(*) FROM public.schema_migration").fetchone()[0] == 1
                c.execute("INSERT INTO public.schema_migration(version,script_name,checksum) VALUES (2,'future.sql',repeat('A',64))")
            assert protocol(empty) == -1
            print("OK: concurrencia 1/0, registro único y rechazo de versión futura.")

            fixtures = [
                ("tabla con datos", "CREATE TABLE public.unrelated(id integer); INSERT INTO public.unrelated VALUES (7)", "SELECT count(*) FROM public.unrelated WHERE id=7"),
                ("esquema", "CREATE SCHEMA unrelated", "SELECT count(*) FROM pg_namespace WHERE nspname='unrelated'"),
                ("función", "CREATE FUNCTION public.unrelated() RETURNS integer LANGUAGE sql AS 'SELECT 7'", "SELECT count(*) FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace WHERE n.nspname='public' AND p.proname='unrelated'"),
                ("collation", "CREATE COLLATION public.unrelated (provider=libc, locale='C')", "SELECT count(*) FROM pg_collation c JOIN pg_namespace n ON n.oid=c.collnamespace WHERE n.nspname='public' AND c.collname='unrelated'"),
                ("publicación", "CREATE PUBLICATION unrelated", "SELECT count(*) FROM pg_publication WHERE pubname='unrelated'"),
                ("objeto grande", "SELECT lo_create(0)", "SELECT count(*) FROM pg_largeobject_metadata"),
            ]
            for label, setup, verify in fixtures:
                target = create()
                with psycopg.connect(target) as c:
                    c.execute(setup)
                assert protocol(target) == -1, label
                with psycopg.connect(target) as c:
                    assert c.execute(verify).fetchone()[0] == 1, label
                    assert c.execute("SELECT to_regclass('public.schema_migration') IS NULL").fetchone()[0], label
            print("OK: se rechazan y conservan tabla, esquema, función, collation, publicación y objeto grande.")

            # Deliberately dangerous name resolution only inside this scratch DB:
            # preflight must not execute either user-defined shadow function.
            shadow = create()
            with psycopg.connect(shadow) as c:
                c.execute("""
                    CREATE FUNCTION public.to_regclass(text) RETURNS pg_catalog.regclass
                      LANGUAGE plpgsql AS $$BEGIN RAISE EXCEPTION 'shadow to_regclass'; END$$;
                    CREATE FUNCTION public.pg_advisory_xact_lock(integer, integer) RETURNS void
                      LANGUAGE plpgsql AS $$BEGIN RAISE EXCEPTION 'shadow lock'; END$$;
                """)
            hostile_path = psycopg.conninfo.make_conninfo(shadow, options="-c search_path=public,pg_catalog")
            assert protocol(hostile_path) == -1
            with psycopg.connect(shadow) as c:
                assert c.execute("SELECT count(*) FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid=p.pronamespace WHERE n.nspname='public' AND p.proname IN ('to_regclass','pg_advisory_xact_lock')").fetchone()[0] == 2
                assert c.execute("SELECT pg_catalog.to_regclass('public.schema_migration') IS NULL").fetchone()[0]
            print("OK: funciones homónimas no se ejecutan aun con search_path inicial no confiable.")

            fake_ledger = create()
            with psycopg.connect(fake_ledger) as c:
                c.execute("""
                    CREATE FUNCTION public.ledger_trap() RETURNS integer LANGUAGE plpgsql
                      AS $$BEGIN RAISE EXCEPTION 'ledger view executed'; END$$;
                    CREATE VIEW public.schema_migration AS
                      SELECT public.ledger_trap() AS version, '001_Baseline.sql'::text AS script_name, repeat('A',64) AS checksum;
                """)
            assert protocol(fake_ledger) == -1
            with psycopg.connect(fake_ledger) as c:
                assert c.execute("SELECT relkind FROM pg_catalog.pg_class WHERE oid=pg_catalog.to_regclass('public.schema_migration')").fetchone()[0] == 'v'
            print("OK: se rechaza un historial que es una vista sin ejecutarla.")

            rls_ledger = create()
            with psycopg.connect(rls_ledger) as c:
                c.execute("CREATE TABLE public.schema_migration(version integer, script_name text, checksum text); ALTER TABLE public.schema_migration ENABLE ROW LEVEL SECURITY;")
            assert protocol(rls_ledger) == -1
            print("OK: se rechaza un historial protegido con políticas de fila.")

            legacy = create()
            with psycopg.connect(legacy) as c:
                c.execute("""
                    CREATE TABLE public.schema_migration(version integer);
                    CREATE FUNCTION public.to_regclass(text) RETURNS pg_catalog.regclass
                      LANGUAGE plpgsql AS $$BEGIN RAISE EXCEPTION 'legacy shadow'; END$$;
                """)
            guard_source = (ROOT / "src/FrubberPOS.Infrastructure/Migrations/MigrationRunner.cs").read_text(encoding="utf-8")
            guard = re.findall(r'guard.CommandText = "([^"\n]+)";', guard_source)
            if len(guard) != 1:
                raise AssertionError("No se identificó el candado del migrador anterior.")
            hostile_legacy = psycopg.conninfo.make_conninfo(legacy, options="-c search_path=public,pg_catalog")
            with psycopg.connect(hostile_legacy) as c:
                assert c.execute(guard[0]).fetchone()[0]
                assert c.execute("SELECT count(*) FROM public.schema_migration").fetchone()[0] == 0
            print("OK: candado anterior detecta 2.0 sin ejecutar función homónima.")

            rollback = create()
            try:
                with psycopg.connect(rollback) as c:
                    c.execute(baseline)
                    c.execute("SELECT 1/0")
            except psycopg.errors.DivisionByZero:
                pass
            else:
                raise AssertionError("No ocurrió el fallo inyectado.")
            with psycopg.connect(rollback) as c:
                assert c.execute("SELECT to_regclass('public.schema_migration') IS NULL").fetchone()[0]
                assert c.execute("SELECT to_regclass('public.ticket_counter') IS NULL").fetchone()[0]
            print("OK: fallo antes de confirmar revierte esquema y registro.")
            print("Límite: protocolo SQL emulado; no se ejecutó el instalador C# ni xUnit.")
        finally:
            for name in created:
                admin.execute(sql.SQL("DROP DATABASE {} WITH (FORCE)").format(sql.Identifier(name)))
            print("Bases temporales eliminadas:", len(created))


if __name__ == "__main__":
    main()
