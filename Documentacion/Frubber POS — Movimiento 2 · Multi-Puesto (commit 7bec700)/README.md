# Archivo de decisiones del Movimiento 2

La [edición oficial de Frubber POS 2.0](../Frubber%20POS%202.0/README.md) contiene los requisitos y la arquitectura vigentes. Para configurar el servidor o un puesto, consulta el [paquete 2.0](../Frubber%20POS%202.0/paquete/).

Esta carpeta conserva las actas, propuestas y comparaciones que llevaron a la decisión AD-01. Las versiones originales de 00, 2.1 a 2.5, 3.3, 4.0 y 5.0 se conservan como antecedentes. Para aplicar esas secciones utiliza su revisión correspondiente en el paquete 2.0.

Las ADR-021 y ADR-024 están retiradas. La arquitectura vigente es la conexión directa de los puestos a PostgreSQL, con un rol por puesto y TLS VerifyFull. La idempotencia del cobro se mantiene en los casos de uso y en PostgreSQL. La base normativa de estas decisiones son las actas 3.6 y 3.7 y las ADR-029 y ADR-030.
