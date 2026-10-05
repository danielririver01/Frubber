# Análisis preliminar · JHO-67 — Rediseño R2 Venta/TPV + modal de cobro

**Fecha:** 5 de octubre de 2026
**Para:** Jhoan Rivera (dueño del producto)
**Commit analizado:** `a052fd6` — *JHO-64: rediseno R1 - login, pantalla de bloqueo y modal de licencia*
**Rama de trabajo de esta sesión:** `arena/11865dfa-frubber` (documento de solo lectura; no se tocó código)

---

## 0. Veredicto (lo que hay que saber en 2 minutos)

El punto de partida es bueno: la arquitectura aguanta el rediseño, los tres bloqueantes que anunciaba la
revisión son reales, y **encontré un cuarto problema no listado que rompe el modo oscuro aunque se
arreglen los tres** (§3.2). Además hay un defecto concreto y verificable: **el modal de cobro anuncia
"F9 confirma" y F9 no hace absolutamente nada** (§5.2).

| # | Bloqueante | ¿Confirmado? | Dónde duele | Esfuerzo |
|---|---|---|---|---|
| 1 | Tokens de tema + lint de colores crudos | **Sí, y peor de lo que suena** | 77 líneas con color literal fuera del tema en 9 archivos · el diccionario claro usa `StaticResource` (107 veces, 0 `DynamicResource`) → el oscuro no repinta los estilos | Alto (toca el tema + 9 vistas) |
| 2 | Anti-doble-cobro y estado "cobrando" | **Sí** | El blindaje actual es un `bool` privado no observable; la venta se finaliza *después* de cerrar el modal, sin indicador ni errores en línea | Medio (TPV + cobro + navegador) |
| 3 | Precedencia del teclado | **Sí, con un defecto extra** | No hay dueño único por tecla; F9 muerto en el cobro; con el cobro como capa dentro del shell, F2/F4/F6/F8/F10 del TPV disparan por debajo | Medio (vistas + un enrutador) |
| 4 | *(no listado)* Modo oscuro no funcionaría aunque se fusione el diccionario | **Sí** | `FrubberTheme.xaml` resuelve sus 107 referencias con `StaticResource`: al fusionar el oscuro, los `Style` del tema siguen apuntando a los brushes claros → tema mixto | Bajo si se hace junto con el 1 |

Lo que **no** hay que rehacer: la lógica de venta (`DraftSale`, `FinalizeSaleUseCase`) es sólida y ya está
probada (rounding half-up, revalidación de precio, atomicidad, secuencia de tickets sin huecos). El
rediseño de R2 es de **interfaz, tokens y flujo de teclado**, no de dominio.

---

## 1. Alcance, método y lo que faltó

Este análisis se hizo **leyendo el repositorio y los prototipos**, sin ejecutar nada: en este entorno no
hay .NET SDK y NuGet/Microsoft están bloqueados (solo responde el registro npm), así que **no pude
compilar ni correr los 423 tests**. Toda afirmación de código va con archivo y línea para que se pueda
verificar a mano o en Windows.

**No estuvo disponible:**

| Qué | Estado |
|---|---|
| `revision-plan-JHO-67.md` (3 bloqueantes + 7 mejoras) | No está en el repo ni en el entorno. Las 7 mejoras de §6 son mi **reconstrucción razonada**, no el documento original: hay que contrastarlas. |
| Rama `dr4140485/jho-67-rediseno-r2-ventatpv-modal-de-cobro` | No existe en el remoto (`git ls-remote` solo devuelve `main`). No hay trabajo previo de R2 que revisar. |
| Los 10 prototipos de `/home/user/` del traspaso | En el repo viven en `Documentacion/Diseño de Interfaz y Experiencia de Usuario/` (misma familia, sin `splash_screen_frubber_pos.html` suelto). Usé esos como fuente normativa. |
| `revision-plan` / `Resumen-ejecutivo` / `capturas` en la raíz | No están en la raíz; el PDF y las capturas sí están en `Documentacion/…`. |
| PDFs de reglas y SRS | Están como PDF; solo contrasté lo que el propio código y `AGENTS.md` citan (P-UX-010, P-UX-011, P-UX-008, RN-VEN-002, ADR-009). |

**Fuentes normativas usadas:** `frubber-pos.html` (prototipo integrado, 5.347 líneas), `frubber-pos-rediseno.html` (Venta individual, 710 líneas), `AGENTS.md`, y el código (`src/FrubberPOS.Desktop/**`).

---

## 2. Radiografía del TPV y del cobro actuales

| Pieza | Tamaño | Qué es |
|---|---|---|
| `Views/TpvView.xaml` | 197 líneas | Buscador (42 px, chip F2), tabla RESULTADOS, tabla VENTA EN PREPARACIÓN, banda inferior con acciones + Subtotal/Descuento/TOTAL(28 pt) + botón Cobrar |
| `Views/TpvView.xaml.cs` | 98 líneas | `OnPreviewKeyDown`: F2, Enter (solo si el foco está en el buscador), Supr (solo si **no** está en el buscador), F4, F6, F8. `OnLoaded` refresca caja y busca |
| `ViewModels/TpvViewModel.cs` | 542 líneas | Búsqueda con token anti-anticuado, guard `submitting`, `ShowCheckout` + `finalizeSale`, banner de caja, impresión opcional |
| `Views/CheckoutDialog.xaml` | 116 líneas | `Window` modal, 560 px, resumen + descuento editable + 2 radios (Efectivo/Transferencia) + recibido + CAMBIO + botón "Confirmar  F9" |
| `ViewModels/CheckoutViewModel.cs` | 174 líneas | Reglas de descuento/recibido/cambio, `ConfirmCommand`, `RequestClose` |
| `App.xaml.cs:311` | — | `ShowCheckout` → `Resolve<CheckoutViewModel>` + `window.ShowDialog()` (bucle modal anidado) |
| `Themes/FrubberTheme.xaml` | 570 líneas | 33 `<Color>` + 33 brushes + 96 claves; **107 `StaticResource`, 0 `DynamicResource`** |
| `Themes/FrubberTheme.Dark.xaml` | 91 líneas | Tokens oscuros completos + `OnBrandBrush`… **que nadie usa** |
| `tests/` | 76 archivos, 423 tests | ViewModels completos, migraciones, licencia, y 3 guardianes de interfaz: `ThemeTests`, `XamlSafetyTests`, `MessageConsistencyTests`, más `ViewPreviewTests` (renderiza PNG reales) |

