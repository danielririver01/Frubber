#!/usr/bin/env python3
"""Demuestra un COMMIT confirmado con respuesta perdida (no ejecuta C#).

Solo para un clúster PostgreSQL desechable y dedicado en localhost. Abre un
proxy TCP local sin TLS, crea una base aleatoria y elimina solo esa base.
No usar con datos ni credenciales de producción. Requiere psycopg y CREATEDB.
"""
import hashlib
import os
from pathlib import Path
import select
import socket
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


def main():
    name = "frubber_jho136_" + uuid.uuid4().hex
    with psycopg.connect(DSN, autocommit=True) as admin:
        # The proxy explicitly disables TLS and is intentionally local-only.
        if admin.info.host not in ("127.0.0.1", "localhost"):
            raise SystemExit("El ensayo de corte solo admite un servidor desechable en localhost.")
        target_port = admin.info.port
        admin.execute(sql.SQL("CREATE DATABASE {}").format(sql.Identifier(name)))
        try:
            run_cut(name, target_port)
        finally:
            admin.execute(sql.SQL("DROP DATABASE {} WITH (FORCE)").format(sql.Identifier(name)))
            print("Base temporal eliminada.")


def run_cut(name, target_port):
    listener = socket.socket()
    listener.bind(("127.0.0.1", 0))
    listener.listen(1)
    listener.settimeout(10)
    proxy_port = listener.getsockname()[1]
    saw_commit = threading.Event()
    lost_ack = threading.Event()
    errors = []

    def proxy():
        try:
            client, _ = listener.accept()
            server = socket.create_connection(("127.0.0.1", target_port), timeout=10)
            with client, server:
                client.setblocking(False)
                server.setblocking(False)
                frontend = b""
                backend = b""
                while True:
                    ready, _, _ = select.select([client, server], [], [], 10)
                    if not ready:
                        raise TimeoutError("Tiempo agotado esperando al proxy de prueba.")
                    for origin in ready:
                        try:
                            data = origin.recv(65536)
                        except BlockingIOError:
                            continue
                        if not data:
                            return
                        if origin is client:
                            frontend = (frontend + data)[-65536:]
                            if b"COMMIT\x00" in frontend:
                                saw_commit.set()
                            server.sendall(data)
                        elif saw_commit.is_set():
                            # Wait for the server's COMMIT response and drop it,
                            # then close both sockets instead of acknowledging it.
                            backend = (backend + data)[-65536:]
                            if b"COMMIT\x00" in backend:
                                lost_ack.set()
                                return
                        else:
                            client.sendall(data)
        except Exception as exc:
            errors.append(type(exc).__name__)
        finally:
            listener.close()

    thread = threading.Thread(target=proxy, daemon=True)
    thread.start()
    baseline = (ROOT / "database/postgresql/001_Baseline.sql").read_bytes().decode("utf-8")
    checksum = hashlib.sha256(baseline.encode("utf-8")).hexdigest().upper()
    direct = psycopg.conninfo.make_conninfo(DSN, dbname=name)
    through_proxy = psycopg.conninfo.make_conninfo(
        DSN, host="127.0.0.1", port=proxy_port, dbname=name, sslmode="disable", connect_timeout=10)
    try:
        try:
            with psycopg.connect(through_proxy) as client:
                client.execute(baseline)
                client.execute("INSERT INTO public.schema_migration(version,script_name,checksum) VALUES(1,%s,%s)",
                               ("001_Baseline.sql", checksum))
                client.commit()
        except psycopg.OperationalError:
            print("OK: cliente perdió la conexión durante COMMIT.")
        else:
            raise AssertionError("La conexión no fue interrumpida durante COMMIT.")
        thread.join(12)
        if thread.is_alive() or errors or not saw_commit.is_set() or not lost_ack.is_set():
            raise AssertionError(f"El proxy no observó el corte previsto: {errors}")
        with psycopg.connect(direct) as observer:
            if observer.execute("SELECT count(*) FROM public.schema_migration WHERE version=1").fetchone()[0] != 1:
                raise AssertionError("No se observó el registro confirmado.")
            if observer.execute("SELECT last_number FROM public.ticket_counter WHERE scope='venta'").fetchone()[0] != 0:
                raise AssertionError("No se observó el contador inicial.")
        print("OK: el servidor confirmó esquema y registro a pesar del error del cliente.")
        print("Límite: psycopg y proxy local; no ejecuta C# ni Npgsql.")
    finally:
        listener.close()
        thread.join(12)


if __name__ == "__main__":
    main()
