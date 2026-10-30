# PRD FEAT-001b: Bloqueo y gestión de la contraseña

| Campo | Valor |
|-------|-------|
| Ticket | FEAT-001b (parte b de FEAT-001) |
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
Con el ingreso de FEAT-001a, la contraseña es la única barrera frente a los datos personales de
los clientes. Sin límite de intentos, alguien puede probar contraseñas indefinidamente. Además,
las dueñas necesitan cambiarla, y como no hay administrador ni envío de emails, una contraseña
olvidada tiene que poder restablecerse sin perder datos.

El certificado digital y el punto de venta de ARCA son datos fiscales sensibles: no deben aparecer
en ninguna pantalla.

## Goals
- Que probar contraseñas al azar sea impracticable, sin dejar a las dueñas sin acceso por más de 5
  minutos.
- Que las dueñas puedan cambiar la contraseña y recuperar el acceso sin perder datos.
- Que ningún dato de ARCA sea visible ni editable desde la aplicación.

## Functional Requirements
- FR-01: El sistema debe bloquear el ingreso durante 5 minutos tras 5 intentos fallidos
  consecutivos, rechazando en ese lapso incluso la contraseña correcta. [DISC-001-01 FR-04;
  PRD-001 RNF-08]
- FR-02: El sistema debe liberar el bloqueo de FR-01 por sí solo al cumplirse los 5 minutos, sin
  intervención de un administrador. [DISC-001-01 FR-05; PRD-001 RNF-08]
- FR-03: El sistema debe permitir cambiar la contraseña de acceso con sesión iniciada, pidiendo la
  contraseña actual y la nueva, y aplicando a la nueva la longitud mínima de 8 caracteres.
  [DISC-001-01 FR-17; decisión de las dueñas, 2026-10-29]
- FR-04: El sistema debe ofrecer un procedimiento de restablecimiento de la contraseña, documentado
  y ejecutable solo en la PC donde corre la aplicación, que elimine la contraseña sin modificar
  ningún otro dato, de modo que el siguiente ingreso pida definirla. [DISC-001-01 FR-18; decisión
  de las dueñas, 2026-10-29]
- FR-05: El sistema no debe ofrecer el restablecimiento de FR-04 desde la interfaz ni desde la API.
  [DISC-001-01 FR-18]
- FR-06: El sistema no debe mostrar ni permitir editar, en ninguna pantalla, el certificado
  digital de la empresa ni el punto de venta habilitado para web services. [DISC-001-01 FR-14;
  PRD-001 RF-64]

## Non-Functional Requirements
- NFR-01: El bloqueo por intentos fallidos debe activarse con 5 intentos consecutivos y durar
  5 minutos. [PRD-001 RNF-08]
- NFR-02: Todos los criterios de aceptación de este documento que usan la interfaz deben cumplirse
  en Chrome y en Edge, en su versión estable vigente y en la inmediata anterior (4 combinaciones).
  [PRD-001 RNF-03]

## Acceptance Criteria
- AC-01 (FR-01, NFR-01; FEAT-001 AC-05): IF se ingresa 5 veces seguidas una contraseña incorrecta, THEN THE sistema
  SHALL rechazar durante los 5 minutos siguientes todo ingreso, incluida la contraseña correcta,
  e informar que el acceso está bloqueado temporalmente. [PRD-001 AC-58]
- AC-02 (FR-02, NFR-01; FEAT-001 AC-06): WHEN transcurren 5 minutos desde el bloqueo de AC-01, THE sistema SHALL
  admitir el ingreso con la contraseña correcta sin intervención de un administrador.
  [PRD-001 AC-58]
- AC-03 (FR-06, NFR-02; FEAT-001 AC-14): WHEN la operadora recorre todas las pantallas con sesión iniciada, THE
  sistema SHALL no mostrar ni permitir editar en ninguna el certificado digital ni el punto de
  venta habilitado para web services. [PRD-001 AC-50]
- AC-04 (FR-03; FEAT-001 AC-16): WHEN la operadora ingresa la contraseña actual correcta y una contraseña nueva de
  8 caracteres o más y confirma el cambio, THE sistema SHALL aceptar la contraseña nueva en el
  siguiente ingreso y rechazar la anterior.
- AC-05 (FR-03; FEAT-001 AC-17): IF al cambiar la contraseña la contraseña actual ingresada es incorrecta, THEN THE
  sistema SHALL no cambiarla e indicar junto al campo que la contraseña actual no es válida.
- AC-06 (FR-04; FEAT-001 AC-18): WHEN se ejecuta en la PC del local el procedimiento documentado de
  restablecimiento, THE sistema SHALL mostrar en el siguiente ingreso la pantalla de definición de
  la contraseña, y los demás datos guardados SHALL seguir sin cambios.
- AC-07 (FR-05; FEAT-001 AC-19): IF se busca restablecer la contraseña desde cualquier pantalla o llamando a
  cualquier operación de la API, THEN THE sistema SHALL no ofrecer ni ejecutar el restablecimiento.

## Out of Scope
- Ingreso, definición de la contraseña, sesión y marca: corresponden a FEAT-001a.
- Configuración de parámetros de negocio: corresponde a FEAT-001c.
- Recuperación de la contraseña por email u otro canal.
- Instalación y resguardo del certificado digital y del punto de venta: se hacen fuera de la
  aplicación.

## Risks and Mitigations
- Riesgo: el bloqueo deja a las dos operadoras sin acceso a la vez, porque comparten la contraseña.
  Mitigación: dura solo 5 minutos y se libera solo (FR-02).
- Riesgo: contraseña olvidada sin administrador que la restablezca. Mitigación: procedimiento en la
  PC del local (FR-04), que no borra datos.
- Riesgo: el procedimiento de FR-04 permite a quien tenga acceso físico a la PC tomar el control
  del sistema. Mitigación: solo se ejecuta en esa PC, no desde la red ni la API (FR-05); la
  protección física de la PC queda a cargo del negocio.

## Dependencies
- FEAT-001a: ingreso con contraseña, pantalla de definición de la contraseña y mensajes de
  validación junto al campo.
