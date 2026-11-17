# Emisión y reemisión de licencias · JHO-117

LicenseManager se opera en el equipo del dueño. Su backend conserva la configuración de firma. El código de equipo se toma del servidor de Frubber POS y los parámetros de comprobación del identificador y de la clave pública se toman de la versión de Frubber que usará el cliente.

## Emitir FRB2

1. Confirmar los 16 caracteres del equipo servidor y el número de puestos contratados.
2. Desde el backend de LicenseManager y con su entorno Python activo, ejecutar el emisor local. Elegir un archivo de salida nuevo.

   ```powershell
   python -m license_manager.crypto.frb2 <codigo-servidor> --seats <cupo> --expected-key-id <id-publico> --expected-public-key <clave-publica-base64> --output <archivo-nuevo.frb2>
   ```

3. Entregar el archivo firmado al operador autorizado para registrarlo en el servidor del negocio. En Frubber 2.0, verificar que los puestos autorizados arranquen con el perfil protegido de ese servidor.

## FRB1 y reemisión

Para una instalación FRB1, usar el flujo de emisión existente de LicenseManager con su identificador de clave actual. Una FRB1 autoriza una caja. Para ampliar el cupo o cambiar el servidor con FRB2, emitir un archivo nuevo con el código y cupo vigentes. La firma cubre ambos datos.

El backend emite y Frubber verifica los siguientes formatos:

```text
FRB1|keyId|CODIGO_SERVIDOR|yyyyMMdd.firmaBase64
FRB2|keyId|CODIGO_SERVIDOR|yyyyMMdd|cupo.firmaBase64
```

La firma ECDSA P-256 con SHA-256 cubre los bytes UTF-8 anteriores al punto. Se codifica como 64 bytes IEEE P1363 (`r` y `s` de 32 bytes cada uno) en Base64 estándar. El código del servidor usa 16 caracteres hexadecimales en mayúscula. En FRB2 el cupo es un entero positivo sin ceros iniciales.

## Evidencia

- Prueba de las funciones del generador privado con una clave temporal en memoria. FRB1 y FRB2 conservaron el mismo identificador. Las firmas se verificaron por separado y FRB2 incluyó el cupo de dos puestos.
- El backend de LicenseManager emitió una FRB1 y una FRB2 con el mismo identificador y la misma clave de firma configurada por el dueño. El ejecutable Release, con su verificador habitual, arrancó y cobró con cada licencia en clústeres de laboratorio separados. También recuperó los cobros al cerrar y reabrir el proceso. Se eliminaron los archivos de licencia y los clústeres temporales del ensayo.
