# AGENTS.md — Frubber POS · JHO-16

> Crear solución .NET y estructura de proyectos.
> Este archivo es la guía operativa para agentes. No amplía el alcance aprobado.

## 1. Qué es Frubber POS (resumen)

Aplicación de escritorio **offline**, para verdulería/fruver en **un solo PC** con
Windows 10/11 x64. Gestión de productos (kg/lb/unidad), inventario básico,
ventas, caja básica, usuarios (ADMIN/CASHIER con PIN de 4 dígitos), historial
inmutable, impresión Windows, respaldo manual y licencia offline vitalicia.

Stack aprobado (ADR-001…ADR-020):

- C# sobre **.NET 10 LTS**, Desktop con `net10.0-windows`. WPF + MVVM.
- **SQLite** embebido + `Microsoft.Data.Sqlite` + **Dapper**, SQL explícito parametrizado. Sin EF Core.
- Monolito modular. Publicación autocontenida x64 + Inno Setup (se decide después, fuera de JHO-16).
- Sin MediatR, sin bus de mensajes, sin CQRS extra, sin servidor, sin cloud, sin IA.

## 2. Fuente de verdad documental

Leer en este orden ante cualquier duda. Lo local manda sobre la memoria del agente:

1. `Documentacion/Documento Vision y Alcance/Frubber POS — Documento de Visión y Alcance.pdf` (v1.1)
2. `Documentacion/Especificación de Requisitos de Software (SRS)/Frubber POS — Especificación de Requisitos de Software (SRS).pdf` (v1.1)
3. `Documentacion/Documento de Reglas de Negocio/Frubber POS — Documento de Reglas de Negocio.pdf` (v1.0)
4. `Documentacion/Arquitectura Tecnica/Frubber POS — Documento de Arquitectura Técnica.pdf` (v1.0, ADR-001…ADR-020, §16 estructura)
5. `Documentacion/Diseño Técnico Detallado/Frubber POS — Diseño Técnico Detallado.pdf` (v1.0, P-DD-001…P-DD-007)
6. `Documentacion/Diseño de Base de Datos/Frubber POS — Documento de Diseño de Base de Datos.pdf` (v1.0) + `Documentacion/Diseño de Base de Datos/001_Initial.sql` (normativo)
7. `Documentacion/Diseño de Interfaz y Experiencia de Usuario/Frubber POS — Diseño de Interfaz y Experiencia de Usuario.pdf` (v1.0, P-UX-001…P-UX-014: menú por rol, login con pad, wizard, TPV)

**Diseño visual normativo (desde 2026-10-02, reemplaza a Stitch/contact sheet):**
prototipos HTML funcionales del dueño en `Documentacion/Diseño de Interfaz y Experiencia de Usuario/`
— `frubber-pos*.html` (integrado + login/venta/caja/productos/historial/configuración/respaldo),
`splash_screen_frubber_pos.html`, capturas claro y oscuro (`pos-*`, `lg-*`, `caja-*`, `config-*`,
`bk-*`, `dark-*`…), `.fonts/` (Inter), `iconos/` (SVG/ICO de marca). Decisiones del dueño:
PIN 4 dígitos; sin exportación de historial ni restauración desde la UI; acciones masivas en
Productos sí; splash al inicio; tema oscuro real (ronda final, JHO-71). Tokens v3 en código:
`src/FrubberPOS.Desktop/Themes/FrubberTheme.xaml` (claro) + `FrubberTheme.Dark.xaml` (preparado).

## 3. Tarea en curso (vive en Linear, no aquí)

Este archivo NO fija la tarea actual: los issues son temporales. Al iniciar
sesión, el agente resuelve la tarea vigente desde Linear (equipo `Frubber`,
proyecto `Frubber POS 1.0`):

1. Consultar el issue indicado por el usuario (o el siguiente `Todo` del
   proyecto si no indica uno).
2. Tomar de Linear: título, descripción, criterios de aceptación, rama
   sugerida (`gitBranchName`), padre y hermanos (los hermanos NO se tocan).
3. Moverlo a `In Progress` al iniciar. Al cumplir todos los criterios con
   evidencia: publicar comentario con la evidencia y mover a `Done`
   directamente, sin pedir confirmación.
4. El equipo Frubber NO tiene estado `In Review`; el flujo es
   `Todo → In Progress → Done`.

## 4. Estructura a crear (ADR-020 §16)

En la raíz del repo:

