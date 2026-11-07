# PRD FEAT-004: Configurar ESLint en la web

| Campo | Valor |
|-------|-------|
| Ticket | FEAT-004 |
| Tracker | ninguno |
| Fecha | 2026-11-07 |
| PRD loops | 0 |
| Loops since last human decision | 0 |

## Context and Problem
La interfaz web del proyecto no tiene un analizador estático de código: hoy solo se controla con el
chequeo de tipos de la compilación y con las pruebas. Errores comunes de un componente (dependencias
faltantes en un efecto, variables sin uso, reglas de los hooks que no se respetan) no los detecta
nada antes de llegar a revisión. El usuario pidió el 2026-11-07 configurar ESLint en la web con las
reglas de la plantilla de React + TypeScript de Vite.

## Goals
- Que cualquier persona del equipo pueda analizar el código de la web con un solo comando.
- Que el código actual de la web quede sin problemas reportados, para que un problema nuevo se note.
- Que el pipeline del proyecto corra ese análisis igual que corre las pruebas y la compilación.

## Functional Requirements
- FR-01: El proyecto web debe ofrecer un comando de análisis estático que revise todo el código
  fuente de la web, incluidas sus pruebas. [pedido del usuario]
- FR-02: El análisis debe aplicar el conjunto de reglas recomendado para JavaScript, el recomendado
  para TypeScript y las reglas de los hooks de React y de recarga de componentes que trae la plantilla
  de React + TypeScript de Vite. [decisión del usuario del 2026-11-07]
- FR-03: El comando debe terminar con éxito cuando no encuentra problemas y con error cuando encuentra
  al menos uno, indicando archivo, línea y regla de cada problema.
- FR-04: El código actual de la web debe quedar sin problemas reportados por el análisis, sin cambiar
  el comportamiento de la aplicación.
- FR-05: El análisis no debe revisar archivos generados ni dependencias instaladas (la salida de la
  compilación, el informe de cobertura y las dependencias de terceros).
- FR-06: La documentación de contexto del proyecto debe declarar el comando de análisis junto a los
  de instalación, compilación y pruebas, para que el pipeline lo ejecute.

## Non-Functional Requirements
- NFR-01: El análisis completo de la web debe terminar en menos de 60 segundos en la PC de
  desarrollo.
- NFR-02: Las dependencias nuevas deben ser solo de desarrollo (0 dependencias nuevas en lo que se
  publica) y tener 0 vulnerabilidades conocidas según la auditoría de paquetes al momento de
  agregarlas.

## Acceptance Criteria
- AC-01 (FR-01, FR-03): WHEN se ejecuta el comando de análisis sobre el código actual de la web, THE
  sistema SHALL terminar con éxito y no reportar ningún problema.
- AC-02 (FR-01): WHEN se ejecuta el comando de análisis, THE sistema SHALL revisar tanto los archivos
  de la aplicación como los de prueba de la web.
- AC-03 (FR-02, FR-03): IF un archivo de la web declara una variable que no se usa, THEN THE sistema
  SHALL terminar con error e informar el archivo, la línea y la regla incumplida.
- AC-04 (FR-02, FR-03): IF un componente de la web llama a un hook de React dentro de una
  condición, THEN THE sistema SHALL terminar con error e informar el archivo, la línea y la regla de
  los hooks.
- AC-05 (FR-04): WHEN se ejecutan las pruebas y la compilación de la web después de corregir lo que
  el análisis reportó, THE sistema SHALL pasar todas las pruebas y compilar sin errores, con la misma
  cantidad de pruebas que antes.
- AC-06 (FR-05): WHEN existen la salida de la compilación y el informe de cobertura en la carpeta de
  la web, THE sistema SHALL analizar el código sin reportar problemas en esos archivos.
- AC-07 (FR-06): WHEN se lee la sección de stack de la documentación de contexto del proyecto, THE
  documento SHALL mostrar el comando de análisis de la web junto a los de compilación y pruebas.

## Out of Scope
- Análisis estático de las pruebas de punta a punta (`e2e/`) y del código .NET.
- Reglas de formato automático del código (por ejemplo, un formateador) y su aplicación masiva.
- Reglas con información de tipos más estrictas que las de la plantilla (descartadas por el usuario).
- Integración continua, ganchos de commit y configuración de editores.
- Cambios de comportamiento de la aplicación web.

## Risks and Mitigations
- Riesgo: corregir lo que reporta el análisis cambia sin querer el comportamiento de una pantalla.
  Mitigación: AC-05 exige las mismas pruebas en verde y la misma cantidad; los cambios se limitan a lo
  que marca el análisis.
- Riesgo: las dependencias nuevas traen vulnerabilidades o un paquete malicioso (cadena de
  suministro). Mitigación: solo de desarrollo, versiones fijadas en el archivo de bloqueo y auditoría
  de paquetes sin vulnerabilidades (NFR-02).
- Riesgo: una regla de la plantilla choca con un patrón del proyecto y obliga a cambios grandes.
  Mitigación: si pasa, se decide con el usuario si se ajusta la regla o el código; no se desactivan
  reglas en silencio.

## Dependencies
- Ninguna funcional. Usa la web existente (FEAT-001 a FEAT-003a, mergeados).
