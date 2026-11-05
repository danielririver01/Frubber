#!/usr/bin/env python3
"""JHO-111: ensaya la línea base 2.0 en una base temporal propia.

La conexión administrativa se lee de FRUBBER_JHO111_ADMIN_DSN. Requiere
psycopg 3; nunca imprime el DSN. No ejecutar contra un servidor de producción.
"""
import concurrent.futures
import multiprocessing
import os
from pathlib import Path
import sys
import uuid

import psycopg
from psycopg import sql
from psycopg.conninfo import make_conninfo

ROOT = Path(__file__).resolve().parent.parent
WORKERS = 10
SALES_PER_WORKER = 50


def sell(conn, session_id, user_id, product_id, *, invalid_line=False):
    """El número, la cabecera y la línea comparten una transacción explícita."""
    with conn.transaction():
        number, = conn.execute('SELECT next_ticket()').fetchone()
        sale_id, = conn.execute('''
            INSERT INTO sale (sale_number, cash_session_id, cashier_user_id,
                cashier_name_snapshot, subtotal_cop, discount_cop, total_cop,
                payment_method, received_cop, change_cop, client_request_id)
            VALUES (%s, %s, %s, 'Cajero de prueba', 1500, 0, 1500,
                'CASH', 1500, 0, %s) RETURNING sale_id
        ''', (number, session_id, user_id, uuid.uuid4())).fetchone()
        conn.execute('''
            INSERT INTO sale_line (sale_id, line_number, product_id, plu_snapshot,
                product_name_snapshot, unit_snapshot, unit_price_cop_snapshot,
                quantity_milli, line_total_cop)
            VALUES (%s, 1, %s, 'VERIF29', 'Producto de prueba', 'KG', 1500, 1000, %s)
        ''', (sale_id, product_id, 0 if invalid_line else 1500))
    return number


def worker(dsn, session_id, user_id, product_id, barrier):
    # Las 10 conexiones y procesos existen al mismo tiempo antes de comenzar.
    with psycopg.connect(dsn, autocommit=True) as conn:
        pid, = conn.execute('SELECT pg_backend_pid()').fetchone()
        barrier.wait(timeout=30)
        numbers = [sell(conn, session_id, user_id, product_id)
                   for _ in range(SALES_PER_WORKER)]
        return pid, numbers


def assert_equal(actual, expected, description):
    if actual != expected:
        raise AssertionError(f'{description}: esperado {expected!r}, obtenido {actual!r}')


def check_original_prototype(conn):
    # Ejecuta el bloque 2 original sin los metacomandos de psql ni los bloques
    # de otras tareas, en un esquema separado dentro de la base desechable.
    source = (ROOT / 'prototipo/verificacion-postgresql.sql').read_text(encoding='utf-8')
    start = source.index('CREATE TABLE ticket_counter (')
    end = source.index("\\echo '  (la prueba de concurrencia", start)
    with conn.transaction():
        conn.execute('CREATE SCHEMA jho111_prototipo')
        conn.execute('SET LOCAL search_path TO jho111_prototipo, public')
        conn.execute('CREATE TABLE resultado (n int PRIMARY KEY, estado text NOT NULL)')
        conn.execute("INSERT INTO resultado VALUES (2, 'pendiente')")
        conn.execute(source[start:end])
        result, = conn.execute('SELECT estado FROM resultado WHERE n = 2').fetchone()
        assert_equal(result, 'OK', 'Bloque 2 del prototipo')
    print('OK: bloque 2 del prototipo original (100 tickets y rollback).', flush=True)


