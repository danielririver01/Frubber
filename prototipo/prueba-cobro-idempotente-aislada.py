#!/usr/bin/env python3
"""JHO-114 · banco destructivo SOLO en clúster PostgreSQL desechable y dedicado.

Crea/elimina una base llamada frubber y roles globales de prueba. El guion 002
puede alterar roles de clúster, por lo que nunca debe usarse en un servidor
compartido o con datos reales. Requiere psycopg 3, superusuario de pruebas y
FRUBBER_JHO114_ISOLATED=1. No ejecuta la aplicación .NET ni prueba PIN/licencia.
"""
from concurrent.futures import ThreadPoolExecutor
import os
from pathlib import Path
import select
import socket
import threading
import uuid

import psycopg
from psycopg import sql
from psycopg.conninfo import make_conninfo
from psycopg.types.json import Jsonb

ROOT = Path(__file__).resolve().parent.parent
CODES = ('A111111111111111', 'B222222222222222')
ROLES = tuple('puesto_' + code.lower() for code in CODES)
FORBIDDEN = ('frubber_puesto', 'frubber_puesto_test', *ROLES)
SALE = 'SELECT * FROM public.app_cobrar_idempotente(%s,%s,%s,%s,%s,%s,%s)'
ORIGINAL = 'SELECT * FROM public.app_registrar_venta(%s,%s,%s,%s,%s,%s,%s)'
RECEIPT = 'SELECT * FROM public.app_comprobante_por_intento(%s)'


def require(condition, message):
    if not condition:
        raise AssertionError(message)


def denied(callback, expected):
    try:
        callback()
    except expected:
        return
    raise AssertionError('La operación que debía rechazarse fue aceptada')


def payload(session, cashier, product, request, price=1500):
    product_id, plu = product
    lines = [{'product_id': product_id, 'quantity_milli': 1000, 'unit_price_cop': price,
              'plu': plu, 'name': 'Producto de prueba', 'unit': 'UNIT'}]
    return (session, cashier, 0, 'CASH', 1500, request, Jsonb(lines))


def sale(conn, arguments):
    return conn.execute(SALE, arguments).fetchone()


def lost_commit(role_dsn, target_port, arguments):
    """Descarta la respuesta de COMMIT después de que PostgreSQL la emite."""
    listener = socket.socket()
    listener.bind(('127.0.0.1', 0))
    listener.listen(1)
    listener.settimeout(10)
    port = listener.getsockname()[1]
    seen, dropped = threading.Event(), threading.Event()
    errors = []

    def proxy():
        try:
            client, _ = listener.accept()
            server = socket.create_connection(('127.0.0.1', target_port), timeout=10)
            with client, server:
                client.setblocking(False)
                server.setblocking(False)
                front, back = b'', b''
                while True:
                    ready, _, _ = select.select([client, server], [], [], 10)
                    if not ready:
                        raise TimeoutError('Tiempo agotado en proxy de prueba')
                    for origin in ready:
                        try:
                            data = origin.recv(65536)
                        except BlockingIOError:
                            continue
                        if not data:
                            return
                        if origin is client:
                            front = (front + data)[-65536:]
                            if b'COMMIT\x00' in front:
                                seen.set()
                            server.sendall(data)
                        elif seen.is_set():
                            back = (back + data)[-65536:]
                            if b'COMMIT\x00' in back:
                                dropped.set()
                                return
                        else:
                            client.sendall(data)
        except Exception as exc:
            errors.append(type(exc).__name__)
        finally:
            listener.close()

    thread = threading.Thread(target=proxy, daemon=True)
    thread.start()
    proxy_dsn = make_conninfo(role_dsn, host='127.0.0.1', port=port, sslmode='disable', connect_timeout=10)
    result = None
    try:
        try:
            with psycopg.connect(proxy_dsn) as connection:
                result = sale(connection, arguments)
                connection.commit()
        except psycopg.OperationalError:
            pass
        else:
            raise AssertionError('El corte de conexión no ocurrió durante COMMIT')
        thread.join(12)
        require(not thread.is_alive() and not errors and seen.is_set() and dropped.is_set(),
                'El proxy no observó la respuesta de COMMIT perdida')
        return result
    finally:
        listener.close()
        thread.join(12)


