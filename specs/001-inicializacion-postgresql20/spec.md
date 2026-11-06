# Especificación: preparación segura de una base nueva 2.0

**Identificador de especificación**: `001-inicializacion-postgresql20` (no es una rama)
**Creada**: 2026-11-06 | **Estado**: En desarrollo, no habilitada para producción
**Vinculación**: parte de JHO-136; no cubre todo el issue.
**Entrada**: preparar la integración de Frubber 2.0 con la línea base elegida, preservando la 1.0 y las bases existentes.

## Escenarios de usuario y pruebas

### Historia 1 — Preparar un servidor nuevo (P1)

Un administrador prepara una base nueva dedicada a Frubber 2.0 y puede comprobar qué versión se instaló sin modificar ninguna base anterior.

**Prueba independiente**: en un servidor aislado, crear una base vacía y solicitar su preparación dos veces; inspeccionar el historial y el esquema creado.

1. **Dado** un destino nuevo dedicado y vacío, **cuando** se prepare la línea base aprobada, **entonces** el esquema y su registro quedan confirmados juntos una sola vez.
2. **Dado** un destino que ya tiene el registro esperado, **cuando** se repita la operación, **entonces** no se vuelve a crear el esquema ni se registra una segunda aplicación.
3. **Dado** un fallo antes de confirmar, **cuando** termine la operación, **entonces** no quedan cambios parciales de esa instalación.

### Historia 2 — Proteger la información existente (P1)

Un administrador evita que una instalación o un arranque incompatible altere una base que contiene información previa.

**Prueba independiente**: probar en bases temporales con datos ajenos, con el esquema anterior y con un historial 2.0 distinto; comprobar que el contenido anterior permanece igual.

1. **Dado** un destino con objetos o datos existentes sin el historial 2.0 válido, **cuando** se solicite su preparación, **entonces** se rechaza sin modificarlos.
2. **Dado** un historial 2.0 desconocido, cambiado, incompleto o mezclado con el anterior, **cuando** se solicite su preparación, **entonces** se rechaza sin reemplazarlo.
3. **Dado** un destino con historial 2.0, incluso vacío o mezclado, **cuando** el migrador anterior intente usarlo, **entonces** se detiene sin aplicar sus migraciones.

### Historia 3 — Operación repetida o interrumpida (P2)

Un administrador obtiene un resultado seguro aunque dos preparaciones coincidan o se interrumpa la conexión.

**Prueba independiente**: iniciar dos preparaciones sobre un mismo destino vacío; provocar un fallo antes de confirmar y simular una interrupción al confirmar.

1. **Dado** dos preparaciones simultáneas que usan el mismo mecanismo, **cuando** terminen, **entonces** una instala y la otra reconoce el historial sin duplicarlo.
2. **Dado** una pérdida de conexión mientras se confirma, **cuando** no sea posible saber el resultado, **entonces** se informa que debe revisarse el historial y no se afirma que no hubo cambios.

### Casos límite

- El destino contiene una tabla, función, tipo, esquema u otro objeto preexistente, aun sin filas.
- El registro contiene una versión futura, nombre o huella distinta, está vacío o no tiene el formato esperado.
- La conexión carece de permisos, se cancela o pierde conectividad; no debe revelar credenciales ni datos comerciales.
- Un administrador u otra herramienta que no use el mecanismo de coordinación modifica el destino simultáneamente: esto no está cubierto; se requiere exclusividad operativa.
- El registro válido no garantiza que el resto del esquema no haya sido alterado: no se afirma validación de integridad completa.

## Requisitos

### Requisitos funcionales

- **RF-001**: La preparación usa la línea base 2.0 aprobada y conserva un registro verificable de versión, nombre y huella del contenido.
- **RF-002**: La primera instalación del esquema y su registro se confirman o revierten juntos.
- **RF-003**: Una repetición del registro conocido no aplica el esquema por segunda vez.
- **RF-004**: La preparación rechaza destinos con objetos existentes sin registro válido y rechaza registros incompatibles o mezclados, sin borrar ni cambiar su contenido.
- **RF-005**: El migrador anterior rechaza cualquier destino con historial 2.0 antes de ejecutar sus migraciones.
- **RF-006**: Las preparaciones concurrentes del mismo mecanismo se coordinan para producir como máximo una instalación.
- **RF-007**: Un resultado de confirmación incierto se comunica como incierto, sin exponer datos sensibles.
- **RF-008**: Esta preparación no se ejecuta automáticamente desde los puestos, no migra datos de clientes y no habilita el uso de la 2.0 por la aplicación.
- **RF-009**: Las pruebas de integración se limitan a bases temporales de un servidor de pruebas aislado; no se automatizan operaciones globales del guion de seguridad todavía no revisado.

### Entidades principales

- **Destino nuevo**: base dedicada donde se prepara el esquema, distinta de la instalación de un cliente.
- **Registro de línea base**: versión, nombre y huella que identifican lo instalado.
- **Historial anterior**: registro usado por el migrador de la estructura anterior, incompatible con el de 2.0.

## Criterios de éxito

- **CE-001**: En una prueba de destino vacío, la primera preparación registra una instalación y la segunda registra cero; existe exactamente un registro esperado.
- **CE-002**: En los casos de datos ajenos, historial anterior, historial cambiado o mezclado, todas las pruebas de rechazo conservan el contenido previo.
- **CE-003**: En una prueba con dos preparaciones simultáneas hay una sola instalación y ningún registro duplicado.
- **CE-004**: En la prueba de fallo antes de confirmar no permanece ninguna parte del esquema o registro nuevos.
- **CE-005**: Se ejecutan las pruebas automatizadas de integración y regresión pertinentes y se registra su resultado real antes de considerar terminado el tramo.

## Suposiciones y límites

- El destino es creado y asignado explícitamente por un administrador en un servidor de pruebas o en el futuro servidor dedicado; el mecanismo no lo crea ni borra.
- La exclusividad frente a herramientas externas es responsabilidad operativa; la coordinación solo cubre ejecutores que siguen el mismo protocolo.
- Seguridad de roles, autenticación de cajero, conexión de la aplicación, migración de clientes y distribución quedan fuera de este tramo, pero siguen siendo requisitos de JHO-136 y del lanzamiento.
