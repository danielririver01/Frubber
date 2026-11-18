# Muestra de la versión 1.0 para la migración

JHO-123. Conservar una base SQLite con datos y su esquema real, con fecha y procedencia, para preparar JHO-124.

## Criterios

1. Una copia consistente de la base y el esquema exportado quedan en una ubicación estable con fecha.
2. Se registran negocio y versión exacta de la aplicación que originó la muestra.
3. La captura conserva los datos del origen y permite comprobar su integridad y reproducir el esquema.

El origen seleccionado es el respaldo local `frubberpos.licensed-backup-20261014.db`. El dueño confirmó Don Rodrigo y Frubber POS 1.0.0. El archivo con datos se conserva en `artifacts/reference/sqlite10/2026-11-18/`. El esquema y la trazabilidad se versionan en `database/sqlite10-reference/2026-11-18/`.
