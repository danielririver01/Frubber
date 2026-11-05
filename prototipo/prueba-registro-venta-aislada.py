#!/usr/bin/env python3
"""JHO-111: prueba SECURITY DEFINER en un clúster PostgreSQL DESHABILITADO para producción.

ATENCIÓN: necesita un clúster completamente aislado para pruebas. Crea y borra
la base 'frubber' y roles del clúster; nunca lo ejecute en un servidor compartido.
Requiere psycopg 3 y FRUBBER_JHO111_ADMIN_DSN con credenciales administrativas.
"""
from concurrent.futures import ThreadPoolExecutor
import os
from pathlib import Path
import sys
import threading
import uuid

import psycopg
from psycopg import sql
from psycopg.conninfo import make_conninfo
from psycopg.types.json import Jsonb

ROOT = Path(__file__).resolve().parent.parent
CODES = ('A111111111111111', 'B222222222222222')
ROLES = tuple('puesto_' + code.lower() for code in CODES)
SALE = 'SELECT * FROM public.app_registrar_venta(%s,%s,%s,%s,%s,%s,%s)'


def expect(condition, message):
    if not condition:
        raise AssertionError(message)


def sale(conn, session_id, user_id, product, request=None, price=1500):
    product_id, plu = product
    lines = Jsonb([{'product_id': product_id, 'quantity_milli': 1000,
                    'unit_price_cop': price, 'plu': plu,
                    'name': 'Producto de prueba', 'unit': 'UNIT'}])
    return conn.execute(SALE, (session_id, user_id, 0, 'CASH', 1500,
                               request or uuid.uuid4(), lines)).fetchone()


def expect_failure(conn, callback, expected):
    try:
        callback()
    except expected:
        return
    raise AssertionError('La operación que debía fallar fue aceptada')


