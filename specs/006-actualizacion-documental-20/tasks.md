# Tareas de JHO-132

- [x] Identificar los documentos oficiales, la versión PostgreSQL de 2.2 y las enmiendas del plan 6.1.
- [x] Contrastar el acta 3.6 y ADR-030 con el código que guarda la licencia y con la migración verificada en JHO-124.
- [x] Registrar la decisión del dueño para instalaciones que solo conservan el respaldo sin firma.
- [x] Actualizar y verificar las siete revisiones oficiales, las propuestas, el registro 3.3, el índice 4.0 y las redacciones vigentes de 5.0.
- [x] Publicar el paquete versionado en el repositorio y sincronizarlo en [Drive](https://drive.google.com/file/d/1WGEMzSJLdaN1AvqAOvgGBkIGku9Kjl4X/view).
- [x] Verificar JHO-118 contra la Arquitectura y la enmienda A-7. LicenseManager emitió FRB1 de una caja y FRB2 con cupos de 1, 2 y 2147483647 usando una clave temporal. El parser y verificador Release aceptaron las cuatro firmas. Rechazaron un cupo alterado después de firmar y cupos 0, 02 y 2147483648. Pasaron 23 pruebas de formato y verificación ECDSA.
