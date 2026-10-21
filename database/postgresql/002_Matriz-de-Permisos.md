# M2-28 · Matriz de permisos del puesto (Frubber POS 2.0)

Decisión de origen: **ADR-029 punto 10**, **ADR-026 reformulada**, decisión **AD-03**
(acta 3.6) y enmienda **S-3**. Candado de lanzamiento: la 2.0.0 NO se distribuye
sin esta matriz implementada y probada.

Archivos:

| Archivo | Qué contiene |
|---|---|
| `database/postgresql/001_Baseline.sql` | Línea base del esquema (M2-27) |
| `database/postgresql/002_Security.sql` | Roles, revokes, funciones SECURITY DEFINER |
| `prototipo/verificacion-seguridad-real.sql` | Pruebas contra el esquema real (criterios 1–3) |

## 1. Roles

| Rol | Login | Qué es |
|---|---|---|
| `postgres` | sí | Dueño del motor. Solo instalación, migraciones y respaldo (en el local, únicamente el servidor lo usa). |
| `frubber_puesto` | NO | Grupo con la matriz mínima. Nadie se conecta con él. |
| `puesto_<machine_code>` | sí | Uno por puesto autorizado (p. ej. `puesto_deadbeef00000001`). Miembro de `frubber_puesto`. Contraseña generada por el motor; se guarda con **DPAPI** en el archivo de configuración del puesto, nunca en texto plano. |
| `0000000000000000` | — | Marcador del terminal heredado de la 1.0 («Puesto desconocido (versión 1.0)»), desactivado; no tiene rol de base de datos. |

Cada rol de puesto lleva `CONNECTION LIMIT 12` (pool de 10 por puesto, decisión
de M2-26: el valor por omisión de 100 multiplicado por 5 cajas agotaría el motor).

## 2. Matriz sobre la base `frubber`

### Lecturas permitidas para el rol de grupo

| Objeto | Tipo |
|---|---|
| `product` | SELECT (catálogo para vender) |
| `business_setting` | SELECT (encabezado del ticket) |
| `v_sale_history` | SELECT (historial del período) |
| `v_open_cash_totals` | SELECT (totales de caja abierta) |

### Todo lo demás: SIN permisos

| Objeto | Qué queda prohibido |
|---|---|
| `app_user` | SELECT/INSERT/UPDATE/DELETE/TRUNCATE — el hash del PIN nunca sale del motor |
| `sale`, `sale_line`, `sale_void` | sin lectura directa y **sin escritura directa de ningún tipo** |
| `cash_session` | sin lectura directa (se lee por `v_open_cash_totals`) y sin escritura |
| `terminal` | sin lecturas: solo vía `terminal_info()` (que también actualiza `last_seen_at` = latido) |
| `license_state` | lectura y escritura prohibidas (la firma la verifica en cada puesto la app, ADR-030) |
| `ticket_counter` | prohibido (numeración vía bloqueo de fila, M2-29 la expone por función) |
| `schema_migration` | prohibido (solo el servidor migra) |
| `pin_attempt_log`, `pin_lock_state` | prohibidos (los escriben las funciones SECURITY DEFINER) |
| esquema `public` | `CREATE` denegado: el puesto no puede crear objetos |
| base de datos | `CREATE`/`TEMP` denegados; `CONNECT` solo para `frubber_puesto` y `postgres` |

### Escrituras en general

**Ninguna tabla se escribe directamente por un puesto.** Todas las rutas de
escritura que el flujo operativo necesite (abrir caja, registrar venta, anular,
cerrar caja) llegan después como funciones SECURITY DEFINER: M2-29 (numeración
sin huecos) y M2-19 (cobro idempotente). Con M2-28 el puesto queda con acceso de
escritura **cero**, y cada función que se agregue a futuro debe REPITIR esa
prohibición: `REVOKE EXECUTE ... FROM frubber_puesto` es el punto de partida
normal, no al revés.

## 3. PIN: verificación dentro del motor (ADR-026 reformulada)

1. La app llama `app_pin_challenge(user_id)` → sal, iteraciones, versión del
   algoritmo. La sal **no es secreta** (se asume su exposición por I/O y
   respaldos; la defensa es el PIN de 4 dígitos + PBKDF2).
2. El puesto calcula **PBKDF2-HMAC-SHA-256** localmente, con el mismo algoritmo
   y las 210 000 iteraciones de la 1.0 (`Pbkdf2PinHasher`). El PIN reviso NUNCA
   sale del puesto: no viaja en claro por la red.
3. La app llama `app_pin_verify(user_id, hash_candidato)`; la función
   **SECURITY DEFINER** compara contra el hash guardado. El hash guardado nunca
   vuelve al cliente, y un cliente que intente leerlo recibe 42501.
