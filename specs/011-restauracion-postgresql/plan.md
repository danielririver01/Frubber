# Plan

Reutilizar el formato de los respaldos diarios y verificar la restauración en un PostgreSQL 18.6 aislado. Pedir a otra persona seguir la guía en un PC aparte y documentar la fecha y el resultado.

En un motor nuevo, soporte revisa el archivo de roles antes de restaurar y aplica únicamente los roles y las membresías de Frubber. Los permisos guardados en el archivo de respaldo se conservan. La cuenta administrativa y los roles ajenos del equipo de destino se gestionan por separado.
