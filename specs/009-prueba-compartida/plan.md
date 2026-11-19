# Plan

1. Comprobar el comportamiento desde los casos de uso sobre PostgreSQL de laboratorio, con dos puestos y el mismo reloj del motor.
2. Aplicar una migración aditiva del estado operativo con permisos de lectura acotados al puesto. Reutilizar el estado de prueba importado de la 1.0 y la verificación ECDSA.
3. Comparar el reloj local en UTC con el del servidor. Para la diferencia entre equipos, aceptar hasta cinco minutos en ambos sentidos. Medir los 30 días como 720 horas del reloj del servidor.
4. Verificar los cupos en altas concurrentes, el bloqueo con una sesión abierta, la integridad de los registros y la activación tras vencer la prueba.