```text
FrubberPOS.sln
src/FrubberPOS.Domain/FrubberPOS.Domain.csproj          (net10.0, sin dependencias de proyecto)
src/FrubberPOS.Application/FrubberPOS.Application.csproj (net10.0, ref → Domain)
src/FrubberPOS.Infrastructure/FrubberPOS.Infrastructure.csproj (net10.0, ref → Application, Domain)
src/FrubberPOS.Desktop/FrubberPOS.Desktop.csproj        (net10.0-windows, UseWPF, ref → Application; ref → Infrastructure SOLO para composición en App)
tests/FrubberPOS.Tests/FrubberPOS.Tests.csproj          (net10.0-windows, xUnit — decidido 2026-09-28; Windows-only como el producto, puede referenciar Desktop para probar ViewModels sin abrir ventanas)
```

Reglas de dependencia (prohibidas las circulares):

- Domain → nada (ni WPF, ni Dapper, ni SQLite, ni Serilog, ni Windows API).
- Application → Domain únicamente.
- Infrastructure → Application + Domain.
- Desktop → Application; usa Infrastructure **únicamente en el Composition Root (App)**. Sin SQL ni reglas críticas en Desktop.
- Tests → los que necesite. No se distribuye.

### Comandos de referencia (Windows, PowerShell)

```powershell
dotnet new sln -n FrubberPOS
dotnet new classlib -n FrubberPOS.Domain -f net10.0 -o src/FrubberPOS.Domain
dotnet new classlib -n FrubberPOS.Application -f net10.0 -o src/FrubberPOS.Application
dotnet new classlib -n FrubberPOS.Infrastructure -f net10.0 -o src/FrubberPOS.Infrastructure
dotnet new wpf -n FrubberPOS.Desktop -f net10.0-windows -o src/FrubberPOS.Desktop
dotnet new xunit -n FrubberPOS.Tests -f net10.0 -o tests/FrubberPOS.Tests
dotnet sln FrubberPOS.sln add src/FrubberPOS.Domain src/FrubberPOS.Application src/FrubberPOS.Infrastructure src/FrubberPOS.Desktop tests/FrubberPOS.Tests
dotnet add src/FrubberPOS.Application/FrubberPOS.Application.csproj reference src/FrubberPOS.Domain/FrubberPOS.Domain.csproj
dotnet add src/FrubberPOS.Infrastructure/FrubberPOS.Infrastructure.csproj reference src/FrubberPOS.Application/FrubberPOS.Application.csproj src/FrubberPOS.Domain/FrubberPOS.Domain.csproj
dotnet add src/FrubberPOS.Desktop/FrubberPOS.Desktop.csproj reference src/FrubberPOS.Application/FrubberPOS.Application.csproj src/FrubberPOS.Infrastructure/FrubberPOS.Infrastructure.csproj
dotnet restore FrubberPOS.sln
dotnet build FrubberPOS.sln -c Release --no-restore
```

Verificación mínima para dar por hecho JHO-16: `restore` + `build Release` en
verde, sin warnings de referencia, y `git status` limpio salvo los archivos nuevos.

> Nota de plantilla (2026-09-28, SDK 10.0.401): `dotnet new sln` genera por
> defecto `.slnx`; JHO-16 exige el clásico `FrubberPOS.sln`, recrear con
> `dotnet new sln -n FrubberPOS -f sln`. Además, el `App.xaml.cs` generado
> declara `App : Application`, que colisiona con el namespace
> `FrubberPOS.Application`; mantenerlo calificado como
> `System.Windows.Application` (relevante para JHO-14).

> ⚠️ Estado del entorno (2026-09-28): `dotnet` NO está en el PATH de esta
> máquina y el workspace aún no tiene `src/` ni `*.sln`. Antes de ejecutar lo
> anterior hay que instalar el **.NET 10 SDK x64** y reabrir la terminal.

## 5. Convenciones obligatorias (desde JHO-16)

- Código y namespaces en **inglés**: `FrubberPOS.<Proyecto>.<Funcionalidad>`.
  Textos visibles y mensajes de validación en **español**.
- Casos de uso futuros: una clase por caso, sufijo `UseCase`, firma
  `ExecuteAsync(Request, CancellationToken) → Task<Result<TResponse>>`;
  `Request`/`Response` como records inmutables. Sin MediatR (P-DD-001).
