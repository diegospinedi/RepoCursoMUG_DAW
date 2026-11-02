# PRD FEAT-001a: Ingreso, sesión y marca

| Campo | Valor |
|-------|-------|
| Ticket | FEAT-001a (parte a de FEAT-001) |
| Tracker | ninguno |
| Fecha | 2026-10-30 |
| PRD padre | `docs/ddw/prd/prd-FEAT-001.md` (índice), derivado de `prd-DISC-001-01.md` |
| PRD loops | 0 |
| Loops since last human decision | 0 |

> **Trazabilidad.** Cada FR, NFR y AC indica entre corchetes el requisito o criterio de origen.
> Los AC se numeran desde AC-01 en este documento y cada uno cita entre paréntesis su número
> global en FEAT-001 (por ejemplo, `FEAT-001 AC-05`), que es el que usa el índice. Decisión del
> 2026-10-30: el validador de DDW exige numeración sin saltos (F-PRD-05) y numeración global en
> las partes de una división (F-PRD-11) a la vez; la cita global cumple las dos.

## Context and Problem
La base de datos de Óptica Sistema va a guardar datos personales de clientes (DNI, domicilio,
teléfono y email) alcanzados por la Ley 25.326, además de datos fiscales de la empresa. Antes de
que exista cualquier pantalla con esos datos, el sistema necesita un acceso protegido por
contraseña. No hay módulo de usuarios: las dos dueñas comparten una única contraseña.

Esta parte entrega la primera versión utilizable de la aplicación: definir la contraseña, ingresar,
cerrar la sesión por inactividad y ver la identidad visual de la óptica en todas las pantallas.

El repositorio es público, por eso no lleva la marca real de la óptica: trae una marca de ejemplo,
y la marca real se instala solo en la PC del local.

## Goals
- Que ninguna pantalla ni operación del sistema devuelva datos sin una sesión iniciada.
- Que la aplicación muestre el logo y los colores de la óptica en todas sus pantallas, sin que la
  marca real esté publicada en el repositorio.

## Functional Requirements
- FR-01: El sistema debe exigir el ingreso de la contraseña de acceso antes de mostrar cualquier
  pantalla o responder cualquier operación que devuelva datos de clientes, presupuestos, facturas o
  configuración. [DISC-001-01 FR-01; PRD-001 RNF-04, AC-79]
- FR-02: El sistema debe pedir que se defina la contraseña de acceso cuando todavía no exista
  ninguna. [DISC-001-01 FR-02]
- FR-03: El sistema no debe aceptar como contraseña de acceso un valor de menos de 8 caracteres.
  [DISC-001-01 FR-03; PRD-001 RNF-04, AC-57]
- FR-04: El sistema debe cerrar la sesión tras 60 minutos sin actividad y volver a exigir la
  contraseña. [DISC-001-01 FR-06; PRD-001 RNF-09]
- FR-05: El sistema debe guardar la contraseña de acceso únicamente como hash con sal, nunca en
  texto plano. [DISC-001-01 FR-07; PRD-001 RNF-14]
- FR-06: El sistema debe mostrar en todas sus pantallas el logo de la marca instalada y aplicar su
  color primario sobre su color de fondo. [DISC-001-01 FR-15; PRD-001 RF-34]
- FR-07: El sistema debe usar una marca de ejemplo, incluida en el repositorio, cuando en la PC no
  haya instalada una marca de la óptica; la marca de la óptica no debe versionarse en el
  repositorio. [Decisión de las dueñas, 2026-10-30: el repositorio es público]
- FR-08: El sistema debe indicar, en todo error de validación, el campo afectado y la acción
  correctiva que debe tomar la operadora. [DISC-001-01 FR-16; PRD-001 RF-35]

## Non-Functional Requirements
- NFR-01: La contraseña de acceso debe tener 8 caracteres como mínimo. [PRD-001 RNF-04]
- NFR-02: La sesión debe cerrarse tras 60 minutos de inactividad. [PRD-001 RNF-09]
- NFR-03: Todos los criterios de aceptación de este documento deben cumplirse en Chrome y en Edge,
  en su versión estable vigente y en la inmediata anterior (4 combinaciones). [PRD-001 RNF-03]
- NFR-04: El 100 % de las operaciones que devuelven datos de clientes, presupuestos, facturas o
  configuración deben rechazar el pedido sin sesión iniciada. [PRD-001 RNF-04, AC-79]

