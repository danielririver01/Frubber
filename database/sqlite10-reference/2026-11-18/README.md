# Referencia SQLite de Frubber POS 1.0

JHO-123. Captura del 18 de noviembre de 2026 a partir del respaldo local del 14 de octubre. El dueño confirmó en esta conversación que corresponde a Don Rodrigo y a Frubber POS 1.0.0.

`schema.sql` contiene las definiciones de tablas, índices y disparadores extraídas de esa muestra. Se reprodujo su estructura completa en una base SQLite vacía y se comprobó su igualdad con el origen.

La base con datos, otra copia del esquema y su manifiesto están en la carpeta local estable `artifacts/reference/sqlite10/2026-11-18/`, ignorada por Git. El manifiesto identifica el origen, fecha, negocio, versión, migraciones, huellas SHA-256 y resultados de verificación. Ese conjunto permite recuperar la muestra usada para JHO-124.

La copia se obtuvo mediante la API de respaldo de SQLite con el origen abierto en modo de solo lectura. Se verificaron integridad completa, claves foráneas e igualdad de todos los valores de todas las tablas, incluidos registros internos y campos binarios. Las huellas del archivo de origen y su WAL coincidieron antes y después de la captura.

| Archivo | SHA-256 |
| --- | --- |
| Respaldo de origen | C4AE5EEE6C09AF7E717E0CC2137D79AD04CBBAF14B8A1A642601BD550E11B5A8 |
| Copia consistente `frubberpos.db` | EA78CE488D2329A2802B056DC6E4E22DA35A9AA9E15C2B09EE35ACF7F96CBF68 |
| `schema.sql` | 21C33466D68432A0D47FA064B563FFBFD670FF3116E68BC32922F567C9EA6F43 |

La muestra tiene cuatro migraciones de la 1.0, dos usuarios, dos sesiones de caja, dieciocho productos, cinco ventas y ocho líneas de venta. El manifiesto conserva los recuentos de las demás tablas. Estos recuentos complementan la comprobación de igualdad de todos los valores.
