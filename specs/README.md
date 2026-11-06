# Especificaciones de Frubber con Spec Kit

El repositorio se inicializó con **Specify CLI 1.3.1**, integración `generic` y scripts Python. La integración genérica entrega instrucciones en `.specify/arena-commands/`; no instala comandos de chat nativos ni ejecuta automáticamente este agente. Se sigue el flujo leyendo las instrucciones y usando los artefactos de `.specify/` y `specs/`. No se instaló la extensión `git`, por lo que crear una especificación no cambia la rama.

La herramienta CLI usada para inicializar el repositorio se instaló en un entorno temporal de esta sesión, no en el computador del usuario. Si un desarrollador necesita instalarla en su propio entorno, con Python 3.11 o superior puede usar `python -m pip install specify-cli==1.3.1` o `uv tool install specify-cli==1.3.1`. El proyecto ya está inicializado: no volver a ejecutar `init --force` sin revisar los archivos que podría sobrescribir.

La especificación activa de este tramo es `001-inicializacion-postgresql20/`. `.specify/feature.json` es local y se ignora en Git; para operar desde otra copia, establecer `SPECIFY_FEATURE_DIRECTORY=specs/001-inicializacion-postgresql20` o seleccionar la especificación correspondiente mediante los scripts. La especificación no sustituye Linear, la documentación aprobada ni los candados de seguridad.

Los archivos de herramienta bajo `.specify/` proceden de [github/spec-kit](https://github.com/github/spec-kit) 1.3.1; su licencia MIT y aviso de copyright están en `.specify/LICENSE.spec-kit`. La constitución es específica de Frubber.
