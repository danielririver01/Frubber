# Plan

Reutilizar el detalle y la confirmación de Historial. Validar el token administrativo de la caja que solicita la anulación. Bloquear la fila de la caja de origen durante la anulación para coordinar con su cierre. Mantener el registro de anulación y la reintegración de existencias en una sola transacción.

Verificar en PostgreSQL el rechazo a un cajero, la anulación por un administrador conectado desde otra caja, el rechazo con la caja de origen cerrada y el inicio de sesión desde otro puesto con un usuario compartido. Repetir la prueba de vencimiento compartido.
