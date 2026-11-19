# Especificación de requisitos de software

Versión 2.0 · 19 de noviembre de 2026

## Operación y seguridad

- **S-1 y S-2.** Los puestos se conectan al motor PostgreSQL por la red local. El sistema opera sin internet. Solo el servidor inicializa y migra el esquema.
- **S-3.** Cada puesto usa un rol distinto con permisos mínimos y contraseña protegida mediante DPAPI en su cuenta Windows. El PIN de cuatro dígitos se verifica mediante funciones SECURITY DEFINER. Se conserva el límite de intentos de autenticación. El motor exige TLS y el cliente verifica identidad y certificado con VerifyFull.
- **S-4.** La licencia se vincula al código del servidor. FRB1 autoriza una caja y FRB2 usa el cupo firmado. Se verifica firma, equipo y cupo en el arranque y antes de abrir caja o vender. La prueba de 30 días es única por negocio y la hora del motor sirve de referencia. El cambio de servidor requiere una licencia emitida para el código de destino.
- **S-5 y S-6.** Cada puesto tiene como máximo una caja abierta. El administrador puede anular desde cualquier puesto mientras siga abierta la caja de origen.
- **S-7.** El consolidado muestra el total del día por puesto y del negocio, dentro de la aplicación.
- **S-8 y S-9.** El administrador autoriza, lista y desactiva puestos, consulta el cupo y revisa la conexión y la última comunicación. El puesto permite reintentar la conexión.
- **S-10.** Si el servidor no responde, el puesto muestra el estado e impide iniciar operaciones que dependan de él. Un cobro de resultado desconocido se recupera usando su identificador original.

## Continuidad de FRB1

Soporte busca primero la FRB1 firmada en el historial de emisiones. Si falta, el dueño la reemite para el mismo equipo con cupo de una caja. El migrador valida firma e identidad del servidor antes de importar. Ver [Licenciamiento, red y seguridad](paquete/2.4-licenciamiento-red-y-seguridad.md).

Esta revisión incorpora S-1 a S-10. Los requisitos de la 1.0 ajenos a estas enmiendas siguen como base.