def main():
    admin_dsn = os.environ.get('FRUBBER_JHO111_ADMIN_DSN')
    if not admin_dsn:
        raise RuntimeError('Defina FRUBBER_JHO111_ADMIN_DSN para el clúster aislado')
    roles_created = []
    db_created = False
    postgres_created = False
    with psycopg.connect(make_conninfo(admin_dsn, dbname='postgres'), autocommit=True) as admin:
        # No toca un clúster que ya tenga una base de negocio o nuestros roles.
        dbs = admin.execute("SELECT datname FROM pg_database WHERE datname NOT IN ('postgres','template0','template1')").fetchall()
        roles = admin.execute("SELECT rolname FROM pg_roles WHERE rolname = 'frubber_puesto' OR rolname = ANY(%s)", (list(ROLES),)).fetchall()
        expect(not dbs and not roles, 'El clúster no está vacío: prueba cancelada sin cambios')
        try:
            postgres_exists = admin.execute("SELECT 1 FROM pg_roles WHERE rolname='postgres'").fetchone()
            if not postgres_exists:
                admin.execute('CREATE ROLE postgres NOLOGIN')
                postgres_created = True
            admin.execute('CREATE DATABASE frubber TEMPLATE template0')
            db_created = True
            dsn = make_conninfo(admin_dsn, dbname='frubber')
            with psycopg.connect(dsn, autocommit=True) as c:
                for name in ('001_Baseline.sql', '002_Security.sql', '003_RegistroVenta.sql'):
                    c.execute((ROOT / 'database/postgresql' / name).read_text(encoding='utf-8'))
                passwords = []
                for i, code in enumerate(CODES):
                    role, password = c.execute('SELECT * FROM public.register_terminal(%s,%s)',
                                               (f'Puesto de prueba {i + 1}', code)).fetchone()
                    expect(role == ROLES[i], 'Rol creado inesperado')
                    roles_created.append(role)
                    passwords.append(password)
                user_id, = c.execute('''INSERT INTO app_user(display_name,role,is_active,pin_salt,pin_hash,pin_iterations,pin_algorithm_version)
                    VALUES ('Cajero prueba','CASHIER',true,%s,%s,210000,1) RETURNING user_id''',
                    (bytes(16), bytes(32))).fetchone()
                products = []
                for plu in ('PRUEBA29A', 'PRUEBA29B'):
                    product_id, = c.execute('''INSERT INTO product(plu,name,unit,price_cop,stock_milli,is_active,unit_locked)
                        VALUES (%s,'Producto de prueba','UNIT',1500,1000000,true,true) RETURNING product_id''', (plu,)).fetchone()
                    products.append((product_id, plu))
                sessions = []
                for code in CODES:
                    sid, = c.execute('''INSERT INTO cash_session(terminal_id,opened_by_user_id,opened_by_name_snapshot,opening_balance_cop)
                        SELECT terminal_id,%s,'Cajero prueba',0 FROM terminal WHERE machine_code=%s RETURNING cash_session_id''',
                        (user_id, code)).fetchone()
                    sessions.append(sid)
                expect(not c.execute("SELECT has_function_privilege('public', 'public.app_registrar_venta(bigint,bigint,bigint,text,bigint,uuid,jsonb)', 'EXECUTE')").fetchone()[0], 'PUBLIC puede ejecutar la función')

            role_dsns = [make_conninfo(dsn, user=role, password=password)
                         for role, password in zip(ROLES, passwords)]
            with psycopg.connect(role_dsns[0], autocommit=True) as one:
                expect_failure(one, lambda: one.execute('INSERT INTO sale DEFAULT VALUES'), psycopg.errors.InsufficientPrivilege)
                expect_failure(one, lambda: one.execute('SELECT * FROM cash_session'), psycopg.errors.InsufficientPrivilege)
                expect_failure(one, lambda: one.execute('UPDATE ticket_counter SET last_number=0'), psycopg.errors.InsufficientPrivilege)
                expect_failure(one, lambda: one.execute('SELECT next_ticket()'), psycopg.errors.InsufficientPrivilege)
                expect_failure(one, lambda: sale(one, sessions[1], user_id, products[1]), psycopg.errors.CheckViolation)
                expect_failure(one, lambda: sale(one, sessions[0], user_id, products[0], price=1), psycopg.errors.CheckViolation)
                request = uuid.uuid4()
                expect(sale(one, sessions[0], user_id, products[0], request)[1] == 1, 'Primer número incorrecto')
                expect_failure(one, lambda: sale(one, sessions[0], user_id, products[0], request), psycopg.errors.UniqueViolation)
                expect(sale(one, sessions[0], user_id, products[0])[1] == 2, 'Repetición consumió un número')
            print('OK: permisos mínimos, caja de otro puesto y precio manipulado rechazados.')
            print('OK: solicitud repetida rechazada sin consumir un número; reintento idempotente pendiente.')

            with psycopg.connect(dsn, autocommit=True) as c:
                c.execute('''CREATE FUNCTION jho111_fail_line() RETURNS trigger LANGUAGE plpgsql AS $$
                    BEGIN RAISE EXCEPTION 'fallo de prueba' USING ERRCODE='23514'; END $$;
                    CREATE TRIGGER jho111_fail_line BEFORE INSERT ON sale_line
                    FOR EACH ROW EXECUTE FUNCTION jho111_fail_line()''')
            with psycopg.connect(role_dsns[0], autocommit=True) as one:
                expect_failure(one, lambda: sale(one, sessions[0], user_id, products[0]), psycopg.errors.CheckViolation)
            with psycopg.connect(dsn, autocommit=True) as c:
                expect(c.execute("SELECT last_number FROM ticket_counter WHERE scope='venta'").fetchone()[0] == 2, 'Rollback dejó un hueco')
                expect(c.execute('SELECT count(*) FROM sale').fetchone()[0] == 2, 'Rollback dejó una venta parcial')
                c.execute('DROP TRIGGER jho111_fail_line ON sale_line; DROP FUNCTION jho111_fail_line()')
            with psycopg.connect(role_dsns[0], autocommit=True) as one:
                expect(sale(one, sessions[0], user_id, products[0])[1] == 3, 'Número no reutilizado')
            print('OK: fallo tras insertar cabecera revierte la venta y reutiliza el número 3.')

            barrier = threading.Barrier(10, timeout=30)
            def worker(i):
                with psycopg.connect(role_dsns[i % 2], autocommit=True) as conn:
                    pid, = conn.execute('SELECT pg_backend_pid()').fetchone()
                    barrier.wait()
                    return pid, [sale(conn, sessions[i % 2], user_id, products[i % 2])[1] for _ in range(50)]
            with ThreadPoolExecutor(max_workers=10) as pool:
                results = list(pool.map(worker, range(10)))
            expect(len({pid for pid, _ in results}) == 10, 'No hubo diez conexiones independientes')
            expect(sorted(n for _, numbers in results for n in numbers) == list(range(4, 504)), 'Huecos o duplicados en concurrencia')
            with psycopg.connect(dsn, autocommit=True) as c:
                expect(c.execute('SELECT count(*), count(DISTINCT sale_number), max(sale_number) FROM sale').fetchone() == (503,503,503), 'Ventas incoherentes')
                expect(c.execute('SELECT count(*) FROM sale_line').fetchone()[0] == 503, 'Líneas incoherentes')
                expect(c.execute("SELECT last_number FROM ticket_counter WHERE scope='venta'").fetchone()[0] == 503, 'Contador incoherente')
                stocks = [c.execute('SELECT stock_milli FROM product WHERE product_id=%s', (product[0],)).fetchone()[0] for product in products]
                expect(stocks == [747000, 750000], 'Inventario incoherente')
            print('OK: 500 ventas concurrentes, dos puestos y diez conexiones: números 4 a 503, sin huecos ni duplicados.')
            print('OK: 503 ventas, 503 líneas, contador 503 e inventario correcto.')

            # Dos puestos intentan el mismo identificador a la vez. Esta
            # versión rechaza el segundo intento; JHO-114 implementará la
            # recuperación de la venta ya confirmada.
            shared_request = uuid.uuid4()
            retry_barrier = threading.Barrier(2, timeout=30)
            def duplicate_worker(i):
                with psycopg.connect(role_dsns[i], autocommit=True) as conn:
                    retry_barrier.wait()
                    try:
                        return sale(conn, sessions[i], user_id, products[i], shared_request)[1]
                    except psycopg.errors.UniqueViolation:
                        return None
            with ThreadPoolExecutor(max_workers=2) as pool:
                duplicate_results = list(pool.map(duplicate_worker, range(2)))
            expect(sorted(n for n in duplicate_results if n is not None) == [504], 'Solicitud concurrente duplicada o con hueco')
            expect(duplicate_results.count(None) == 1, 'La segunda solicitud no fue rechazada')
            with psycopg.connect(dsn, autocommit=True) as c:
                expect(c.execute("SELECT last_number FROM ticket_counter WHERE scope='venta'").fetchone()[0] == 504, 'La repetición concurrente consumió un número')
                expect(c.execute('SELECT count(*) FROM sale').fetchone()[0] == 504, 'La repetición concurrente duplicó la venta')
            print('OK: dos solicitudes simultáneas con el mismo identificador crean una sola venta y ningún hueco.')
        finally:
            if db_created:
                admin.execute('DROP DATABASE frubber WITH (FORCE)')
            # register_terminal crea roles de clúster; no sobrevivirán a la prueba.
            for role in roles_created:
                admin.execute(sql.SQL('DROP ROLE {}').format(sql.Identifier(role)))
            if db_created:
                admin.execute('DROP ROLE IF EXISTS frubber_puesto')
            if postgres_created:
                admin.execute('DROP ROLE postgres')
            if db_created:
                print('Base y roles temporales eliminados.')


if __name__ == '__main__':
    try:
        main()
    except Exception as exc:
        print(f'FALLO: {type(exc).__name__}', file=sys.stderr)
        if isinstance(exc, AssertionError):
            print(str(exc), file=sys.stderr)
        sys.exit(1)