def main():
    if os.environ.get('FRUBBER_JHO114_ISOLATED') != '1':
        raise SystemExit('Solo clúster desechable: configure FRUBBER_JHO114_ISOLATED=1')
    admin_dsn = os.environ.get('FRUBBER_JHO114_ADMIN_DSN')
    if not admin_dsn:
        raise SystemExit('Falta FRUBBER_JHO114_ADMIN_DSN para el clúster de prueba')
    created_db = False
    created_postgres = False
    roles_created = []
    with psycopg.connect(make_conninfo(admin_dsn, dbname='postgres'), autocommit=True) as admin:
        require(admin.info.host in ('127.0.0.1', 'localhost'), 'Solo se admite un clúster de prueba local')
        require(admin.execute('SELECT rolsuper FROM pg_catalog.pg_roles WHERE rolname=current_user').fetchone()[0],
                'El administrador de prueba necesita ser superusuario')
        dbs = admin.execute("SELECT datname FROM pg_database WHERE datname NOT IN ('postgres','template0','template1')").fetchall()
        roles = admin.execute('SELECT rolname FROM pg_roles WHERE rolname = ANY(%s)', (list(FORBIDDEN),)).fetchall()
        require(not dbs and not roles, 'Clúster no vacío o roles ya existentes: cancelado sin cambios')
        print('PostgreSQL', admin.execute('SHOW server_version').fetchone()[0])
        try:
            if not admin.execute("SELECT 1 FROM pg_roles WHERE rolname='postgres'").fetchone():
                admin.execute('CREATE ROLE postgres NOLOGIN')
                created_postgres = True
            admin.execute('CREATE DATABASE frubber TEMPLATE template0')
            created_db = True
            dsn = make_conninfo(admin_dsn, dbname='frubber')
            with psycopg.connect(dsn, autocommit=True) as c:
                for name in ('001_Baseline.sql', '002_Security.sql', '003_RegistroVenta.sql'):
                    c.execute((ROOT / 'database/postgresql' / name).read_text(encoding='utf-8'))
                for i, code in enumerate(CODES):
                    role, password = c.execute('SELECT * FROM public.register_terminal(%s,%s)', (f'Puesto {i+1}', code)).fetchone()
                    require(role == ROLES[i], 'Identidad de puesto inesperada')
                    roles_created.append(role)
                    if i == 0:
                        passwords = []
                    passwords.append(password)
                user_id, = c.execute("""INSERT INTO public.app_user(display_name,role,is_active,pin_salt,pin_hash,pin_iterations,pin_algorithm_version)
                    VALUES ('Cajero prueba','CASHIER',true,%s,%s,210000,1) RETURNING user_id""", (bytes(16), bytes(32))).fetchone()
                products = []
                for plu in ('PRUEBA114A', 'PRUEBA114B'):
                    pid, = c.execute("""INSERT INTO public.product(plu,name,unit,price_cop,stock_milli,is_active,unit_locked)
                        VALUES (%s,'Producto de prueba','UNIT',1500,1000000,true,true) RETURNING product_id""", (plu,)).fetchone()
                    products.append((pid, plu))
                sessions = []
                for code in CODES:
                    sid, = c.execute("""INSERT INTO public.cash_session(terminal_id,opened_by_user_id,opened_by_name_snapshot,opening_balance_cop)
                        SELECT terminal_id,%s,'Cajero prueba',0 FROM public.terminal WHERE machine_code=%s RETURNING cash_session_id""", (user_id, code)).fetchone()
                    sessions.append(sid)
            role_dsns = [make_conninfo(dsn, user=role, password=password) for role, password in zip(ROLES, passwords)]
            script = (ROOT / 'database/postgresql/004_CobroIdempotente.sql').read_text(encoding='utf-8')

            # Probe migration guard in a transaction; the pre-existing synthetic
            # sale is rolled back after proving that neither it nor a guard changes.
            with psycopg.connect(dsn) as c:
                c.execute("""INSERT INTO public.sale(sale_number,cash_session_id,cashier_user_id,cashier_name_snapshot,
                    subtotal_cop,discount_cop,total_cop,payment_method,received_cop,change_cop,client_request_id)
                    VALUES(1,%s,%s,'Cajero prueba',1500,0,1500,'CASH',1500,0,%s)""", (sessions[0], user_id, uuid.uuid4()))
                c.execute('SAVEPOINT preexisting_sale')
                denied(lambda: c.execute(script), psycopg.errors.ObjectNotInPrerequisiteState)
                c.execute('ROLLBACK TO SAVEPOINT preexisting_sale')
                require(c.execute('SELECT count(*) FROM public.sale').fetchone()[0] == 1, 'Se alteró la venta preexistente')
                require(c.execute("SELECT pg_catalog.to_regclass('public.sale_request_guard') IS NULL").fetchone()[0], 'Quedó esquema parcial')
                c.rollback()
            with psycopg.connect(dsn) as c:
                c.execute(script)
            print('OK: rechaza instalación en una base con ventas sin alterar esa venta.')

            first_id = uuid.uuid4()
            args = payload(sessions[0], user_id, products[0], first_id)
            with psycopg.connect(role_dsns[0], autocommit=True) as one:
                for statement in ('SELECT * FROM public.sale_request_guard', 'INSERT INTO public.sale_request_guard DEFAULT VALUES', 'INSERT INTO public.sale DEFAULT VALUES',
                                  'UPDATE public.ticket_counter SET last_number=0', 'SELECT * FROM public.app_user'):
                    denied(lambda s=statement: one.execute(s), psycopg.errors.InsufficientPrivilege)
                denied(lambda: one.execute(ORIGINAL, args), psycopg.errors.InsufficientPrivilege)
                rejected = uuid.uuid4()
                bad_lines = Jsonb([{'product_id': products[0][0], 'quantity_milli': 1000,
                                    'unit_price_cop': 1500, 'plu': products[0][1],
                                    'name': 'Producto de prueba', 'unit': 'UNIT', 'pin': 'no-guardar'}])
                bad_args = (sessions[0], user_id, 0, 'CASH', 1500, rejected, bad_lines)
                denied(lambda: sale(one, bad_args), psycopg.errors.CheckViolation)
                denied(lambda: sale(one, (sessions[0], user_id, 0, 'CASH', 1500, uuid.uuid4(), Jsonb([123]))), psycopg.errors.CheckViolation)
                first = sale(one, args)
                require(first[1:] == (1, 1500), 'Primer cobro incorrecto')
                require(sale(one, args) == first, 'El reintento no recuperó la venta')
                receipt = one.execute(RECEIPT, (first_id,)).fetchone()
                require(receipt[:2] == first[:2] and receipt[3:10] ==
                        ('Cajero prueba', 1500, 0, 1500, 'CASH', 1500, 0), 'Cabecera de comprobante incorrecta')
                require(receipt[2] is not None and receipt[10] == [
                    {'line_number': 1, 'name': 'Producto de prueba', 'unit': 'UNIT',
                     'quantity_milli': 1000, 'unit_price_cop': 1500, 'line_total_cop': 1500}],
                    'Líneas históricas del comprobante incorrectas')
                require(one.execute(RECEIPT, (uuid.uuid4(),)).fetchone() is None, 'Intento desconocido retornó comprobante')
                reordered = Jsonb([{'unit': 'UNIT', 'name': 'Producto de prueba', 'plu': products[0][1],
                                    'unit_price_cop': 1500, 'quantity_milli': 1000, 'product_id': products[0][0]}])
                require(sale(one, (*args[:6], reordered)) == first, 'Orden JSON alteró el reintento')
                denied(lambda: sale(one, (*args[:5], None, args[6])), psycopg.errors.CheckViolation)
                denied(lambda: sale(one, payload(sessions[0], user_id, products[0], first_id, price=1)), psycopg.errors.InsufficientPrivilege)
            with psycopg.connect(role_dsns[1], autocommit=True) as other:
                denied(lambda: sale(other, args), psycopg.errors.InsufficientPrivilege)
                require(other.execute(RECEIPT, (first_id,)).fetchone() is None, 'Otro puesto recibió el comprobante')
                denied(lambda: other.execute(ORIGINAL, args), psycopg.errors.InsufficientPrivilege)
                denied(lambda: other.execute('SELECT * FROM public.sale_request_guard'), psycopg.errors.InsufficientPrivilege)
            with psycopg.connect(dsn, autocommit=True) as c:
                require(c.execute('SELECT count(*) FROM public.sale').fetchone()[0] == 1, 'Se duplicó una venta')
                require(c.execute('SELECT count(*) FROM public.sale_request_guard WHERE client_request_id=%s', (rejected,)).fetchone()[0] == 0, 'Se guardaron datos no permitidos')
                require(c.execute('SELECT stock_milli FROM public.product WHERE product_id=%s', (products[0][0],)).fetchone()[0] == 999000, 'Se duplicó descuento de inventario')
                c.execute('UPDATE public.product SET price_cop=2000 WHERE product_id=%s', (products[0][0],))
            with psycopg.connect(role_dsns[0], autocommit=True) as one:
                require(sale(one, args) == first, 'El cambio de precio impidió recuperar el comprobante')
                require(one.execute(RECEIPT, (first_id,)).fetchone() == receipt, 'El comprobante cambió con el catálogo')
            with psycopg.connect(dsn, autocommit=True) as c:
                c.execute('UPDATE public.product SET price_cop=1500 WHERE product_id=%s', (products[0][0],))
            print('OK: reintento idéntico, permisos mínimos y rechazo de cambios/otro puesto.')

            failed_request = uuid.uuid4()
            failed_args = payload(sessions[0], user_id, products[0], failed_request)
            # A transaction that receives a provisional response but never
            # commits must not reserve the request or consume its ticket.
            with psycopg.connect(role_dsns[0]) as one:
                require(sale(one, failed_args)[1] == 2, 'Número provisional incorrecto')
                one.rollback()
            with psycopg.connect(dsn, autocommit=True) as c:
                require(c.execute('SELECT count(*) FROM public.sale_request_guard WHERE client_request_id=%s', (failed_request,)).fetchone()[0] == 0, 'Rollback dejó intento')
                require(c.execute("SELECT last_number FROM public.ticket_counter WHERE scope='venta'").fetchone()[0] == 1, 'Rollback consumió ticket')
            with psycopg.connect(dsn, autocommit=True) as c:
                c.execute("""CREATE FUNCTION public.jho114_fail_line() RETURNS trigger LANGUAGE plpgsql AS $$
                    BEGIN RAISE EXCEPTION 'fallo de prueba' USING ERRCODE='23514'; END $$;
                    CREATE TRIGGER jho114_fail_line BEFORE INSERT ON public.sale_line
                    FOR EACH ROW EXECUTE FUNCTION public.jho114_fail_line()""")
            with psycopg.connect(role_dsns[0], autocommit=True) as one:
                denied(lambda: sale(one, failed_args), psycopg.errors.CheckViolation)
            with psycopg.connect(dsn, autocommit=True) as c:
                require(c.execute('SELECT count(*) FROM public.sale_request_guard WHERE client_request_id=%s', (failed_request,)).fetchone()[0] == 0, 'Intento fallido persistió')
                require(c.execute("SELECT last_number FROM public.ticket_counter WHERE scope='venta'").fetchone()[0] == 1, 'Rollback consumió un número')
                c.execute('DROP TRIGGER jho114_fail_line ON public.sale_line; DROP FUNCTION public.jho114_fail_line()')
            started, finished = threading.Event(), threading.Event()
            def waiting_retry():
                with psycopg.connect(role_dsns[0], autocommit=True) as retry:
                    started.set()
                    try:
                        return sale(retry, failed_args)
                    finally:
                        finished.set()
            with psycopg.connect(role_dsns[0]) as first_connection:
                require(sale(first_connection, failed_args)[1] == 2, 'Número provisional incorrecto')
                with ThreadPoolExecutor(max_workers=1) as pool:
                    pending = pool.submit(waiting_retry)
                    require(started.wait(10), 'No se inició el competidor')
                    require(not finished.wait(0.3), 'El segundo intento no esperó la transacción anterior')
                    first_connection.rollback()
                    require(pending.result(timeout=30)[1] == 2, 'No se reutilizó el número tras el rollback')
            print('OK: rollback y fallo de línea revierten intento y venta; competidor espera y crea número 2.')

            shared = uuid.uuid4()
            shared_args = payload(sessions[0], user_id, products[0], shared)
            barrier = threading.Barrier(10, timeout=30)
            def same_worker(_):
                with psycopg.connect(role_dsns[0], autocommit=True) as c:
                    pid, = c.execute('SELECT pg_backend_pid()').fetchone()
                    barrier.wait()
                    return pid, sale(c, shared_args)
            with ThreadPoolExecutor(max_workers=10) as pool:
                same_results = list(pool.map(same_worker, range(10)))
            require(len({pid for pid, _ in same_results}) == 10, 'No fueron 10 conexiones')
            require(len({row for _, row in same_results}) == 1 and same_results[0][1][1] == 3, 'Reintentos simultáneos no coinciden')
            print('OK: diez conexiones con UUID idéntico recuperan una sola venta.')

            # Race two distinct terminals with the same UUID: exactly one may
            # own it; the other must fail without recovering the other sale.
            contested = uuid.uuid4()
            two = threading.Barrier(2, timeout=30)
            def race(i):
                with psycopg.connect(role_dsns[i], autocommit=True) as c:
                    two.wait()
                    try:
                        return sale(c, payload(sessions[i], user_id, products[i], contested))
                    except psycopg.errors.InsufficientPrivilege:
                        return None
            with ThreadPoolExecutor(max_workers=2) as pool:
                race_results = list(pool.map(race, range(2)))
            require(sum(row is not None for row in race_results) == 1, 'La competencia entre puestos no se resolvió correctamente')
            require(next(row for row in race_results if row is not None)[1] == 4, 'Número de competencia incorrecto')
            print('OK: dos puestos compiten por un UUID: uno gana y el otro no recibe datos.')

            barrier500 = threading.Barrier(10, timeout=30)
            def worker(i):
                with psycopg.connect(role_dsns[i % 2], autocommit=True) as c:
                    pid, = c.execute('SELECT pg_backend_pid()').fetchone()
                    barrier500.wait()
                    return pid, [sale(c, payload(sessions[i % 2], user_id, products[i % 2], uuid.uuid4()))[1] for _ in range(50)]
            with ThreadPoolExecutor(max_workers=10) as pool:
                results = list(pool.map(worker, range(10)))
            require(len({pid for pid, _ in results}) == 10, 'No hubo diez conexiones')
            require(sorted(n for _, numbers in results for n in numbers) == list(range(5, 505)), 'Números concurrentes con huecos o duplicados')
            print('OK: 500 ventas en dos puestos y diez conexiones: números 5–504 sin huecos.')

            cut_id = uuid.uuid4()
            cut_args = payload(sessions[0], user_id, products[0], cut_id)
            observed = lost_commit(role_dsns[0], admin.info.port, cut_args)
            with psycopg.connect(role_dsns[0], autocommit=True) as one:
                recovered = sale(one, cut_args)
            require(observed == recovered and recovered[1] == 505, 'No se recuperó el cobro tras el corte')
            with psycopg.connect(dsn, autocommit=True) as c:
                require(c.execute('SELECT count(*), count(DISTINCT sale_number), max(sale_number) FROM public.sale').fetchone() == (505, 505, 505), 'Ventas finales incorrectas')
                require(c.execute('SELECT count(*), count(sale_id) FROM public.sale_request_guard').fetchone() == (505, 505), 'Intentos incompletos o duplicados')
                require(c.execute('SELECT count(*) FROM public.sale_line').fetchone()[0] == 505, 'Líneas finales incorrectas')
                require(c.execute("SELECT last_number FROM public.ticket_counter WHERE scope='venta'").fetchone()[0] == 505, 'Contador final incorrecto')
                for pid, _ in products:
                    n, stock = c.execute('SELECT (SELECT count(*) FROM public.sale_line WHERE product_id=%s), stock_milli FROM public.product WHERE product_id=%s', (pid, pid)).fetchone()
                    require(stock == 1000000 - 1000 * n, 'Inventario final incorrecto')
            print('OK: corte real TCP en COMMIT, reintento devuelve la misma venta 505.')
            print('OK: 505 ventas, líneas e intentos completos; contador e inventarios coherentes.')

            # Check ordered historical lines on the receipt without changing the
            # final state: this multi-line sale is deliberately rolled back.
            multiline_id = uuid.uuid4()
            multiline = Jsonb([{'product_id': pid, 'quantity_milli': quantity, 'unit_price_cop': 1500,
                                'plu': plu, 'name': 'Producto de prueba', 'unit': 'UNIT'}
                               for (pid, plu), quantity in zip(reversed(products), (2000, 1000))])
            with psycopg.connect(role_dsns[1]) as one:
                row = sale(one, (sessions[1], user_id, 0, 'CASH', 4500, multiline_id, multiline))
                ticket = one.execute(RECEIPT, (multiline_id,)).fetchone()
                require(row[1:] == (506, 4500), 'Venta temporal multilínea incorrecta')
                require(ticket[10] == [{'line_number': i, 'name': 'Producto de prueba', 'unit': 'UNIT',
                                         'quantity_milli': qty, 'unit_price_cop': 1500, 'line_total_cop': total}
                                        for i, qty, total in ((1, 2000, 3000), (2, 1000, 1500))], 'Orden de líneas históricas incorrecto')
                one.rollback()
            print('OK: comprobante histórico de dos líneas, orden conservado y puesto distinto sin acceso.')

            # A closed cash session must still permit recovery of its receipt,
            # but not a new sale. A deactivated terminal cannot recover it.
            with psycopg.connect(dsn, autocommit=True) as c:
                c.execute("""UPDATE public.cash_session SET closed_at=now(), closed_by_user_id=%s,
                    closed_by_name_snapshot='Cajero prueba',
                    closed_cash_sales_cop=(SELECT coalesce(sum(total_cop),0) FROM public.sale WHERE cash_session_id=%s),
                    closed_transfer_sales_cop=0, closed_discount_cop=0,
                    closed_net_sales_cop=(SELECT coalesce(sum(total_cop),0) FROM public.sale WHERE cash_session_id=%s),
                    closed_expected_cash_cop=(SELECT coalesce(sum(total_cop),0) FROM public.sale WHERE cash_session_id=%s),
                    closed_sale_count=(SELECT count(*) FROM public.sale WHERE cash_session_id=%s)
                    WHERE cash_session_id=%s""", (user_id, *([sessions[0]] * 5)))
            with psycopg.connect(role_dsns[0], autocommit=True) as one:
                require(sale(one, cut_args) == recovered, 'Caja cerrada impidió recuperación')
                require(one.execute(RECEIPT, (cut_id,)).fetchone()[:2] == recovered[:2], 'Caja cerrada impidió consultar comprobante')
                denied(lambda: sale(one, payload(sessions[0], user_id, products[0], uuid.uuid4())), psycopg.errors.CheckViolation)
                with psycopg.connect(dsn, autocommit=True) as c:
                    c.execute('UPDATE public.terminal SET is_active=false WHERE machine_code=%s', (CODES[0],))
                denied(lambda: sale(one, cut_args), psycopg.errors.InsufficientPrivilege)
                denied(lambda: one.execute(RECEIPT, (cut_id,)), psycopg.errors.InsufficientPrivilege)
            with psycopg.connect(dsn, autocommit=True) as c:
                require(c.execute('SELECT count(*) FROM public.sale').fetchone()[0] == 505, 'Caja cerrada o puesto inactivo alteraron ventas')
                require(c.execute('SELECT count(*) FROM public.sale_request_guard').fetchone()[0] == 505, 'Fallos dejaron intentos')
            print('OK: recupera con caja cerrada; bloquea venta nueva y recuperación por puesto inactivo.')
            print('LÍMITE: prueba SQL con psycopg, no aplicación, PIN autenticado ni licencia.')
        finally:
            if created_db:
                admin.execute('DROP DATABASE frubber WITH (FORCE)')
            for role in roles_created:
                admin.execute(sql.SQL('DROP ROLE {}').format(sql.Identifier(role)))
            # El rol no existía antes del banco y fue creado por 002 solo si se
            # aplicó. No se eliminan roles que existieran antes de la preprueba.
            if admin.execute("SELECT 1 FROM pg_roles WHERE rolname='frubber_puesto'").fetchone():
                admin.execute('DROP ROLE frubber_puesto')
            if created_postgres:
                admin.execute('DROP ROLE postgres')
            print('Base y roles temporales eliminados.')


if __name__ == '__main__':
    main()