- `async/await` + `CancellationToken` de extremo a extremo. Prohibido
  `.Result`, `.Wait()`, `async void` (salvo handlers WPF). Prohibido Service Locator;
  DI solo vía constructor, Composition Root solo en `App` (Desktop, JHO-14).
- No `double`/`float`/`decimal` para dinero/cantidades persistidas: `long` en COP
  enteros y cantidades en milésimas (ADR-009). Sin `REAL` en SQLite.
- No loguear jamás: PIN, sal/hash, claves de licencia, payload DPAPI, SQL con
  datos comerciales, seriales físicos completos (ADR-011/016, JHO-15).
- `001_Initial.sql` es inmutable y vive como recurso embebido en Infrastructure
  (JHO-19, NO en JHO-16). El script no lleva BEGIN/COMMIT; lo abre el ejecutor.

## 6. Prohibiciones explícitas

MediatR, EF Core, servidor/API, cloud, IA, categorías, precio de compra,
impuestos separados, pagos combinados, devoluciones parciales, egresos de caja,
reportes/exportaciones, restauración desde UI, báscula automática, ESC/POS
directo, biblioteca externa de impresión, generador de licencias o clave privada
dentro de `FrubberPOS.sln` (herramienta privada separada, ADR-020 §16.7),
script Inno Setup dentro de la solución (§16.8).

## 7. Flujo de trabajo con Linear

- Todo cambio corresponde a un issue. Cerrar un hijo no cierra al padre.
- Operar con autonomía (2026-09-29): avanzar por los issues sin pedir
  confirmación en cada paso. Interrumpir al dueño solo para: pruebas que
  exijan sus ojos en la app, material de su llave privada, o
  contradicciones documentales. Reportar al cerrar cada issue.
- **Ramas: máximo una viva** (regla del dueño, 2026-10-10): trabajar en
  rama secundaria para no ensuciar `main`, pero NO crear una rama nueva
  por cada issue o tipo de trabajo: reutilizar la misma rama para el
  trabajo consecutivo. Al cerrar el issue: fusionar a `main` y borrar la
  rama (local y remota), o seguir en esa misma rama si el siguiente
  trabajo es inmediato. Nunca pueden quedar varias ramas acumuladas; el
  dueño NO gestiona ni borra ramas. El `gitBranchName` de Linear es
  sugerencia, no obligación. Mensaje tipo:
  `JHO-16: crea solución y estructura de proyectos`.
- Acumular ~45 archivos con cambios antes de cada commit + push a GitHub
  (balance 2026-09-29: ni micro-commits ni lotes gigantes sin respaldo).
  Excepción: si se cierra un issue con menos archivos, se sube igual con su
  comentario de evidencia y no se espera al lote.
- Si un documento aprobado contradice al código o a otro documento: NO suponer.
  Registrar el hallazgo y pedir aprobación de Jhoan Rivera (dueño del producto).

### Cómo reportar cambios al dueño (comunicación)

Al cerrar un issue o resumir trabajo al dueño, hablar en **lenguaje claro,
en español, orientado a lo que él ve en la pantalla**. NO un resumen técnico
de archivos, clases ni commits (eso va solo en el comentario de evidencia de
Linear). Patrón de respuesta:

1. **Qué se arregló / qué se agregó** — en una o dos frases, como se lo
   contarías a alguien que no programa. Ejemplo: *"Las columnas de la tabla
   del TPV ahora están alineadas igual que en Productos: el título de la
   columna y el valor quedan en la misma línea vertical."*
2. **Qué cambia para él al usar la app** — qué nota distinto al abrir,
   vender, ver historial, etc.
3. **Dónde probarlo** — pantalla o flujo concreto (ej. "abre Venta y
   compara el título PRECIO con el precio de un producto").
4. Solo si él lo pide: detalle técnico (archivos, commits, tests).

El comentario de evidencia en Linear sí es técnico (suite, build, commit).
La respuesta visible en el chat hacia el dueño, NO.

## 8. Definición de hecho (DoD)

Por tarea:

1. `FrubberPOS.sln` con los 5 proyectos en las rutas de §4.
2. TFMs correctos (`net10.0-windows` + `UseWPF` solo en Desktop).
3. Referencias exactamente como §4; `dotnet list reference` sin extras.
4. `dotnet restore` + `dotnet build -c Release` en verde en Windows 10/11 x64.
5. Sin paquetes MediatR ni referencias a servidor.
6. Issue en `Done` con comentario de evidencia del build.
