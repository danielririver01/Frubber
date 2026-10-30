# Frubber POS

[![C#](https://img.shields.io/badge/C%23-239120?style=flat&logo=csharp&logoColor=white)](https://learn.microsoft.com/dotnet/csharp/)
[![.NET 10](https://img.shields.io/badge/.NET-10_LTS-512BD4?style=flat&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![WPF](https://img.shields.io/badge/WPF-MVVM-512BD4?style=flat&logo=dotnet&logoColor=white)](https://learn.microsoft.com/dotnet/desktop/wpf/)
[![SQLite](https://img.shields.io/badge/SQLite-sin_servidor-003B57?style=flat&logo=sqlite&logoColor=white)](https://sqlite.org/)
[![xUnit](https://img.shields.io/badge/xUnit-102_archivos-de5f57?style=flat&logo=xunit&logoColor=white)](https://xunit.net/)
[![Version](https://img.shields.io/badge/version-1.0.1-blue)](https://github.com/danielririver01/Frubber)

Punto de venta de escritorio para **verdulerías y fruver**: funciona **100 % sin internet**,
en **un solo PC** de Windows, y está construido para que un cajero no pierda tiempo en
hora pico.

Sin nubes, sin suscripciones de infraestructura, sin servidor. La base de datos es un archivo
que vive junto al ejecutable: si sabes copiar un archivo, sabes respaldar el negocio.

---

## Índice

- [Qué lo distingue](#qué-lo-distingue)
- [Características](#características)
- [Arquitectura](#arquitectura)
- [Estructura del repositorio](#estructura-del-repositorio)
- [Requisitos y arranque](#requisitos-y-arranque)
- [Pruebas](#pruebas)
- [Instalador](#instalador)
- [Licenciamiento offline](#licenciamiento-offline)
- [Documentación](#documentación)

---

## Qué lo distingue

| Decisión | Por qué |
|---|---|
| **Sin EF Core.** SQL explícito con Dapper y parámetros | La caja no puede esperar a un ORM que decida por ella |
| **Dinero en enteros de COP.** Nada de `double`/`float`/`decimal` (ADR-009) | Un peso no admite redondeos binarios. Las cantidades van en milésimas |
| **Historial inmutable** | No se edita ni se borra una venta registrada |
| **Sin servidor, sin API, sin cloud** | Un negocio de barrio no depende de que AWS esté bien |
| **Migraciones con verificación de checksum** | Si un script cambia tras aplicarse, el arranque lo detecta |
| **Nunca se loguea un dato sensible** | PIN, hash, claves de licencia y payload DPAPI quedan fuera del log (ADR-011/016) |

---

## Características

### Caja y venta
- TPV rápido, navegable **enteramente por teclado**: el mouse es opcional.
- Productos en **kg, lb o unidad**, con báscula por **puerto COM** (RS232 nativo o USB con
  conversor HL-340/CH340). La **entrada manual siempre está disponible** como respaldo.
- **Sesiones de caja** con saldo de apertura, resumen por sesión y concurrencia controlada.
- **Ventas** con borradores guardados, anulación con motivo, ticket y composición del tique.
- Precios en pesos colombianos con formato local (`$95.500`).

### Usuarios y seguridad
- Dos roles: **ADMIN** y **CASHIER**, con acceso de **PIN de 4 dígitos**.
- **Licencia offline vitalicia** (ver [Licenciamiento offline](#licenciamiento-offline)).
- Protección de datos sensibles con **DPAPI** de Windows: el secreto no se guarda en claro.
- Registro estructurado con **Serilog**, sin filtrar nunca información comercial o sensible.

### Datos
- **SQLite** con `Microsoft.Data.Sqlite` y **Dapper**: SQL parametrizado explícito, sin ORM opaco.
- **Migraciones SQL versionadas** (`001_Initial`, `002_Trial`, `003_OpeningBalanceCorrection`,
  `004_Scale`) aplicadas por un migrador propio con catálogo, checksum y recursos embebidos.
- **Respaldo** de la base de datos mediante la API de respaldo en línea de SQLite.
- **Impresión** con el sistema de impresión de Windows.

### Interfaz
- **WPF + MVVM** con inyección de dependencias por constructor.
- Tema claro y tema oscuro, con tokens de diseño en XAML.
- Fuentes **Inter** embebidas: se ve igual en cualquier equipo, sin instalar tipografías.

---

## Arquitectura

Cuatro proyectos con dependencias que apuntan hacia adentro. `Domain` no conoce a nadie.

```
FrubberPOS.Desktop          WPF, ViewModels, Composition Root (App)
        │
FrubberPOS.Application      Casos de uso y puertos (interfaces)
        │
FrubberPOS.Infrastructure   Dapper, SQLite, migraciones, DPAPI, ECDSA, Serilog
        │
FrubberPOS.Domain           Entidades y reglas puras. Sin dependencias externas.
```

| Proyecto | Responsabilidad | Referencias |
|---|---|---|
| `FrubberPOS.Domain` | Entidades y reglas de negocio | ninguna |
| `FrubberPOS.Application` | Casos de uso y contratos | → `Domain` |
| `FrubberPOS.Infrastructure` | Dapper, SQLite, migraciones, criptografía, logging | → `Application`, `Domain` |
| `FrubberPOS.Desktop` | WPF: pantallas, ViewModels y arranque | → `Application` (y `Infrastructure` solo en `App`) |

Reglas que se hacen cumplir en el código:

- Casos de uso como **una clase por caso**, con sufijo `UseCase` y firma
  `ExecuteAsync(Request, CancellationToken) → Task<Result<TResponse>>`.
- `async/await` de extremo a extremo; prohibidos `.Result`, `.Wait()` y `async void`.
- Sin *Service Locator*: la inyección va por constructor y el *Composition Root* vive en `App`.
- Códigos y namespaces en inglés, textos visibles al usuario en español.

---

## Estructura del repositorio

```
FrubberPOS.sln
src/
  FrubberPOS.Domain/            Entidades y reglas de negocio
  FrubberPOS.Application/       Casos de uso y contratos
  FrubberPOS.Infrastructure/    Dapper, SQLite, migraciones, criptografía, logging
  FrubberPOS.Desktop/           Aplicación WPF (Views, ViewModels, temas)
tests/
  FrubberPOS.Tests/             Suite xUnit: 17 áreas, 102 archivos de prueba
installer/
  FrubberPOS.Installer/         Instalador propio en WPF (fuera de la solución)
Documentacion/                  Especificaciones y decisiones de diseño
```

---

## Requisitos y arranque

**Requisitos:** Windows 10/11 x64 y el [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
# Restaurar y compilar
dotnet restore FrubberPOS.sln
dotnet build FrubberPOS.sln -c Release

# Ejecutar desde el código
dotnet run --project src/FrubberPOS.Desktop
```

La base de datos se crea sola en el primer arranque: el migrador aplica los scripts en orden.

---

## Pruebas

**17 áreas, 102 archivos de prueba.** Cubren ventas, caja, inventario, autenticación,
balanza, respaldo, licenciamiento, rendimiento y seguridad, entre otras.

```bash
dotnet test FrubberPOS.sln --collect:"XPlat Code Coverage"
```

Los tests de licenciamiento son los más exigentes: **compilan y ejecutan el verificador real**
(`EcdsaLicenseSignatureVerifier`) en lugar de un doble, así que cualquier cambio en el formato
de la licencia rompe la suite de inmediato. También hay pruebas de concurrencia de caja y de
robustez del flujo de venta.

---

## Instalador

```powershell
.\installer\build-installer.ps1
```

Genera un único ejecutable con la aplicación completa comprimida en su interior.
No requiere internet ni componentes externos.

> El instalador vive deliberadamente **fuera** de la solución: `installer/` no forma parte de
  `FrubberPOS.sln` (ADR-020 §16.8).

---

## Licenciamiento offline

Frubber POS se activa con una **licencia vitalicia y offline**: no necesita conexión en el
momento de la activación ni nunca después.

```
FRB1|<keyId>|<CÓDIGO-DE-EQUIPO>|<aaaammdd>|<firma>
```

- El código de equipo son **16 caracteres hexadecimales** obtenidos del hardware.
- La licencia se firma con **ECDSA P-256 (SECP256R1) + SHA-256**, en formato IEEE P1363.
- El POS valida con una **llave pública embebida en el binario**.
- La llave privada **no está en este repositorio** y no debe estarlo: vive solo en el
  servidor emisor.

La herramienta que emite esas licencias es un proyecto complementario privado con su propio
panel administrativo, auditoría encadenada por hash y respaldos verificados. Está construido
para que el contrato entre emisor y POS quede garantizado por una prueba de interoperabilidad
que ejecuta este mismo verificador en C#.

---

## Documentación

El repositorio incluye el material de diseño completo en `Documentacion/`:

| Documento | Contenido |
|---|---|
| Visión y Alcance | Objetivos de negocio y límites del producto |
| Especificación de Requisitos de Software (SRS) | Requisitos funcionales y no funcionales |
| Reglas de Negocio | Reglas de caja, inventario y precios |
| Arquitectura Técnica | Capas, dependencias y **ADR-001 … ADR-020** |
| Diseño Técnico Detallado | Implementación por módulo |
| Diseño de Base de Datos | Esquema, relaciones e índices |
| Diseño de Interfaz y Experiencia de Usuario | Flujos de caja y pantallas |
| Guía de Instalación y Operación | Puesta en marcha y soporte |

---

## Estado

Versión **1.0.1** en operación, en desarrollo activo.

---

Hecho por [Jhoan Daniel Rivera Ortiz](https://github.com/danielririver01).