Ojo con un activo poco conocido: **`ViewPreviewTests` ya genera capturas de la app real** (`StaViewHarness.SavePng`, salida en `%TEMP%\opencode\frubber-preview`). Es la herramienta natural para el "antes/después" de esta ronda, sin abrir la app ni usar un PIN.

---

## 3. Bloqueante 1 · Tokens de tema y lint de colores crudos

### 3.1 Evidencia (medida, no impresiones)

Colores literales o por nombre **fuera de `Themes/`**: **77 líneas en 9 archivos**.

| Archivo | Líneas | Muestra |
|---|---|---|
| `Views/LicenseWindow.xaml` | 41 | `Background="#F8FAFC"` (16), `Foreground="#B91C1C"` (58), `Background="#D1FAE5"` (167), `Foreground="White"` (83, 111) |
| `Views/SplashWindow.xaml` | 13 | `Background="#062F2B"` (6), `#35B2A6` ×7 (barras de progreso), `Foreground="#94A3B8"` (68, 89) |
| `Views/LoginCardView.xaml` | 8 | `Foreground="White"` (109, 200, 360, 512, 602, 646), `Color="#0F172A"` (184), `Background="#0F172A"` (643) |
| `Views/BusinessSettingsView.xaml` | 6 | `Foreground="#B45309"` ×5 (pistas), `Foreground="#6B7280"` (47) |
| `MainWindow.xaml` | 3 | `Foreground="White"` (33, 97, 179) |
| `Views/CashView.xaml` | 2 | `Background="#2563EB"` (82, barra KPI de Transferencia), `BorderBrush="#FDE68A"` (135) |
| `Views/TpvView.xaml` | 2 | `BorderBrush="#FDE68A"` (26), `Foreground="White"` (158, chip F8) |
| `Views/BackupView.xaml` | 1 | `Foreground="#374151"` (10) |
| `Views/FirstStartWindow.xaml` | 1 | `Foreground="White"` (16) |

Tres de esos literales son decisiones ya cerradas que están incumplidas en el código:

1. **`#2563EB` (CashView:82)** no existe en la paleta del prototipo. El prototipo usa `--blue-500 #3B82F6 / --blue-soft #EFF6FF / --blue-deep #1D4ED8` (§ :root, líneas 20-22) y el tema oscuro los redefine (`--blue-soft:#111F33`, `--blue-deep:#93C5FD`). **Esos tres tokens no existen en el tema de WPF**: alguien los necesitó y metió el hex a mano.
2. **`Foreground="White"` sobre relleno de marca** (LoginCardView, MainWindow, FirstStart, Splash, chip del botón Cobrar) contradice la decisión #8 ("el texto sobre relleno de marca se calcula: blanco salvo en ámbar → tinta oscura"). El diccionario oscuro **ya define** `OnBrandColor #062F2B` + `OnBrandBrush` (líneas 89-90)… y **nadie los consume** (`grep OnBrand` solo los encuentra ahí). El claro ni los tiene.
3. **`#FDE68A`** (borde ámbar del aviso) está literal en TpvView:26 y CashView:135 junto a `{DynamicResource WarnSoftBrush}` — medio aviso tokenizado y medio a mano; el prototipo lo tiene como valor de `.warn-note`. Debe ser `WarnBorderColor` en ambos temas (en oscuro el borde ámbar sobre fondo oscuro canta).

Dato extra: en el prototipo `--on-brand` **se usa 5 veces pero nunca se declara** en `:root` (la variable queda indefinida y el color de texto se hereda). Es decir, el prototipo tiene la deuda exacta que la decisión #8 vino a cerrar; en WPF hay que declararla explícitamente en los dos temas.

### 3.2 El problema de raíz: `StaticResource` dentro del tema

`FrubberTheme.xaml` está escrito con **`StaticResource` en sus 107 referencias** (por ejemplo el `Style TargetType="Button"` resuelve `Background="{StaticResource PrimaryBrush}"`) y **cero `DynamicResource`**. Las vistas, en cambio, usan `DynamicResource` casi siempre.

Consecuencia, si el oscuro se activa fusionando `FrubberTheme.Dark.xaml` después del claro (que es lo que insinúa el propio fichero: *"se activa en JHO-71 con su toggle persistido"*):

- los `Style` del tema **quedan congelados** con los brushes claros (la referencia se resolvió al cargar el diccionario claro y no cambia),
- los usos en línea de las vistas sí cambian a oscuro,
- resultado: **tema mixto** — tarjetas oscuras con controles claros, texto oscuro sobre fondo oscuro en los sitios donde el estilo manda.

Es un problema silencioso (no lanza excepción, no falla ningún test hoy) y afecta a *todas* las pantallas, no solo a Venta. La reparación es barata **solo si se hace en la misma pasada del bloqueante 1**: convertir las referencias internas del tema a `DynamicResource`, o reestructurar en dos diccionarios con claves idénticas y conmutar el diccionario completo.

### 3.3 Tokens que hay que añadir (propuesta)