## Acceptance Criteria
- AC-01 (FR-01, NFR-04; FEAT-001 AC-01): IF la operadora no inició sesión y abre cualquier pantalla del sistema,
  THEN THE sistema SHALL redirigirla a la pantalla de ingreso sin mostrar datos de clientes,
  presupuestos, facturas ni configuración. [PRD-001 AC-79]
- AC-02 (FR-01, NFR-04; FEAT-001 AC-02): IF se llama a cualquier operación de la API sin sesión iniciada, THEN THE
  sistema SHALL responder 401 sin devolver datos de clientes, presupuestos, facturas ni
  configuración. [PRD-001 AC-79]
- AC-03 (FR-02; FEAT-001 AC-03): WHEN la operadora abre el sistema y todavía no hay una contraseña definida, THE
  sistema SHALL mostrar la pantalla de definición de la contraseña en lugar de la de ingreso.
- AC-04 (FR-03, FR-08, NFR-01; FEAT-001 AC-04): IF en la pantalla de definición de la contraseña se ingresa una
  contraseña de 7 caracteres, THEN THE sistema SHALL rechazarla e indicar junto al campo que la
  longitud mínima es de 8 caracteres. [PRD-001 AC-57]
- AC-05 (FR-04, NFR-02; FEAT-001 AC-07): WHILE una sesión iniciada permanece 60 minutos sin actividad, THE sistema
  SHALL cerrarla y exigir nuevamente la contraseña en la siguiente acción. [PRD-001 AC-59]
- AC-06 (FR-05; FEAT-001 AC-08): WHEN se inspeccionan la base de datos y los archivos de configuración del sistema
  después de definir la contraseña, THE sistema SHALL no contener la contraseña en texto plano en
  ninguno de ellos. [PRD-001 AC-80]
- AC-07 (FR-06, NFR-03; FEAT-001 AC-15): WHEN, con la marca de la óptica instalada en la PC, la operadora abre
  cualquier pantalla del sistema, incluida la de ingreso, THE sistema SHALL mostrar el logo de la
  óptica y aplicar el color primario #0903A0 sobre fondo #FFFFFF. [PRD-001 AC-33]
- AC-08 (FR-07; FEAT-001 AC-22): WHEN no hay una marca de la óptica instalada en la PC y la operadora abre la
  pantalla de ingreso, THE sistema SHALL mostrar el logo y los colores de la marca de ejemplo, y el
  repositorio SHALL no contener el logo ni el archivo de colores de la óptica.

## Out of Scope
- Bloqueo por intentos fallidos, cambio y restablecimiento de la contraseña y ocultamiento de los
  datos de ARCA: corresponden a FEAT-001b.
- Pantalla de configuración de parámetros de negocio: corresponde a FEAT-001c.
- Módulo de usuarios, roles o más de una contraseña de acceso. [PRD-001 Fuera de Alcance]
- Logo y colores en los PDF: corresponden a los PRDs 05 y 08 de DISC-001.
- Acceso desde internet: el sistema solo se usa desde la PC o la red local del negocio.

## Risks and Mitigations
- Riesgo: acceso no autorizado a datos personales de clientes (Ley 25.326). Mitigación: FR-01 y
  hash con sal (FR-05); el bloqueo por intentos llega en FEAT-001b, que debe estar implementado
  antes de cargar datos reales.
- Riesgo: la marca real de la óptica queda publicada en el repositorio público. Mitigación: FR-07
  y AC-08; el archivo original queda fuera del control de versiones.
- Riesgo: la marca real no se instala en la PC y la óptica usa la app con la marca de ejemplo.
  Mitigación: el procedimiento de instalación debe incluir la copia de la marca.
- Alcance: esta parte tiene 8 criterios, uno más que el umbral de W-PRD-06. Decisión de las
  dueñas, 2026-10-30: se acepta, porque la marca de ejemplo (AC-08) y la marca de la óptica (AC-07)
  son la misma pantalla y el mismo cambio, y separarlas no deja nada que se pueda poner en
  producción por sí solo.

## Dependencies
- Marca de la óptica (`branding.json` con primario #0903A0 y fondo #FFFFFF, y `logo.png`), copiada
  en la PC del local al instalar; hoy está en la carpeta local `Marca/`, fuera de git.
- FEAT-001b y FEAT-001c dependen de este: usan el acceso con sesión iniciada (FR-01) y los
  mensajes de validación (FR-08).