4. La comparación es por igualdad de bytes. Se documenta el canal de tiempo
   como riesgo residual aceptado (AD-03), igual que H-07.
5. Límite: 5 fallos consecutivos bloquean el usuario. Cada bloqueo sucesivo
   duplica la espera (2 → 4 → 8 → 16 → 30 min, tope 30). El éxito limpia
   contadores y bloqueos. `app_pin_verify` no lanza excepciones: devuelve
   `{authenticated, blocked, wait_seconds}` para que la pantalla muestre el
   mensaje con la espera.
6. **Cada intento queda en `pin_attempt_log`** (OK / FALLO / BLOQUEO) con el rol
   que lo hizo (`session_user` = el puesto) y con marca de tiempo.
7. Riesgo residual registrado: un cliente SQL malicioso podría envolver los
   intentos en una transacción que se deshace (`ROLLBACK`) y evadir el
   contador. Se acepta por escrito (AD-03 / H-07): la defensa de fondo es el
   espacio del PIN (4 dígitos) + PBKDF2; el límite existe para frenar el
   ataque en línea de clientes honestos, y el login de la app siempre compromete
   al terminar (sin ROLLBACK pendiente).
8. `app_user_directory()` devuelve `user_id, display_name, role` de los usuarios
   activos (la pantalla de login necesita la lista), nunca sal ni hash.

## 4. Ciclo de vida del puesto (alta y baja, solo administrador)

```sql
-- Alta (devuelve rol y contraseña UNA VEZ; guardar con DPAPI):
SELECT * FROM register_terminal('Caja 2', 'DEADBEEF00000001');
--  db_role                    generated_password
--  puesto_deadbeef00000001    ce88...(48 hex, generada por gen_random_bytes)

-- Desactivación (el puesto deja de poder conectar; la fila queda como historia):
SELECT deactivate_terminal('DEADBEEF00000001');
```

* `register_terminal` valida: nombre 1–60, código de 16 hex mayúsculas,
  único, y rechaza `0000000000000000` (reservado a la 1.0).
* El cupo de puestos de la **licencia** NO lo aplica el motor: lo aplica el
  programa con la firma FRB2 (cupo en la licencia, ADR-030). Igual que D-08,
  queda en las reglas de autorización del programa (riesgo residual aceptado).

## 5. Contraseña del puesto en el archivo de configuración (DPAPI)

Archivo por puesto (ejemplo `config/puesto.cfg`), cifrado con DPAPI
(`ProtectedData`, scope `CurrentUser`, con entropía adicional):

```
(connectionString-like)
Host=<IP del servidor>;Port=5432;Database=frubber;Username=puesto_<code>;Password=<DPAPI-sellada>
```

La contraseña NUNCA queda en texto plano en disco. El sellado/desellado vive en
la capa app (M2-26/M2-10) y usa la misma estrategia DPAPI que la 1.0 ya aprobó
(ProtectedData + entropía por máquina). En el motor la seguridad no depende de
ese archivo: si la contraseña se filtra, el rol del puesto sigue sin poder
escribir nada ni leer el hash del PIN.

## 6. `pg_hba.conf` del servidor del local (M2-32 la instala)

```text
# Solo la subred del local; solo scram-sha-256. Quedan fuera redes externas.
local   all             postgres                                scram-sha-256
host    frubber         all             127.0.0.1/32            scram-sha-256
host    frubber         all             192.168.1.0/24          scram-sha-256
host    all             all             127.0.0.1/32            scram-sha-256
host    all             all             ::1/128                 scram-sha-256
```

`192.168.1.0/24` es un EJEMPLO: el instalador fija la subred real del local
(sin incluir la Puerta de Enlace del ISP ni redes de invitados). El motor local
de este taller ya corre con `scram-sha-256` para todas las conexiones (se
verificó en el propio `pg_hba.conf` de instalación).

## 7. Cómo se probó (evidencia)

1. `prototipo/verificacion-seguridad-real.sql` sobre la base `frubber` real:
   23/23 declarativas, 6/6 PIN, 13/13 de escritura/lectura denegada en la práctica (42501).
2. Alta real de un puesto con `register_terminal`, login con `psql` usando su
   contraseña (scram-sha-256): aceptado; contraseña incorrecta → `FATAL`.
3. `deactivate_terminal`: la fila queda `is_active=false`, el rol a `NOLOGIN`,
   y un reingreso da `FATAL: la autentificación password falló`.
4. Evidencia completa en la bitácora 6.2 (E-07 = esquema, E-08 = seguridad).
