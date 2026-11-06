# Constitución de desarrollo de Frubber POS

## I. Alcance aprobado y fuente de verdad

El alcance, las reglas de negocio y las decisiones aprobadas del proyecto prevalecen sobre las plantillas de Spec Kit. `AGENTS.md`, los documentos de `Documentacion/` y los issues de Linear aportan el contexto; cuando hay una contradicción material, se registra y no se inventa una decisión. Spec Kit organiza el trabajo, no reemplaza las decisiones del dueño ni cambia por sí solo un issue de estado.

## II. Preservar las instalaciones y la información

La 1.0 y las bases existentes de clientes no se modifican como efecto de preparar la 2.0. La línea base PostgreSQL 2.0 elegida es `database/postgresql/001_Baseline.sql`. La migración de datos desde SQLite es un trabajo separado y debe contar con respaldo y verificación antes del piloto. Las pruebas destructivas se limitan a recursos temporales de un servidor de pruebas aislado y nunca a bases reales o compartidas.

## III. Seguridad y licencia antes de distribución

Los puestos usan permisos mínimos, no superusuario ni escritura directa sobre tablas críticas. Credenciales por puesto protegidas, verificación de PIN, transporte seguro y licencia mantienen los candados aprobados (ADR-029 y AD-03). Una comprobación de usuario activo no equivale a autenticarlo. La unicidad de una solicitud no equivale a recuperación idempotente. No activar ni presentar rutas parciales como listas para distribución.

## IV. Transacciones y exactitud

Dinero y cantidades persistidos usan enteros según ADR-009 y la decisión JHO-109. La asignación de número de venta y sus efectos deben confirmar o revertir juntos; no reservar números por adelantado. Una caída durante la confirmación puede dejar un resultado desconocido; no prometer que no hubo cambios cuando no se sabe.

## V. Arquitectura existente

Mantener el monolito modular .NET/WPF, Npgsql y Dapper y las dependencias por capas de `AGENTS.md`. No introducir un servidor de aplicación, nube ni otras tecnologías prohibidas por el alcance. Solo el servidor administra el esquema 2.0; los puestos no migran.

## VI. Evidencia honesta y trabajo continuo

Cada cambio tiene pruebas proporcionales y trazabilidad a su issue. Diferenciar código escrito, revisión sintáctica, prueba SQL aislada, prueba real de aplicación y resultados comunicados por terceros. No marcar tareas de implementación o verificación como completadas sin evidencia suficiente; no cerrar el issue antes de sus criterios. Trabajar de manera autónoma y pedir intervención solo cuando sea realmente necesaria.

## Gobernanza

Estos principios recogen decisiones existentes; no sustituyen documentos aprobados. Si una plantilla o instrucción automática contradice reglas de la sesión, seguridad o este repositorio, prevalecen estas últimas. Cambios sustanciales a las reglas de producto requieren la decisión correspondiente y trazabilidad. Mantener la rama de trabajo asignada; el identificador de una especificación no crea ni cambia una rama.

**Versión**: 1.0.0 | **Adoptada**: 2026-11-06 | **Última revisión**: 2026-11-06