def run(dsn):
    with psycopg.connect(dsn, autocommit=True) as conn:
        version, = conn.execute('SHOW server_version').fetchone()
        print(f'PostgreSQL {version}', flush=True)
        check_original_prototype(conn)
        with conn.transaction():
            conn.execute((ROOT / 'database/postgresql/001_Baseline.sql').read_text(encoding='utf-8'))
            conn.execute((ROOT / 'prototipo/seed-concurrencia-postgresql.sql').read_text(encoding='utf-8'))
        user_id, = conn.execute("SELECT user_id FROM app_user WHERE display_name = 'Cajero de prueba'").fetchone()
        product_id, = conn.execute("SELECT product_id FROM product WHERE plu = 'VERIF29'").fetchone()
        sessions = [row[0] for row in conn.execute('SELECT cash_session_id FROM cash_session ORDER BY cash_session_id')]
        assert_equal(len(sessions), 2, 'Cajas abiertas')

        initial = [sell(conn, sessions[0], user_id, product_id) for _ in range(100)]
        assert_equal(initial, list(range(1, 101)), '100 tickets consecutivos')
        print('OK: 100 ventas consecutivas, tickets 1 a 100, cero huecos.', flush=True)

        try:
            sell(conn, sessions[0], user_id, product_id, invalid_line=True)
        except psycopg.errors.CheckViolation:
            pass
        else:
            raise AssertionError('La venta inválida no falló')
        assert_equal(conn.execute("SELECT last_number FROM ticket_counter WHERE scope = 'venta'").fetchone()[0], 100, 'Contador tras rollback')
        assert_equal(conn.execute('SELECT count(*) FROM sale').fetchone()[0], 100, 'Ventas tras rollback')
        assert_equal(sell(conn, sessions[0], user_id, product_id), 101, 'Número reutilizado tras rollback')
        print('OK: venta fallida revertida; el siguiente ticket reutiliza el 101.', flush=True)

        context = multiprocessing.get_context('spawn')
        with context.Manager() as manager:
            barrier = manager.Barrier(WORKERS)
            with concurrent.futures.ProcessPoolExecutor(max_workers=WORKERS, mp_context=context) as executor:
                futures = [executor.submit(worker, dsn, sessions[i % 2], user_id, product_id, barrier)
                           for i in range(WORKERS)]
                results = [future.result(timeout=120) for future in futures]
        pids = {pid for pid, _ in results}
        numbers = [number for _, batch in results for number in batch]
        assert_equal(len(pids), WORKERS, 'Conexiones PostgreSQL independientes')
        assert_equal(sorted(numbers), list(range(102, 602)), '500 tickets concurrentes')
        count, unique, minimum, maximum = conn.execute('''
            SELECT count(*), count(DISTINCT sale_number), min(sale_number), max(sale_number)
            FROM sale WHERE sale_number >= 102
        ''').fetchone()
        assert_equal((count, unique, minimum, maximum), (500, 500, 102, 601), 'Ventas confirmadas')
        assert_equal(conn.execute('SELECT count(*) FROM sale_line').fetchone()[0], 601, 'Líneas confirmadas')
        assert_equal(conn.execute("SELECT last_number FROM ticket_counter WHERE scope = 'venta'").fetchone()[0], 601, 'Contador final')
        per_session = conn.execute('''
            SELECT cash_session_id, count(*) FROM sale WHERE sale_number >= 102
            GROUP BY cash_session_id ORDER BY cash_session_id
        ''').fetchall()
        assert_equal([count for _, count in per_session], [250, 250], 'Ventas por puesto')
        print('OK: 500 ventas concurrentes, 10 procesos y 10 conexiones, 250 por puesto.', flush=True)
        print('OK: 500 números distintos y consecutivos (102–601); 0 duplicados, 0 huecos.', flush=True)
        print('OK: 601 ventas, 601 líneas y contador final 601.', flush=True)


def main():
    admin_dsn = os.environ.get('FRUBBER_JHO111_ADMIN_DSN')
    if not admin_dsn:
        raise RuntimeError('Defina FRUBBER_JHO111_ADMIN_DSN para un servidor de pruebas aislado.')
    dbname = 'frubber_jho111_' + uuid.uuid4().hex
    created = False
    with psycopg.connect(admin_dsn, autocommit=True) as admin:
        try:
            admin.execute(sql.SQL('CREATE DATABASE {} TEMPLATE template0').format(sql.Identifier(dbname)))
            created = True
            run(make_conninfo(admin_dsn, dbname=dbname))
        finally:
            if created:
                admin.execute(sql.SQL('DROP DATABASE {} WITH (FORCE)').format(sql.Identifier(dbname)))
                print('Base temporal eliminada.', flush=True)


if __name__ == '__main__':
    try:
        main()
    except Exception as exc:
        # No imprimir cadena de conexión ni excepción de psycopg que pueda contener datos sensibles.
        print(f'FALLO: {type(exc).__name__}. Revise la configuración y los requisitos de la prueba.', file=sys.stderr)
        if isinstance(exc, AssertionError):
            print(str(exc), file=sys.stderr)
        sys.exit(1)