| Token | Claro | Oscuro | Para qué (evidencia) |
|---|---|---|---|
| `OnBrandColor` / `OnBrandBrush` | `#FFFFFF` | `#062F2B` (ya existe) | texto sobre teal/degradado de marca (decisión #8) |
| `OnAmberColor` / `OnAmberBrush` | `#78350F` | `#FCD34D` | texto sobre relleno ámbar (decisión #8, badge de licencia/prueba) |
| `BlueColor/Soft/Deep` + brushes | `#3B82F6` `#EFF6FF` `#1D4ED8` | `#3B82F6` `#111F33` `#93C5FD` | KPI "Transferencia" (CashView:82) y estados informativos |
| `WarnBorderColor` + brush | `#FDE68A` | `#4A3A12` | borde de avisos (TpvView:26, CashView:135) |
| `SplashBackdropColor` | `#062F2B` | `#062F2B` | splash siempre oscuro, pero como recurso (SplashWindow:6) |
| `ShadowColor` | `#0F172A` | `#000000` | sombras del `DialogCard`/login (LoginCardView:184, tema:568) |
| `OverlayBackdropColor` | `#0F172A` | `#000000` | cortina del modal de cobro/overlays (LoginCardView:643, prototipo `.overlay`) |

Además, la paridad entre diccionarios está desbalanceada: **32 claves existen solo en el claro** (todos los `Style` y los 3 `DrawingImage` de iconos) y **2 solo en el oscuro** (`OnBrand*`). Los `Style` compartidos son aceptables *si* se corrigen con `DynamicResource`; los iconos son el caso aparte: `IconCheck`, `IconCheckGreen` e `IconKey` **llevan el color dentro del `Pen`** (`#FF0F766E` en la llave, por ejemplo), viven solo en el diccionario claro y no son tokens: en oscuro la llave teal-700 quedaría con contraste bajo sobre superficie oscura. Hay que darles variante oscura o tokenizar el trazo.

### 3.4 Criterio de aceptación (bloqueante 1)

1. **Cero** colores literales/por nombre fuera de `src/FrubberPOS.Desktop/Themes/` (permitidos solo `Transparent` y `{x:Null}`), verificado por un **test nuevo** que falle el build (p. ej. `ColorTokenLintTests`), con un allowlist explícito y comentado si algo se justifica.
2. **Cero `StaticResource`** apuntando a brushes/colores dentro de los diccionarios de tema; el lint lo verifica.
3. **Paridad de claves** entre claro y oscuro para las claves de color/brush (test), y `OnBrand*`/`OnAmber*`/`Blue*`/`WarnBorder*` definidos en ambos.
4. Los pares AA que ya prueba `ThemeTests` siguen en verde **en los dos temas** (extender el test a oscuro: texto/superficie, texto secundario/fondo, blanco o tinta/marca).
5. Nada de esto puede romper las aserciones vigentes: `ThemeTests` fija `PrimaryColor #0F766E`, `AppBackgroundColor #F7F8FA`, `ErrorColor #DC2626` en el claro, y **exige que `App.xaml` NO fusione el oscuro** (`DarkTheme_EstaPreparadoPeroNoSeFusiona_Todavia`). Si esta ronda activa el modo oscuro, ese test se actualiza **a propósito**, no por accidente.

---

## 4. Bloqueante 2 · Anti-doble-cobro y estado "cobrando"

### 4.1 Lo que ya está protegido (no hay que reinventarlo)

- `TpvViewModel.HandleCheckoutAsync` (líneas 337-365) tiene `submitting`: un segundo Enter/clic mientras el flujo está en vuelo no llega al diálogo ni a la finalización.
- `AsyncCommand.Execute` (Common/AsyncCommand.cs) bloquea reentrada mientras corre y expone `IsRunning`.
- `FinalizeSaleUseCase` es una sola unidad de trabajo: verificación de sesión abierta, revalidación de producto, cabecera+líneas+inventario+numeración todo-o-nada.
- Ya existen tests que lo fijan: `SaleFlowRobustnessTests.ReentrantEnter_WhileDialogOpen_CreatesASingleSale`, `DoubleClick_AfterFirstSale_FindsAnEmptyDraft`, `PrinterFailure_LeavesNoSequenceGapNorDuplicate` (1 sola venta, secuencia = 1).

### 4.2 Huecos reales (lo que la revisión llama "anti-doble-cobro y estado cobrando")

1. **El estado no es observable.** `submitting` es `private bool`; ni la vista ni el modal saben que se está grabando. No hay forma de pintar "Registrando venta…", ni de deshabilitar el botón, ni de bloquear Esc.
2. **Se cobra con el modal ya cerrado.** `RunCheckoutAsync` (368-412): `ShowCheckout` devuelve el resultado y *después* se llama a `finalizeSale`. En la ventana entre "el cajero confirmó" y "la venta quedó guardada" la pantalla muestra el TPV normal, con el subtotal todavía en pantalla: si la escritura en el equipo del cliente tarda (disco lento, antivirus, SQLite con lock), el cajero no ve nada y **vuelve a intentar**.
3. **Los errores del cobro aparecen detrás.** Si `finalizeSale` falla (precio que cambió, total inválido, caja que se cerró en paralelo), el mensaje se escribe en `TpvViewModel.ErrorMessage` (209-215, 401) — o sea, en el texto rojo del TPV, no en el modal que acaba de desaparecer. `FullDiscount_IsRejected_AndNothingPersisted` y `PriceConflict_RevalidatesDraftWithoutCorruption` prueban que el borrador sobrevive, pero **la interfaz no le dice al cajero qué hacer**.
4. **No hay camino de reintento definido.** Tras un fallo, ¿se reabre el cobro con el mismo descuento y el mismo recibido, o se pierden? Hoy: se conservan las líneas y el descuento (el `SetDiscount` se aplica antes), pero se pierde el efectivo recibido. Nadie lo decidió por escrito.
5. **La protección depende de la forma del diálogo.** `submitting` + `ShowDialog` (bucle modal anidado) funcionan *porque* el modal es una ventana aparte que deshabilita el owner. Si la R2 convierte el cobro en **capa dentro del shell** (que es lo que muestra el prototipo: `.overlay`), esa garantía desaparece: el teclado del TPV queda vivo debajo del overlay — y ahí es donde el bloqueante 2 y el 3 se juntan.

**Nota de arquitectura:** `INavigationService.ShowCheckout(subtotal, descuento)` es síncrono y devuelve un `CheckoutResultDto` cerrado (`Navigation/INavigationService.cs:50,103`). Con el cobro como capa asíncrona dentro del shell (que necesita mostrar ocupado, errores y reintento), ese contrato debe cambiar: o el modal recibe un `Func<CheckoutResultDto, Task<FinalizeOutcome>>`/`IUseCase` y resuelve la venta dentro, o el flujo pasa a `Task<CheckoutResultDto?> ShowCheckoutAsync(...)`. Es el cambio más invasivo de la ronda y conviene decidirlo antes de escribir XAML.

### 4.3 Criterio de aceptación (bloqueante 2)

Tests nuevos, con nombre, sobre el flujo rediseñado:

1. `DoubleConfirm_WhileSubmitting_CreatesExactlyOneSale` — dos Enter/F9/clics seguidos durante el estado ocupado → `COUNT(*) FROM Sale = 1` y `ShowCheckoutCount` (o su equivalente) = 1.
2. `Submitting_BlocksEscapeAndClose` — Esc y la X no cierran el modal mientras se registra.
3. `Submitting_ShowsBusyStateAndDisablesConfirm` — `IsSubmitting` observable en el ViewModel, botón deshabilitado, texto "Registrando venta…".
4. `FinalizeFailure_KeepsModalOpen_ShowsInlineError_DraftIntact` — el fallo se muestra **dentro** del modal y las líneas siguen ahí.
5. `RetryAfterFailure_DoesNotDuplicateNorLoseDiscount` — reintento explícito: 1 venta, descuento y recibido conservados.
6. `CobroConfirmado_ClearsDraftOnce_AndReleasesBusyState` — el estado ocupado nunca queda colgado (incluido el caso de excepción).

---

## 5. Bloqueante 3 · Precedencia del teclado

### 5.1 Matriz actual (medida en el código)

| Tecla | Dónde se maneja hoy | Dueño efectivo | Problema |
|---|---|---|---|
| F2 | `TpvView.OnPreviewKeyDown` (34), también `ProductsView` (27) | vista activa | Con un modal/capa abierta, si el foco vuelve a la vista, F2 roba el foco al buscador |
| F4 / F6 / F8 | `TpvView.OnPreviewKeyDown` (62, 69, 76) | TPV | **Sin comprobación de "hay algo encima"** |
| F9 | `ConfirmActionDialog` (37) — **y nada en el cobro** | inconsistente | Ver 5.2 |
| F10 | `MainWindow.OnKeyDown` (70-91), se ignora si el bloqueo está visible | shell (bubble) | No conoce overlays distintos al bloqueo |
| Supr | `TpvView` (55), solo si el foco **no** está en el buscador | TPV | Correcto hoy, frágil si el foco se mueve a otro campo |
| Enter | `TpvView` (42), solo si el foco está en el buscador; `IsDefault` en el cobro | vista + botón | Dos dueños potenciales |
| Esc | `LoginCardView` (63), `LicenseWindow` (94), `IsCancel` en diálogos | varios | En el TPV no hace nada (el prototipo limpia la búsqueda) |

### 5.2 Defecto comprobado: F9 no hace nada en el cobro

`Views/CheckoutDialog.xaml` dice, en el texto de ayuda (línea 104) *"F9 confirma · Esc vuelve sin perder la venta"* y en el botón (108) `Content="Confirmar  F9"`. El botón es `IsDefault="True"` (109), y `IsDefault` en WPF responde a **Enter**, no a F9. El code-behind (`CheckoutDialog.xaml.cs`) **no tiene ningún manejador de teclado**; el comentario de cabecera incluso afirma *"Enter/F9 confirms via IsDefault"* (línea 8). Comparado con `ConfirmActionDialog.xaml.cs:37`, que sí tiene `if (e.Key == Key.F9) OnConfirm(...)`, el cobro es la excepción.

Es exactamente el tipo de incoherencia que la revisión quiere cerrar: **un atajo anunciado en pantalla que no existe**. En una ronda de teclado esto es lo primero que hay que arreglar o quitar del texto.

### 5.3 El prototipo tampoco es ejemplo aquí

El manejador global del prototipo (`frubber-pos.html:5301-5334`) devuelve temprano solo para el bloqueo y el modal de licencia:

```js
if(lkIsOpen()) return lkKey(e);
if(document.getElementById("licModal").classList.contains("show")) return;
...
if(e.key === "F4"){ ... setQtyPrompt(); return; }   // ← se dispara también con mCobro abierto
if(e.key === "F8"){ ... openCobro(); return; }      // ← y vuelve a abrir el cobro
```

Con el modal de cobro abierto, F4/F6/F8 siguen actuando por debajo, y `confirmarCobro()` no tiene guarda de "en vuelo" (es sincrónico y cierra el modal, así que la protección es accidental). **La app debe ser más estricta que el prototipo**, no copiarlo: la regla que sugiere la revisión (un dueño por tecla, con precedencia explícita) es la correcta.

### 5.4 Regla propuesta (para implementar una sola vez y documentar)

Precedencia estricta, de arriba hacia abajo; cada capa decide si consume la tecla y **nada por debajo se entera**:

| Orden | Capa | Teclas | Comportamiento |
|---|---|---|---|
| 1 | Bloqueo de sesión | todas | `lkKey`/tarjeta de PIN (ya funciona así) |
| 2 | Overlay/modal del shell (cobro, confirmaciones, licencia) | **todas** | Solo el overlay decide: Enter/F9 confirman, Esc cancela (salvo "cobrando"), F2/F4/F6/F8/F10 **se tragan** |
| 3 | Campos de texto con foco | Enter, Esc, Supr, F-keys del campo | Enter agrega (buscador), Esc limpia la búsqueda, Supr **no** borra líneas |
| 4 | Pantalla activa (TPV) | F2, F4, F6, F8, Supr | Solo si el foco no está en un campo |
| 5 | Shell | F10 | Cambia a Caja |

Cómo hacerlo verificable: un único punto de decisión (p. ej. `ShellKeyRouter`/`IKeyboardScope` inyectado, o `MainWindow.OnPreviewKeyDown` que pregunte "¿qué capa está activa?" antes de delegar), en lugar de repartir `if (F4)` por cada vista. Así el test puede ser tabulado: *(capa, tecla) → manejada/no manejada*, sin depender de un teclado real.

### 5.5 Criterio de aceptación (bloqueante 3)

1. **F9 confirma de verdad** en el cobro (o el texto deja de mencionarlo). Test: `F9_ConfirmsCheckout`.
2. Con el cobro abierto: F2/F4/F6/F8/F10 **no** cambian nada por debajo. Test tabulado por tecla.
3. Con el bloqueo activo: ningún atajo del TPV responde (ya se cumple; queda como test de regresión).
4. "Cobrando": Esc, X, Enter y F9 no alteran el estado.
5. Esc en el buscador del TPV limpia la búsqueda como en el prototipo (5327-5330) y **no** descarta la venta.
6. La matriz de atajos queda escrita en el código (una tabla, no `if`s sueltos) y **visible en pantalla**, como el prototipo: *"Atajos: F2 buscar · ↑↓ seleccionar · ENTER agregar · F4 cantidad · SUPR quitar · F6 descuento · F8 cobrar"* (frubber-pos.html:1699-1701).

---

## 6. Las 7 mejoras (reconstruidas — hay que validarlas contra el documento original)

Las infiero de la revisión del traspaso + la evidencia del código. **Márcalas o corrígelas cuando me pases `revision-plan-JHO-67.md`.**

| # | Mejora propuesta | Evidencia de que hace falta | Coste |
|---|---|---|---|
| 1 | El cobro como **capa dentro del shell** (no `Window`), con estado ocupado, errores en línea y teclado encapsulado | prototipo `.overlay` + `INavigationService.ShowCheckout` síncrono; §4.2-5 | M |
| 2 | **Paridad visual del TPV** con el prototipo: placeholder del buscador, contadores ("N productos", "N ítems"), empty states con acción, leyenda de atajos | TpvView.xaml hoy no tiene nada de eso (`grep` de "placeholder"/"vacío": 0) | M |
| 3 | **Botón Cobrar con importe** ("Cobrar F8 $ 21.800") y totales en el orden del prototipo | frubber-pos.html:1709-1712 vs banda actual | S |
| 4 | **Modal de cobro rediseñado**: recap con fila "Ítems", chips de medio de pago en rejilla 2×2 con icono, caja de diferencia con 3 estados (escribe · cambio · pago exacto), nota ámbar de consecuencias | CheckoutDialog actual: 2 radios sin icono, cambio siempre verde, sin fila Ítems, sin nota | M |
| 5 | **Modo oscuro real** en TPV y cobro, con el lint del bloqueante 1 | §3 | L |
| 6 | **Foco y accesibilidad**: foco inicial correcto en el modal (hoy va a `DiscountBox`, no a "Efectivo recibido"), `AutomationProperties.Name` en todo lo nuevo, foco que no se pierde al refrescar resultados | CheckoutDialog.xaml.cs:25-26; TpvView refresca en cada `Loaded` | S |
| 7 | **Evidencia de ronda**: capturas antes/después de Venta y Cobro (claro y oscuro) con `ViewPreviewTests` + tests nuevos en verde | `StaViewHarness.SavePng` ya existe y el traspaso maneja ~140 capturas de evidencia por ronda | S |

*Extra que yo añadiría a la lista (no sé si estaba):* el mensaje de error del cobro debe vivir **en el modal**, no en `TpvViewModel.ErrorMessage`, y el banner de "caja cerrada" del TPV debería llevar la acción que muestra el prototipo (ver §9.3).

---

## 7. Delta prototipo → app · pantalla Venta

| Zona | Prototipo (`frubber-pos.html`) | App hoy (`TpvView.xaml`) | Acción R2 |
|---|---|---|---|
| Aviso de caja cerrada | banner *danger* rojo, título "La caja está cerrada", texto "No se pueden registrar ventas…", botón **"Abrir nueva caja →"** (1155-1163) | borde ámbar (`WarnSoftBrush` + `#FDE68A`), texto "Sin caja abierta: puede preparar la venta, pero no podrá cobrarla. Presione F10…" (24-31, `TpvViewModel.NoCashWarningText`) | **Decidir** (§9.3): el prototipo dice "no se pueden registrar ventas" (más estricto que P-UX-010) y ofrece acción |
| Buscador | 44 px, lupa, placeholder *"Escribe el nombre o código del producto..."*, chip F2 (1165-1170) | 42 px, lupa, chip F2, **sin placeholder** (9-19) | Igualar placeholder + altura |
| Resultados | encabezado "Resultados" + **contador** ("1 producto"/"—") y **empty state** (icono, "Busca un producto para empezar", "Usa el nombre, el PLU o escanea el código de barras") (1172-1192) | encabezado "RESULTADOS" en mayúsculas, **sin contador ni empty state** | Añadir contador + empty state |
| Venta en preparación | "Venta en preparación" + contador "0 ítems" + empty state ("Tu ticket está vacío") + columna de acciones por fila (1193-1216) | "VENTA EN PREPARACIÓN" sin contador ni empty state; acciones en botonera inferior | Añadir contadores/empty state |
| Acciones | Limpiar · Cantidad F4 · Quitar SUPR · Descuento F6 · **Agregar ENTER** (teal outline) (1690-1697) | Las cinco, con chips, y "Agregar" teal (120-137) | Ya alineado |
| Leyenda de atajos | línea completa bajo las acciones (1698-1701) | **no existe** | Añadir |
| Totales | Subtotal / "Descuento −$ 0 (10%)" / TOTAL | Subtotal / Descuento / TOTAL 28 pt (test fija ese 28) | Mantener 28 pt salvo decisión |
| Botón cobrar | grande (52 px) con **F8 y el importe dentro**: "Cobrar F8 $ 21.800" (1709-1712) | botón con chip F8, importe **fuera** (146-160) | Mover el importe al botón |
| Descuento | **porcentaje** 0-100 % (`discountPrompt`, 2705-2708) | **COP enteros** (`DraftSale.SetDiscount(discountCop)`, ADR-009) | **Decidir** (§9.1): no cambiar el dominio por el prototipo |

---

## 8. Delta prototipo → app · modal de cobro

| Elemento | Prototipo (1722-1752) | App hoy (`CheckoutDialog.xaml`) |
|---|---|---|
| Ancho / radio | 470 px, radio 16, cabecera con icono verde de check y subtítulo "N ítems · $ X" | 560 px, radio 14 (`DialogCard`), cabecera de texto + X |
| Recap | 4 filas: **Ítems**, Subtotal, Descuento, **Total a cobrar** en bloque | Subtotal, Descuento **editable**, caja TOTAL grande; **sin fila Ítems** |
| Medio de pago | rejilla 2 columnas, chips con icono, Efectivo preseleccionado | 2 radios en fila, sin icono |
| Efectivo recibido | campo solo si el medio es Efectivo (`recibidoField`) | campo condicional (`ShowCashFields`) — ya alineado |
| Diferencia | caja con 3 estados: *"Escribe el efectivo recibido"* · *"Cambio $ X"* · *"Pago exacto por transferencia"*; y en rojo si falta | caja "CAMBIO" **siempre verde**, incluso en 0 o insuficiente (el botón se bloquea) |
| Nota de consecuencias | nota ámbar: *"Al confirmar: se descuenta el stock, la venta entra al Historial y suma al efectivo esperado de Caja"* | **no existe** |
| Pie | "Cancelar" + "Confirmar cobro" | "Cancelar" + "Confirmar  F9" (**F9 muerto**, §5.2) |
| Foco inicial | implícito: el cajero elige medio y escribe el recibido | `DiscountBox` (descuento), que en el prototipo no vive en este modal |

---

## 9. Decisiones que hay que cerrar antes de codificar

### 9.1 Descuento: ¿porcentaje o pesos?
Los dos prototipos usan **%** (`state.sale.disc`, 0-100). La app usa **COP enteros** (`SetDiscount(discountCop)`, ADR-009, dinero nunca en coma flotante) y así lo fijan los tests. Mezclar ambos (pedir % y guardar COP) es posible, pero **hay que decidirlo por escrito**: cambia textos, validaciones, el ticket y el historial.

### 9.2 Medios de pago: ¿dos o configurables?
El prototipo trae medios configurables con Nequi/Daviplata/Tarjeta (`payMethods` 3807-3812, usados en Historial: `pay:"Tarjeta"`). La app **no puede** hacerlo hoy: `001_Initial.sql:143` impone `CHECK (PaymentMethod IN ('CASH','TRANSFER'))`, `AGENTS.md §6` prohíbe pagos combinados, y `FinalizeSaleUseCase` valida `"CASH" or "TRANSFER"`. Si R2 quiere la rejilla de medios del prototipo, primero hay que decidir si (a) solo se cambia la *presentación* de los dos existentes, o (b) entra una migración de medios de pago (fuera de JHO-67, otro issue).

### 9.3 Aviso de caja cerrada
El prototipo dice *"No se pueden registrar ventas"*; P-UX-010/JHO-44 dice *"solo bloquea cobrar"*. Hoy manda el código (se puede preparar la venta). Recomiendo mantener P-UX-010 y **adoptar solo el aspecto y la acción** del banner (ir a Caja). Necesita tu OK porque es una decisión ya cerrada.

### 9.4 Alcance del modo oscuro en esta ronda
`ThemeTests` exige hoy que `App.xaml` **no** fusione el oscuro (era el estado "preparado" de JHO-69/71). R2 toca el TPV y el cobro: ¿se activa el oscuro solo donde se puede verificar (TPV+cobro, con el resto de pantallas ya tokenizadas) o se espera a JHO-71? Mi recomendación: **cerrar el bloqueante 1 completo** (tokens + lint + `DynamicResource`) y activar el conmutador en JHO-71, que ya tiene el toggle persistido como dueño.

### 9.5 ¿"Cobrando" con la venta ya grabada, o antes?
Recomendación: el estado ocupado cubre **desde que el cajero confirma hasta que la venta quedó guardada y el ticket se ofreció**, y el bloqueo de Esc aplica solo a la parte de escritura (una vez guardada, Esc puede cerrar).

### 9.6 Teclado: ¿la leyenda de atajos se queda fija o se puede ocultar?
El prototipo la muestra siempre (una línea gris). En mostrador es útil; si el dueño la quiere ocultable, es un ajuste en Configuración (hoy no existe).

### 9.7 ¿Quién es el dueño del "F9" en toda la app?
Hoy conviven: `IsDefault` (Enter) en varios diálogos, `Key.F9` explícito en `ConfirmActionDialog`, y F9 anunciado pero ausente en el cobro. Propongo la regla: **F9 confirma siempre, Enter también en el botón por defecto, Esc cancela**, y un test que lo verifique en cada diálogo.

### 9.8 Simulación de venta: ¿se puede probar sin caja abierta?
El flujo de cobro requiere caja abierta (P-UX-010). Para las capturas de evidencia no es problema: `ViewPreviewTests` ya rinde el TPV sin caja (el aviso no aparece porque el estado arranca sin comprobar) y los tests de flujo (`SaleFlowRobustnessTests`, `CheckoutTests`) **abren la sesión insertando la fila por SQL directo**, así que la evidencia no depende de la UI. Si el dueño quiere además un modo de entrenamiento para el cajero, es otro issue.

---

## 10. Plan propuesto (4 fases, con lo que se toca)

| Fase | Qué entra | Archivos | Verificación |
|---|---|---|---|
| **F0 · Tokens y lint** (bloqueante 1) | 7 tokens nuevos en ambos temas, `DynamicResource` interno, tokenizar los 77 literales, test de lint + paridad + AA oscuro | `Themes/FrubberTheme.xaml`, `Themes/FrubberTheme.Dark.xaml`, las 9 vistas con literales, `tests/.../ThemeTests.cs` (+ `ColorTokenLintTests.cs` nuevo) | `dotnet test --filter ThemeTests\|ColorTokenLint` y captura de TPV en claro/oscuro |
| **F1 · Venta R2** | placeholder, contadores, empty states, leyenda de atajos, Cobrar con importe, banner de caja según 9.3 | `TpvView.xaml(.cs)`, `TpvViewModel.cs`, `Themes` | `ViewPreviewTests` → PNG de Venta (claro/oscuro) + tests de ViewModel |
| **F2 · Cobro R2** (bloqueante 2) | capa dentro del shell (o modal rediseñado), recap con Ítems, chips de medios, caja de diferencia con estados, nota ámbar, estado "cobrando", errores en línea, contrato de navegación | `CheckoutDialog.xaml(.cs)`, `CheckoutViewModel.cs`, `INavigationService.cs` + `App.xaml.cs`, `TpvViewModel.cs`, `MainWindow.xaml(.cs)` si es overlay, `FakeNavigator.cs` | los 6 tests de §4.3 + PNG del modal en 3 estados (inicial, cobrando, error) |
| **F3 · Teclado y cierre** (bloqueante 3) | enrutador de teclado con precedencia única, F9 real, Esc coherente, tests tabulados, capturas de evidencia antes/después | `MainWindow.xaml.cs`, vistas y diálogos, tests nuevos | tests tabulados + `MessageConsistencyTests` (que los textos de atajos no mientan) |

**Orden obligatorio:** F0 antes que F1/F2 (si no, se pinta dos veces cada pantalla y el modo oscuro queda a medias — que es justo la regla de oro del traspaso), y F2 antes que F3 (el dueño del teclado depende de si el cobro es capa o ventana).

**Tamaño estimado:** ~35-45 archivos tocados, en línea con el criterio de "un lote de ~45 archivos por commit" de `AGENTS.md §7`.

---

## 11. Cómo verificar en este entorno (limitaciones honestas)

- **Aquí no se puede compilar**: no hay .NET SDK y NuGet/Microsoft están bloqueados. Puedo escribir el código, los tests y los XAML, y validarlos estáticamente (XML bien formado, lint de colores propio en Python, paridad de tokens, búsqueda de recursos no definidos — ya tengo los scripts y hoy dan **0 recursos usados sin definir**), pero **el `dotnet build`/`dotnet test` tiene que correrlo el dueño en Windows** (o desbloquear el acceso a los feeds de Microsoft).
- **Lo que sí puedo entregar como evidencia** en cuanto al build: los PNG del harness (`ViewPreviewTests` → `%TEMP%\opencode\frubber-preview`), que rinden las vistas reales sin abrir la app.
- **Checklist manual sugerido para el dueño** (10 minutos, en el PC de mostrador):
  1. F8 con 0 ítems → no pasa nada y el mensaje es llano.
  2. F8 con caja cerrada → no abre el cobro y el aviso es claro.
  3. Cobro en efectivo con $ 60.000 sobre $ 21.800 → **cambio $ 38.200** (número canónico del traspaso).
  4. Cobrar por transferencia → el campo de efectivo desaparece y el cambio no se muestra.
  5. Doble Enter y doble clic en "Confirmar cobro" → **una sola venta** en Historial, un solo número de ticket.
  6. Esc durante la grabación → no cancela ni deja la pantalla bloqueada.
  7. F9 dentro del cobro → confirma (hoy no hace nada).
  8. F4/F6/F8/F10 con el cobro abierto → nada responde por debajo.
  9. Cambiar a modo oscuro → **TPV y cobro completos** en oscuro (ninguna tarjeta clara, ningún texto ilegible).
  10. Precio de un producto cambiado antes de cobrar → error claro dentro del modal y el borrador intacto.
  11. Sin impresora → "La venta V-XXXXXX quedó guardada, pero el ticket no pudo imprimirse" (nunca se cobra dos veces).
  12. Cerrar y reabrir el cobro → descuento y líneas siguen ahí.

---

## 12. Resumen para la reunión (5 viñetas)

1. Los 3 bloqueantes se confirman; **hay un cuarto** (el tema claro usa `StaticResource` → el oscuro no repintaría los estilos) que hay que arreglar en la misma pasada que el bloqueante 1.
2. **Defecto concreto:** el modal de cobro anuncia "F9 confirma" y F9 no hace nada; el resto de diálogos sí lo cumplen.
3. La lógica de venta no se toca: está probada y aguanta. R2 es tokenizar, alinear la vista con el prototipo y reforzar el flujo de cobro (estado ocupado + errores en línea + una sola venta).
4. Hay **4 conflictos prototipo ↔ reglas** que necesitan tu decisión: descuento % o COP, medios de pago configurables vs CHECK `CASH/TRANSFER`, banner de caja cerrada rojo vs P-UX-010, y si el oscuro se activa ya o en JHO-71.
5. Sin `revision-plan-JHO-67.md` a mano, las 7 mejoras de §6 son mi reconstrucción: en cuanto me lo pases, alineo numeración y criterios sin reabrir lo ya cerrado.

---

## Anexo A · Los 77 colores crudos fuera del tema (inventario completo)

<detalle>
`LicenseWindow.xaml` (41): 16 `#F8FAFC` · 17 `#E2E8F0` · 39 `#F1F5F9` · 48 `#475569` · 58 `#B91C1C` · 62 `#FEF2F2`/`#FECACA` · 69 `#FEE2E2` · 83 `White` · 88 `#14B8A6` · 94 `#0D9488` · 97 `#E2E8F0` · 98/98 `#94A3B8` · 111 `White` · 141 `#475569` · 146 `#FFFFFF`/`#E2E8F0` · 153 `#F8FAFC` · 154 `#14B8A6` · 155 `#0F766E` · 163 `#065F46` · 167 `#D1FAE5`/`#10B981` · 186 `#FFFFFF` · 187 `#E2E8F0` · 191 `#14B8A6` · 194 `#FEF2F2` · 195 `#FECACA` · 239 `#BBF7D0` · 248 `#14532D` · 250 `#166534` · 268 `#F8FAFC` · 283/330/332/336/338/342 `#94A3B8` · 309 `#D1FAE5` · 329/335 `#64748B` · 370 `#FFFBEB` · 372 `#D97706`

`SplashWindow.xaml` (13): 6 `#062F2B` · 59 `#35B2A6` · 61/66 `White` · 68 `#94A3B8` · 74-84 `#35B2A6` ×6 · 89 `#94A3B8`

`LoginCardView.xaml` (8): 109 `White` · 184 `#0F172A` · 200 `White` · 360 `White` · 512 `White` · 602 `White` · 643 `#0F172A` · 646 `White`

`BusinessSettingsView.xaml` (6): 10/17/24/31/38 `#B45309` · 47 `#6B7280`

`MainWindow.xaml` (3): 33/97/179 `White`

`CashView.xaml` (2): 82 `#2563EB` · 135 `#FDE68A`

`TpvView.xaml` (2): 26 `#FDE68A` · 158 `White`

`BackupView.xaml` (1): 10 `#374151` · `FirstStartWindow.xaml` (1): 16 `White`
</detalle>

## Anexo B · Mapa de tokens (claro ↔ oscuro)

- **Presentes en ambos (31 `<Color>` + 30 brushes en claro; 32 + 31 en oscuro):** `AppBackground`, `Surface`, `Primary`, `PrimaryActive`, `Accent`, `AccentSoft`, `Text`, `SecondaryText`, `MutedText`, `TextSub`, `Border`, `BorderSoft`, `BorderHover`, `Success`, `Warning`, `Error`, `Focus`, `Subtle`, `Mint`, `MintHover`, `MintRow`, `MintBorder`, `GreenDot`, `WarnSoft`, `ErrorSoft`, `ErrorBorder`, `ErrorHover`, `SuccessSoft`, `Trial`, `TrialText`, `OfflineText` (+ sus brushes y los degradados de marca `BrandGradient`, `AvatarGradient`, `LoginBackdrop`).
- **Solo en claro (32 claves):** `AppFont`, `Card`, `DialogCard`, `PillMint`, `PillWarn`, `PillTrial`, `PillText`, `KpiCard`, `KpiMetric`, `AvatarCircle`, `BrandLogo`, `NavMenuItem`, `NavKeyChip`, `NavCountBadge`, `BigPrimaryButton`, `SecondaryButton`, `DangerButton`, `PadButton`, `ShortcutChip`, `ShortcutText`, `PageTitle`, `PageSubtitle`, `ColumnCaption`, `FieldLabel`, `ErrorText`, `NumericText`, `DisplayTotal`, `FocusVisual`, `IconCheck`, `IconCheckGreen`, `IconKey`, `SplashLogoGradient`.
- **Solo en oscuro (2):** `OnBrandColor`, `OnBrandBrush` — sin consumidores.
- **Faltan en WPF (existen en el prototipo):** `blue-*` (3), `OnBrand` explícito, `OnAmber`, borde ámbar de avisos, fondo de splash y color de sombra/cortina (§3.3).
- **Premisa de calidad ya verificada:** hoy **no hay ni una referencia a recurso inexistente** en el Desktop (0 hallazgos); el riesgo del rediseño no es el recurso que falta, es el literal que sobra.

## Anexo C · Matriz de atajos (hoy vs propuesta)

| Tecla | Hoy | Propuesta | Dónde |
|---|---|---|---|
| F2 | Buscar (TPV, Productos) | Buscar en la pantalla activa; **inactiva** si hay overlay | capa 4 |
| F4 | Cantidad (TPV) | Igual, solo si no hay overlay ni foco en campo | capa 4 |
| F6 | Descuento (TPV) | Igual | capa 4 |
| F8 | Cobrar (TPV) | Igual; **no** si ya se está cobrando | capas 2/4 |
| F9 | Confirma en `ConfirmActionDialog`; **muerto en el cobro** | Confirma en todo diálogo/overlay (incluido el cobro) | capa 2 |
| F10 | Va a Caja (shell) | Igual; **inactiva** con overlay abierto | capa 5 |
| Esc | Cancela diálogos; limpia PIN/licencia; nada en el TPV | Cancela overlay (salvo "cobrando"); limpia la búsqueda del TPV | capas 2/3 |
| Supr | Quita la línea si el foco no está en el buscador | Igual, y **nunca** con overlay | capas 2/4 |
| Enter | Agrega si el foco está en el buscador; `IsDefault` en diálogos | Igual, con dueño único por capa | capas 2/3 |
