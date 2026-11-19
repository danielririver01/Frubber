# Arquitectura técnica de Frubber POS 2.0

Versión 2.0 · 19 de noviembre de 2026

WPF y MVVM componen los casos de uso del monolito modular .NET 10. Infrastructure usa Npgsql y Dapper con SQL explícito parametrizado. Los puestos se conectan directamente a PostgreSQL 18 por la red local. El servidor ejecuta el motor local de datos y puede operar además como puesto de venta. PostgreSQL 18.7 es la versión aprobada para el instalador de Caja principal desde el 19 de noviembre de 2026 (JHO-121).

- **A-1 y A-5.** ADR-004 admite el motor local de base de datos. ADR-010, SQLite con WAL, aplica a la 1.0. En 2.0 el acceso es por conexión al motor y cada puesto tiene su propio rol.
- **A-2 y A-3.** ADR-008, ADR-018 y ADR-028 exigen respaldo verificable antes de migrar. El servidor aplica las migraciones y detiene la operación si falla el respaldo. La tolerancia de hardware de ADR-013 se sigue por JHO-119.
- **A-4 retirada.** AD-02 retira la copia redundante del sello de licencia en ProgramData. DPAPI protege las credenciales de los puestos.
- **A-6 y A-7.** El instalador distingue equipo servidor y puesto. Solo el servidor prepara y migra el esquema. Un puesto incompatible se bloquea hasta actualizar. Las ADR-021 y 024 quedan retiradas. Rigen las ADR-022, 023 y 025 a 030.
- **ADR-029.** PostgreSQL posee los datos. TLS VerifyFull verifica al servidor. Los roles de puesto y las funciones con privilegios acotados limitan la escritura y el acceso a datos sensibles. AD-03 exige probar las mitigaciones antes de distribuir.
- **ADR-030.** El servidor conserva el contenido firmado. Se comprueban firma, fecha, equipo y cupo. Soporte busca la FRB1 en el historial antes de migrar. Si falta, el dueño la reemite para el mismo equipo con una caja. El migrador necesita el texto firmado porque el sello 1.0 no conservó la firma.

## Contrato de licencia

FRB1 usa `FRB1|keyId|CODIGO_SERVIDOR|yyyyMMdd.firmaBase64`. FRB2 usa `FRB2|keyId|CODIGO_SERVIDOR|yyyyMMdd|cupo.firmaBase64`. El código consta de 16 hexadecimales en mayúsculas. El cupo FRB2 es un entero positivo. ECDSA P-256 con SHA-256 firma el texto UTF-8 anterior al punto. La firma IEEE P1363 de 64 bytes usa Base64 estándar.
