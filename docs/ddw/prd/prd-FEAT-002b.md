# PRD FEAT-002b: Búsqueda, edición y baja de artículos

| Campo | Valor |
|-------|-------|
| Ticket | FEAT-002b (parte b de FEAT-002) |
| Tracker | ninguno |
| Fecha | 2026-11-04 |
| PRD padre | `docs/ddw/prd/prd-FEAT-002.md` (índice), derivado de `prd-DISC-001-02.md` |
| PRD loops | 0 |
| Loops since last human decision | 0 |

> **Trazabilidad.** Cada FR, NFR y AC indica entre corchetes el requisito o criterio de origen. Los AC
> se numeran desde AC-01 en este documento y cada uno cita entre paréntesis su número global en FEAT-002,
> que es el que usa el índice.

## Context and Problem
Con el alta de FEAT-002a la operadora puede cargar artículos, pero no encontrarlos, corregirlos ni
sacarlos de uso. Esta parte agrega el listado con búsqueda, la edición del costo y del margen con el
recálculo del precio de venta de ese artículo, y la desactivación y reactivación sin eliminar datos.

## Goals
- Que la operadora encuentre un artículo por su código, su código en el proveedor o su descripción.
- Que corregir el costo o el margen de un artículo actualice su precio de venta al grabar.
- Que un artículo que ya no se vende se pueda desactivar y volver a activar sin perder sus datos.

## Functional Requirements
- FR-01: El sistema debe permitir modificar la Descripción, el Código en el proveedor, el Precio de Costo
  y el Margen de Utilidad de un artículo existente, con las mismas validaciones del alta de FEAT-002a.
  [FEAT-002 FR-06; PRD-001 RF-45]
- FR-02: El sistema debe mostrar, al abrir un artículo grabado para editarlo, el Precio de Venta como
  campo de solo lectura. [FEAT-002 FR-12; PRD-001 RF-69, RF-70]
- FR-03: El sistema debe recalcular y guardar el precio de venta de un artículo cuando se grabe un cambio
  en su Precio de Costo o en su Margen de Utilidad. [FEAT-002 FR-13; PRD-001 RF-45]
- FR-04: El sistema debe permitir listar los artículos y buscarlos por Código, Código en el proveedor o
  Descripción, por coincidencia parcial y sin distinguir mayúsculas ni acentos. [FEAT-002 FR-16]
- FR-05: El sistema debe permitir desactivar un artículo sin eliminarlo ni modificar sus datos.
  [FEAT-002 FR-17; decisión de las dueñas]
- FR-06: El sistema debe permitir reactivar un artículo desactivado. [FEAT-002 FR-18]
- FR-07: El sistema debe indicar en el listado de FR-04 si cada artículo está activo o inactivo.
  [FEAT-002 FR-19]

## Non-Functional Requirements
- NFR-01: Todos los criterios de aceptación de este documento deben cumplirse en Chrome y en Edge, en su
  versión estable vigente y en la inmediata anterior (4 combinaciones). [FEAT-002 NFR-02]
- NFR-02: Los importes deben expresarse con 2 decimales. [FEAT-002 NFR-03]

## Acceptance Criteria
- AC-01 (FR-02; FEAT-002 AC-13): WHEN la operadora abre un artículo grabado para editarlo, THE sistema
  SHALL mostrar el Precio de Venta sin permitir modificar su valor. [PRD-001 AC-81]
- AC-02 (FR-01, FR-03; FEAT-002 AC-14): WHEN, con alícuota 21 y múltiplo 0,01, la operadora cambia de
  50 % a 60 % el margen de un artículo con Precio de Costo $1.210 y graba, THE sistema SHALL guardar un
  Precio de Venta de $1.936,00. [PRD-001 AC-70]
- AC-03 (FR-01, FR-03, NFR-02; FEAT-002 AC-15): WHEN, con alícuota 21 y múltiplo 0,01, la operadora
  cambia a mano de $1.210 a $2.420 el Precio de Costo de un artículo con Margen 50 % y graba, THE sistema
  SHALL guardar un Precio de Venta de $3.630,00. [PRD-001 AC-71]
- AC-04 (FR-04; FEAT-002 AC-19): WHEN la operadora busca "armazón" en el catálogo, THE sistema SHALL
  listar todos los artículos cuya Descripción contiene "Armazón" o "armazon", y ninguno que no lo
  contenga.
- AC-05 (FR-05, FR-07; FEAT-002 AC-20): WHEN la operadora desactiva un artículo, THE sistema SHALL
  mostrarlo como inactivo en el listado y conservar sin cambios su Código, Descripción, Precio de Costo,
  Margen y Precio de Venta.
- AC-06 (FR-06, FR-07; FEAT-002 AC-21): WHEN la operadora reactiva un artículo inactivo, THE sistema
  SHALL mostrarlo como activo en el listado.

## Out of Scope
- Alta de artículos y cálculo del precio de venta: corresponden a FEAT-002a.
- Recálculo del catálogo al cambiar la configuración y el volumen de 10.000 artículos: corresponden a
  FEAT-002c.
- Exclusión de los artículos inactivos en la búsqueda del presupuesto: corresponde al PRD 04.
- Eliminación definitiva de artículos e historial de precios.

## Risks and Mitigations
- Riesgo: editar el código en el proveedor a uno que ya existe duplica la clave que usa la planilla del
  PRD 03. Mitigación: las validaciones del alta (FEAT-002a, código único) aplican también a la edición
  (FR-01).
- Riesgo: una búsqueda sin acentos no encuentra descripciones acentuadas. Mitigación: búsqueda sin
  distinguir mayúsculas ni acentos (FR-04, AC-04).

## Dependencies
- FEAT-002a: el catálogo, el alta, las validaciones y el cálculo del precio de venta.
- FEAT-001a: sesión iniciada (mergeado, PR #2).
