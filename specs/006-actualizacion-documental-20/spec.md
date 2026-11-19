# Actualización documental de Frubber POS 2.0

JHO-132 aplica el plan 6.1 y las decisiones vigentes de las actas 3.4 a 3.7. El paquete oficial exige siete documentos versionados y fechados, las actualizaciones del paquete del Movimiento 2 y una publicación en Drive posterior al repositorio.

## Trazabilidad de las enmiendas

| Documento oficial | Cambios principales |
| --- | --- |
| Visión y Alcance | Servidor con PostgreSQL, puestos en red local y disponibilidad de la venta ligada al servidor. |
| SRS | S-1 servidor local, S-3 rol por puesto y TLS, S-4 firma y cupo. |
| Reglas de Negocio | FRB1 de una caja, cupo firmado de FRB2 y operación contra el servidor. |
| Arquitectura Técnica | A-1, A-5, A-6 y A-7. Retirada de A-4. ADR-010 rige para 1.0. ADR-021 y 024 retiradas, ADR-029 y 030 vigentes. |
| Diseño de Base de Datos | B-1 línea base PostgreSQL y B-3 respaldo con pg_dump y restauración con pg_restore. |
| Diseño Técnico Detallado | T-1 Npgsql, pool y reconexión. T-4 concurrencia, recuperación del cobro, numeración, restauración y caída del servidor. |
| Diseño de Interfaz y Experiencia de Usuario | U-2 mensajes de conectividad, autorización, actualización y cupo. Usar los prototipos funcionales vigentes para lo visual. |

El plan también actualiza 2.1, 2.3, 2.4, 2.5, el registro 3.3 y el índice 4.0. La versión PostgreSQL de 2.2 ya existe. Su texto de dinero debe coincidir con ADR-009 y el esquema aprobado en JHO-109, que usa enteros de 64 bits. El acta 3.7 levantó el requisito de congelar la 1.0.

## Decisión aprobada sobre la FRB1

AD-05 y ADR-030 establecen la continuidad de las FRB1. El código de la 1.0 guarda el contenido canónico bajo DPAPI sin la firma. El migrador requiere la FRB1 completa y comprueba su firma. El 19 de noviembre de 2026 el dueño aprobó buscar primero la emisión original en el historial de LicenseManager. Si falta, el dueño puede reemitir la FRB1 para el mismo equipo y una caja. La decisión se incorporó en S-4, Arquitectura, Reglas de Negocio y 2.4.
