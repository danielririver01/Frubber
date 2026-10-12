# Frubber POS 1.0 — Guía de instalación y operación (JHO-54)

Documento para el dueño y quien instale el equipo. No requiere conocimientos
técnicos ni internet.

## 1. Requisitos

- Computador con **Windows 10 u 11 de 64 bits**.
- Pantalla de **1366 × 768 o más** (se recomienda 100 %, 125 % o 150 % de escala).
- Impresora de tickets instalada en Windows (opcional; se puede vender sin imprimir).
- Báscula comercial por puerto serial (RS232) o cable USB con chip HL-340/CH340 (opcional).

## 2. Instalación

1. Ejecutar `FrubberPOS-Setup-1.0.0.exe` (lo genera `installer\build-installer.ps1`
   con Inno Setup 6; el script vive fuera de la solución).
2. Aceptar el destino sugerido (Archivos de programa). La app crea sola, en el
   primer arranque, su carpeta de datos en `C:\ProgramData\FrubberPOS`.
3. Al primer inicio, el asistente crea el **primer administrador** con su PIN de
   4 dígitos. No existe PIN predeterminado.
4. La licencia se activa con la clave que entrega el desarrollador
   (funciona sin internet; vale de por vida en ese computador).

## 3. Operación básica

- **Caja:** abrir con saldo inicial antes de vender; cerrar al final del día
  (el cierre no pide conteo).
- **Venta (TPV):** buscar por PLU o nombre; en productos por kg/lb se digita el
  peso o se toma de la báscula con «Leer báscula»; cobrar en efectivo (calcula
  el cambio) o transferencia; imprimir es opcional.
- **Historial (administrador):** consulta por fechas y anulación de ventas con
  la caja aún abierta (reintegra inventario; nunca borra el registro).
- **Productos (administrador):** crear, editar, activar/desactivar, ajustar
  existencia y operaciones masivas.
- **Respaldo (administrador):** botón manual a la carpeta elegida; el resultado
  se informa en pantalla. La restauración la hace el desarrollador, nunca desde
  la interfaz.
- **Báscula (administrador):** en Configuración → Báscula elegir puerto COM,
  velocidad (9600 por defecto), protocolo y unidad; «Probar lectura» confirma la
  conexión antes de guardar.

## 4. Desinstalación

Desinstalar quita el programa pero **conserva siempre** los datos del negocio
(`C:\ProgramData\FrubberPOS` con la base, los respaldos y la licencia). Para
borrarlos hay que eliminar esa carpeta a mano, a propósito.

## 5. Trazabilidad de la versión

Los binarios de la 1.0 corresponden al código etiquetado `v1.0.0` y a la suite
de pruebas aprobada (699/699). El artefacto no incluye servidor, nube,
suscripciones ni módulos excluidos del alcance.
