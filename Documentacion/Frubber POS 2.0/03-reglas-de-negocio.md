# Reglas de negocio de Frubber POS 2.0

Versión 2.0 · 19 de noviembre de 2026

- **R-1 y R-2.** Una caja abierta por puesto. Cada cajero abre y cierra la suya. El efectivo esperado suma el saldo inicial y las ventas vigentes en efectivo de esa caja. Las transferencias se muestran aparte.
- **R-3.** Solo el administrador puede anular una venta de cualquier puesto mientras la caja de origen esté abierta. La operación se ejecuta contra el servidor.
- **R-4.** El informe diario consolida la venta neta por puesto y por negocio. Incluye efectivo y transferencias de las ventas vigentes del día después de descuentos, con el mismo criterio de venta neta del cierre de caja. Los saldos iniciales no forman parte de las ventas.
- **R-5.** Los puestos comparten los usuarios del negocio. El PIN tiene cuatro dígitos y el borrado requiere ausencia de histórico.
- **R-6.** Una licencia vitalicia por negocio enlazada al servidor. FRB1 autoriza una caja. FRB2 declara el cupo firmado. Ampliar una FRB1 requiere FRB2. La prueba dura 30 días por negocio y habilita hasta dos cajas conectadas al mismo PostgreSQL. Comparten el plazo, que se conserva al agregar o sustituir puestos.
- **R-7.** Se permite inventario negativo según las reglas de la 1.0. El ajuste manual de existencias corresponde al administrador.

En una migración, soporte busca primero la FRB1 completa en el historial de emisiones. Si no está disponible, el dueño reemite la FRB1 para el mismo equipo, con la autorización de una caja. Un cambio de servidor se tramita con el código del nuevo servidor. Los puestos comparten las reglas de la aplicación y el estado del motor.

Esta revisión incorpora R-1 a R-7. Las reglas de la 1.0 ajenas a estas enmiendas siguen como base.
