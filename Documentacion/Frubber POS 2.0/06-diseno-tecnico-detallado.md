# Diseño técnico detallado de Frubber POS 2.0

Versión 2.0 · 19 de noviembre de 2026

- **T-1.** WPF enlaza las vistas con sus ViewModels. C# aplica las reglas en Domain y Application. Infrastructure abre conexiones Npgsql con pool y operaciones parametrizadas. La reconexión verifica el perfil, el puesto y la identidad del servidor.
- **T-2.** Los casos de uso reciben la identidad operativa del puesto y del cajero. La composición se realiza en App. Las ventas se confirman junto con sus líneas, inventario y contador. Después de perder la respuesta de confirmación se conserva el intento y se consulta su resultado con la misma identidad.
- **T-3.** El servidor prepara la base, gestiona respaldos y ejecuta migraciones. El puesto instala su certificado público, perfil protegido y acceso directo al modo puesto. El cliente comprueba estado, compatibilidad, licencia y conexión TLS VerifyFull.
- **T-4.** La verificación incluye dos puestos concurrentes, corte durante el cobro, recuperación de respuesta perdida, consecutivos sin huecos, restauración de un pg_dump y arranque con motor indisponible. Se prueban permisos, reversión de la transacción y exactitud de cada saldo.
