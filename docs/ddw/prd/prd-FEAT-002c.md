# PRD FEAT-002c: Recálculo del catálogo por configuración y volumen

| Campo | Valor |
|-------|-------|
| Ticket | FEAT-002c (parte c de FEAT-002) |
| Tracker | ninguno |
| Fecha | 2026-11-04 |
| PRD padre | `docs/ddw/prd/prd-FEAT-002.md` (índice), derivado de `prd-DISC-001-02.md` |
| PRD loops | 0 |
| Loops since last human decision | 0 |

> **Trazabilidad.** Cada FR, NFR y AC indica entre corchetes el requisito o criterio de origen. Los AC
> se numeran desde AC-01 en este documento y cada uno cita entre paréntesis su número global en FEAT-002,
> que es el que usa el índice.

## Context and Problem
El precio de venta de cada artículo depende de la alícuota de IVA, la condición fiscal y el múltiplo de
redondeo comercial. Si se cambia uno de esos parámetros en la configuración (FEAT-001c), los precios ya
guardados quedan calculados con los valores anteriores. Esta parte hace que grabar un cambio de esos
parámetros recalcule todo el catálogo en una sola operación, y fija el volumen que el catálogo tiene que
soportar.

## Goals
- Que un cambio de configuración deje todos los precios de venta calculados con los parámetros nuevos.
- Que nunca queden precios calculados con parámetros distintos.
- Que el catálogo funcione igual con 10.000 artículos.

## Functional Requirements
- FR-01: El sistema debe recalcular y guardar el precio de venta de todos los artículos del catálogo,
  activos e inactivos, cuando se grabe un cambio en la alícuota de IVA, la condición fiscal o el múltiplo
  de redondeo comercial. [FEAT-002 FR-14; PRD-001 RF-86]
- FR-02: El sistema debe grabar el cambio de configuración de FR-01 y el recálculo de los precios como una
  sola operación: si el recálculo no se completa, no debe quedar grabado ni el parámetro nuevo ni ningún
  precio recalculado. [FEAT-002 FR-15]

## Non-Functional Requirements
- NFR-01: El catálogo debe admitir hasta 10.000 artículos. [FEAT-002 NFR-01; PRD-001 RNF-12]
- NFR-02: Todos los criterios de aceptación de este documento deben cumplirse en Chrome y en Edge, en su
  versión estable vigente y en la inmediata anterior (4 combinaciones). [FEAT-002 NFR-02]

## Acceptance Criteria
- AC-01 (FR-01; FEAT-002 AC-09): WHEN la operadora cambia la alícuota de 21 a 10,5 con múltiplo 0,01 y
  luego graba un artículo con Precio de Costo $1.105 y Margen 50 %, THE sistema SHALL guardar un Precio
  de Venta de $1.657,50. [PRD-001 AC-72]
- AC-02 (FR-01; FEAT-002 AC-16): WHEN la operadora cambia el múltiplo de $0,01 a $50 en la configuración
  y graba, THE sistema SHALL pasar de $1.815,00 a $1.850,00 el Precio de Venta guardado de un artículo
  con Precio de Costo $1.210 y Margen 50 %, sin que se edite el artículo. [PRD-001 AC-85]
- AC-03 (FR-01; FEAT-002 AC-17): WHEN la operadora graba un cambio de configuración que dispara el
  recálculo, THE sistema SHALL recalcular también el Precio de Venta de los artículos inactivos.
- AC-04 (FR-02; FEAT-002 AC-18): IF el recálculo de FR-01 no se completa, THEN THE sistema SHALL
  conservar el valor anterior del parámetro y los Precios de Venta anteriores de todos los artículos, e
  informar a la operadora que el cambio no se grabó.
- AC-05 (NFR-01; FEAT-002 AC-22): WHEN el catálogo tiene 10.000 artículos, THE sistema SHALL permitir
  grabar, modificar, buscar y desactivar artículos con los mismos resultados que en los AC de FEAT-002a,
  FEAT-002b y este documento.

## Out of Scope
- Alta, búsqueda, edición y baja de artículos: corresponden a FEAT-002a y FEAT-002b.
- Recálculo de los precios de las líneas de presupuestos ya grabados: PRD-001 RF-87 lo prohíbe y se
  cubre en el PRD 04.
- Un tiempo máximo para el recálculo: PRD-001 no lo fija.

## Risks and Mitigations
- Riesgo: el recálculo de 10.000 artículos tarda o falla a mitad de camino. Mitigación: operación única
  de FR-02 (AC-04).
- Riesgo: el recálculo cambia precios de presupuestos ya grabados. Mitigación: PRD-001 RF-87 lo prohíbe y
  se cubre en el PRD 04.

## Dependencies
- FEAT-002a (cálculo del precio de venta) y FEAT-002b (edición y búsqueda, para AC-05).
- FEAT-001c: pantalla y grabación de la configuración (mergeado, PR #4), que esta parte extiende.
