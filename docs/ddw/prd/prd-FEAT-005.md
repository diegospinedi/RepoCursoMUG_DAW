# PRD FEAT-005: Integración continua para los pull requests

| Field | Value |
|-------|-------|
| Ticket | FEAT-005 |
| Tracker | none |
| Date | 2026-11-07 |
| PRD loops | 1 |
| Loops since last human decision | 0 |

## Context and Problem
Hoy la única verificación de un cambio es la corrida local de quien lo hizo: instalación, compilación, lint y
pruebas (.NET, web y de punta a punta) dependen de que alguien se acuerde de correr todo en su PC antes de mergear.
Un pull request puede llegar a `master` con el lint en rojo o con pruebas rotas sin que nadie lo vea. El repositorio
está alojado en GitHub y no tiene ninguna verificación automática.

Alcance: los 11 criterios de aceptación quedan en un solo ticket por decisión del usuario (W-PRD-06, 2026-11-07):
es un único workflow y cada criterio corresponde a uno de sus pasos.

## Goals
- Que cada pull request se verifique solo, con los mismos comandos que el proyecto declara en `AGENTS.md`.
- Que el resultado (aprobado o fallado) quede a la vista en el propio pull request.
- Que la verificación no use secretos ni dependa de servicios externos al repositorio, en particular de ARCA.

## Functional Requirements
- FR-01: El sistema debe ejecutar la verificación automáticamente cada vez que se abre un pull request o se suben
  cambios nuevos a uno abierto.
- FR-02: La verificación debe instalar las dependencias con las versiones fijadas en los archivos de bloqueo del
  repositorio, sin resolver versiones nuevas.
- FR-03: La verificación debe compilar la solución del servidor y correr todas sus pruebas.
- FR-04: La verificación debe correr el linter de la web y la prueba de su configuración, sin admitir avisos.
- FR-05: La verificación debe correr las pruebas de la web con su medición de cobertura y compilar la web.
- FR-06: La verificación debe correr las pruebas de unidad de la suite de punta a punta y la suite de punta a punta
  completa en Chrome y en Edge.
- FR-07: La verificación debe marcar el pull request como fallado cuando cualquiera de sus pasos falla, nombrando el
  paso que falló.
- FR-08: La verificación debe funcionar sin secretos: ni claves, ni contraseñas, ni certificados escritos en su
  definición, y sin pedir secretos del repositorio.
- FR-09: La verificación debe ejecutarse con permisos de solo lectura sobre el repositorio.
- FR-10: Las pruebas que corre la verificación no deben comunicarse con servicios externos (ARCA ni ningún otro):
  solo con servicios levantados por la propia corrida en la misma máquina.

## Non-Functional Requirements
- NFR-01: Una corrida completa de la verificación no debe superar los 45 minutos; pasado ese tiempo se corta y queda
  como fallada.
- NFR-02: Si se suben cambios nuevos a un pull request mientras su verificación anterior sigue corriendo, la anterior
  debe cancelarse, de modo que haya como máximo 1 corrida activa por pull request.

## Acceptance Criteria
- AC-01 (FR-01): WHEN se abre un pull request o se suben commits nuevos a uno abierto, THE sistema SHALL iniciar
  una corrida de la verificación para ese pull request.
- AC-02 (FR-02): WHEN la verificación instala las dependencias, THE sistema SHALL usar exclusivamente las versiones
  de los archivos de bloqueo (`npm ci`, restauración de .NET) y SHALL fallar si el archivo de bloqueo no coincide con
  la declaración de dependencias.
- AC-03 (FR-03): WHEN corre la verificación, THE sistema SHALL compilar la solución del servidor y ejecutar todas
  sus pruebas, informando la cantidad aprobada.
- AC-04 (FR-04): IF el código de la web tiene un problema o un aviso del linter, THEN THE sistema SHALL marcar la
  verificación como fallada en el paso de lint.
- AC-05 (FR-05): WHEN corre la verificación, THE sistema SHALL ejecutar las pruebas de la web con cobertura y la
  compilación de la web.
- AC-06 (FR-06): WHEN corre la verificación, THE sistema SHALL ejecutar las pruebas de unidad de la suite de punta a
  punta y la suite completa en Chrome y en Edge.
- AC-07 (FR-07): IF una prueba (del servidor, de la web o de punta a punta) falla, THEN THE sistema SHALL marcar el
  pull request como fallado y el resultado SHALL nombrar el paso que falló.
- AC-08 (FR-08): WHEN se revisa la definición de la verificación, THE sistema SHALL no contener claves, contraseñas,
  certificados ni referencias a secretos del repositorio.
- AC-09 (FR-09): WHEN corre la verificación, THE sistema SHALL tener permiso de solo lectura sobre el contenido del
  repositorio.
- AC-10 (FR-10): IF una prueba intenta comunicarse con un servicio fuera de la máquina de la corrida, THEN THE
  sistema SHALL no depender de esa respuesta para aprobar: la suite de punta a punta solo habla con `localhost` y
  ninguna prueba del servidor ni de la web usa un servicio externo.
- AC-11 (FR-07): WHEN todos los pasos terminan bien, THE sistema SHALL marcar el pull request como aprobado.

## Out of Scope
- Publicar, desplegar o generar instaladores.
- Correr la verificación en cada push a `master` o con un horario: solo en pull requests (pedido del usuario).
- Configurar las reglas de protección de la rama `master` en GitHub (se hace desde la configuración del repositorio,
  no desde el código).
- Integrar con ARCA (homologación o producción): no existe todavía y ninguna prueba debe usarla.
- Correr la verificación en Windows o en macOS.
- Optimizaciones de tiempo más allá de NFR-01 (cachés avanzadas, matrices de versiones).

## Risks and Mitigations
- **Las pruebas de punta a punta fallan por el entorno de GitHub (navegadores, certificado HTTPS de desarrollo,
  funciones que dependen de Windows).** Mitigación: instalar Chrome y Edge estables en la corrida, generar el
  certificado de desarrollo en la propia corrida y, si una prueba depende de Windows, registrarlo como decisión antes
  de excluirla.
- **Un pull request desde un fork podría intentar leer secretos.** Mitigación: la verificación no usa secretos y
  tiene permisos de solo lectura (FR-08, FR-09).
- **Acciones de terceros modificadas (cadena de suministro).** Mitigación: usar solo acciones oficiales con versión
  fijada; se detalla en la spec y el modelo de amenazas.
- **Datos personales en los registros de la corrida.** Las pruebas usan datos ficticios; la verificación no publica
  bases ni respaldos.

## Dependencies
- FEAT-004 (mergeado, PR #10): comandos de lint de la web (`npm run lint`, `npm run test:lint`).
- Comandos declarados en `AGENTS.md`, sección "Stack".
- GitHub como plataforma del repositorio (`diegospinedi/RepoCursoMUG_DAW`).
