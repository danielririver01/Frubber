# JHO-127 · Ventas del día

El administrador puede ver la venta neta del día por puesto y el total del negocio. Se suman las ventas vigentes, en efectivo y por transferencia, después de descuentos. El saldo inicial de caja queda fuera del total.

El día se determina con la fecha y la zona horaria del servidor PostgreSQL, desde la medianoche inclusive hasta la siguiente medianoche. Se muestran los puestos activos aunque estén en cero y los puestos que registraron ventas ese día aunque luego se hayan desactivado.
