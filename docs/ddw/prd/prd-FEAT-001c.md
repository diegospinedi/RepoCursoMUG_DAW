# PRD FEAT-001c: Configuración de parámetros de negocio

| Campo | Valor |
|-------|-------|
| Ticket | FEAT-001c (parte c de FEAT-001) |
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
Los cálculos de precios y la facturación dependen de cuatro parámetros de negocio: alícuota de
IVA, condición fiscal, tope de identificación del receptor y múltiplo de redondeo comercial. Hoy
no hay dónde administrarlos, y un cambio normativo no puede requerir intervención técnica.

## Goals
- Que las operadoras puedan consultar y cambiar los parámetros desde la aplicación.
- Que un valor fuera de rango no se pueda grabar.
- Que el sistema arranque con valores iniciales razonables y avise del que falta cargar.

## Functional Requirements
- FR-01: El sistema debe permitir consultar y modificar, desde una pantalla de configuración
  accesible solo con sesión iniciada, la alícuota de IVA, la condición fiscal, el tope de
  identificación del receptor y el múltiplo de redondeo comercial. [DISC-001-01 FR-08;
  PRD-001 RF-63]
- FR-02: El sistema debe admitir como condición fiscal únicamente los valores Responsable
  Inscripto y Monotributo. [DISC-001-01 FR-09; PRD-001 RF-48]
- FR-03: El sistema debe admitir como alícuota de IVA únicamente un porcentaje entre 0 y 100,
  expresado con hasta 2 decimales (por ejemplo, 21 o 10,5). [DISC-001-01 FR-10; PRD-001 RF-46]
- FR-04: El sistema no debe admitir un múltiplo de redondeo comercial menor a 0,01.
  [DISC-001-01 FR-11; PRD-001 RF-72]
- FR-05: El sistema no debe admitir un tope de identificación del receptor menor o igual a 0.
  [DISC-001-01 FR-12; PRD-001 RF-49]
- FR-06: El sistema debe aplicar el valor grabado de cada parámetro a partir de la siguiente
  operación que lo use, sin reiniciar la aplicación. [DISC-001-01 FR-13; PRD-001 AC-49]
- FR-07: El sistema debe iniciar, al instalarse, la alícuota de IVA en 21, la condición fiscal en
  Responsable Inscripto y el múltiplo de redondeo comercial en 0,01. [DISC-001-01 FR-19; decisión
  de las dueñas, 2026-10-29]
- FR-08: El sistema debe iniciar el tope de identificación del receptor sin valor al instalarse, y
  mostrar en la pantalla de configuración un aviso de que falta cargarlo mientras no tenga valor.
  [DISC-001-01 FR-20; decisión de las dueñas, 2026-10-29]

## Non-Functional Requirements
- NFR-01: Todos los criterios de aceptación de este documento deben cumplirse en Chrome y en Edge,
  en su versión estable vigente y en la inmediata anterior (4 combinaciones). [PRD-001 RNF-03]

## Acceptance Criteria
- AC-01 (FR-01, FR-06; FEAT-001 AC-09): WHEN la operadora modifica el tope de identificación del receptor en la
  pantalla de configuración y graba, THE sistema SHALL mostrar el nuevo valor al volver a abrir la
  pantalla y usarlo en la siguiente operación que lo consulte, sin reiniciar la aplicación.
  [PRD-001 AC-49]
- AC-02 (FR-01, FR-02; FEAT-001 AC-10): WHEN la operadora cambia la condición fiscal de Responsable Inscripto a
  Monotributo y graba, THE sistema SHALL mostrar Monotributo como condición fiscal vigente al
  volver a abrir la pantalla de configuración.
- AC-03 (FR-03; FEAT-001 AC-11): IF la operadora ingresa una alícuota de IVA de 101 o de -1, THEN THE sistema SHALL
  no grabarla y mostrar junto al campo que la alícuota debe estar entre 0 y 100.
- AC-04 (FR-04; FEAT-001 AC-12): IF la operadora ingresa un múltiplo de redondeo comercial de 0,001, THEN THE
  sistema SHALL no grabarlo e indicar junto al campo que el valor mínimo es 0,01. [PRD-001 AC-82]
- AC-05 (FR-05; FEAT-001 AC-13): IF la operadora ingresa un tope de identificación del receptor de 0, THEN THE
  sistema SHALL no grabarlo e indicar junto al campo que el tope debe ser mayor a 0.
- AC-06 (FR-07, NFR-01; FEAT-001 AC-20): WHEN la operadora abre la pantalla de configuración por primera vez
  después de instalar el sistema, THE sistema SHALL mostrar alícuota de IVA 21, condición fiscal
  Responsable Inscripto y múltiplo de redondeo comercial 0,01.
- AC-07 (FR-08; FEAT-001 AC-21): WHILE el tope de identificación del receptor no tiene valor, THE sistema SHALL
  mostrar en la pantalla de configuración el aviso de que falta cargarlo.

## Out of Scope
- Ingreso, sesión y marca: corresponden a FEAT-001a.
- Bloqueo y gestión de la contraseña: corresponden a FEAT-001b.
- Recálculo de precios del catálogo al grabar la configuración: corresponde al PRD 02 de
  DISC-001 (PRD-001 RF-86).
- Uso de los parámetros en el cálculo de precios y en la facturación: corresponde a los PRDs 02,
  06 y 07 de DISC-001. Esta parte solo los administra.

## Risks and Mitigations
- Riesgo: un parámetro mal cargado (por ejemplo, IVA 210 en lugar de 21) altera todos los precios.
  Mitigación: rangos válidos de FR-03 a FR-05 con mensaje junto al campo.
- Riesgo: facturar con el tope sin cargar. Mitigación: aviso de FR-08; el PRD 06 de DISC-001
  impide la emisión mientras el tope no tenga valor.

## Dependencies
- FEAT-001a: sesión iniciada para acceder a la configuración y mensajes de validación junto al
  campo.
