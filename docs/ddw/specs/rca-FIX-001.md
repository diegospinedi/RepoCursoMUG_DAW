# RCA FIX-001: El bloqueo de ingreso se levanta si el reloj del sistema retrocede

| Field | Value |
|-------|-------|
| Ticket | FIX-001 |
| Tier | FIX |
| Tracker | none |
| Date | 2026-11-06 |
| PRD relacionado | docs/ddw/prd/prd-FEAT-001b.md (FR-01, FR-02, NFR-01, AC-01, AC-02) |
| PRD loops | 0 |
| Loops since last human decision | 0 |

## Context and Problem

En el cierre de CODE de FEAT-002c, la prueba e2e `[msedge-acceso] acceso.spec.ts:198 cinco intentos incorrectos
bloquean el ingreso` falló una vez en cinco corridas del harness: el quinto intento incorrecto respondió 423 (bloqueo
activo) y, menos de un segundo después, el ingreso con la contraseña correcta respondió 204 y abrió la sesión. FR-01 de
FEAT-001b exige que durante los 5 minutos del bloqueo se rechace incluso la contraseña correcta.

**Síntoma:** el bloqueo de 5 minutos por 5 intentos fallidos se levanta antes de tiempo, de forma intermitente.

**Causa raíz:** `PoliticaBloqueo.Evaluar` (`src/Optica.Domain/Acceso/PoliticaBloqueo.cs`) toma como ancla la fecha del
rechazo número `5·n` y bloquea solo si `0 ≤ ahora − ancla < 5 min`. La condición `transcurrido >= TimeSpan.Zero` hace que,
si la hora actual de la PC queda **antes** del ancla, el resultado sea `Libre`. Cuando el reloj del sistema retrocede
(en WSL2 lo hace solo al resincronizar con el host, en segundos), la siguiente evaluación ve un ancla "en el futuro" y
libera el bloqueo. `IngresarConContrasena` toma `ahora` de `TimeProvider.GetUtcNow()`, que sigue al reloj de pared.

**Cadena de eventos:**
1. Cinco rechazos consecutivos; el quinto se registra con la hora T y responde 423 (segunda evaluación con la hora
   original, D-Block 2 de FEAT-001b).
2. El reloj del sistema retrocede algunos milisegundos o segundos.
3. El intento siguiente lee los cinco rechazos, toma `ahora < T`, `transcurrido < 0` → `EstadoBloqueo.Libre`.
4. Se verifica el hash, la contraseña es correcta → 204 y sesión.

**Por qué no lo vio nadie antes:** es un comportamiento elegido y documentado. ADR-006 (Consequences) dice que "atrasar
la hora antes del ancla anula el bloqueo vigente (acceso a la PC o sincronización horaria) y libera como máximo 4
intentos antes del próximo bloqueo", y el modelo de amenazas de FEAT-001b lo registró como R-02 con probabilidad baja,
pensando en cambios de hora manuales. El e2e mostró que el retroceso ocurre solo, sin intervención, así que la
probabilidad no es baja en este entorno, y la regla contradice FR-01.

**Confirmación:** no se reprodujo el retroceso del reloj de forma aislada; es la única causa coherente con el código
(las pruebas e2e son secuenciales, duran 2 s y no hay concurrencia en ese tramo). La prueba de regresión con el reloj
controlado (`FakeTimeProvider`) reproduce el defecto de forma determinista.

**Componente afectado:** dominio de Acceso (`PoliticaBloqueo`), usado por `IngresarConContrasena`; ninguna otra pieza
evalúa el bloqueo.

**Brecha en el PRD:** FR-01 de FEAT-001b dice cuánto dura el bloqueo pero no qué pasa si la hora de la PC retrocede.
La regla que lo resuelve la decidió el usuario el 2026-11-06 y se fija acá (FR-01 a FR-03 de este ticket); el PRD de
FEAT-001b no se modifica.

## Goals

- Que un retroceso chico del reloj (resincronización) no levante el bloqueo vigente.
- Que un cambio de hora grande no deje a la operadora bloqueada indefinidamente.
- Que la intermitencia del e2e `cinco intentos incorrectos bloquean el ingreso` desaparezca por la causa y no por la prueba.

## Functional Requirements

- FR-01: El sistema debe mantener el bloqueo de ingreso cuando la hora actual queda antes del ancla del bloqueo por hasta 5 minutos, y debe sostenerlo hasta que la hora actual alcance el ancla más 5 minutos. (Decisión del usuario, 2026-11-06.)
- FR-02: El sistema debe liberar el bloqueo cuando la hora actual queda antes del ancla por más de 5 minutos, tratándolo como un cambio de hora deliberado, igual que hasta ahora.
- FR-03: El sistema debe conservar sin cambios el bloqueo y la liberación cuando el reloj avanza normalmente: bloqueado mientras transcurren menos de 5 minutos desde el ancla y libre a partir de los 5 minutos.

## Non-Functional Requirements

- NFR-01: La tolerancia ante el retroceso del reloj debe ser de 5 minutos, igual a la duración del bloqueo, y el bloqueo debe durar como máximo 10 minutos de reloj de pared desde el retroceso.

## Acceptance Criteria

- AC-01 (FR-01, NFR-01): IF la hora actual retrocede 1 segundo después del quinto rechazo, THEN THE sistema SHALL rechazar con 423 el ingreso con la contraseña correcta y no abrir sesión.
- AC-02 (FR-01, NFR-01): IF la hora actual queda exactamente 5 minutos antes del ancla, THEN THE sistema SHALL mantener el bloqueo e informar como fin del bloqueo el ancla más 5 minutos.
- AC-03 (FR-02, NFR-01): IF la hora actual queda 5 minutos y 1 segundo antes del ancla, THEN THE sistema SHALL tratar el ingreso como libre y aceptar la contraseña correcta.
- AC-04 (FR-03): WHEN transcurren 4 minutos y 59 segundos desde el ancla, THE sistema SHALL mantener el bloqueo, y WHEN transcurren 5 minutos, THE sistema SHALL liberarlo.
- AC-05 (FR-01): WHEN el harness e2e corre la prueba `cinco intentos incorrectos bloquean el ingreso` en Chrome y Edge, THE sistema SHALL responder 423 a la contraseña correcta durante el bloqueo en cada corrida.

## Out of Scope

- Cambiar la duración del bloqueo, la cantidad de intentos o el orden por `Id` del registro de eventos (ADR-006).
- Usar un reloj monotónico persistido o un evento nuevo de "retroceso detectado".
- La restricción de acceso a la PC o a la configuración horaria de Windows.

## Risks and Mitigations

- Un atraso deliberado de más de 5 minutos sigue liberando el bloqueo (FR-02): lo exige quien tenga acceso a la PC o a
  la hora de Windows, y cada atraso libera como máximo 4 intentos antes del próximo bloqueo (R-02 de FEAT-001b).
- Un atraso de hasta 5 minutos alarga el bloqueo hasta 10 minutos de reloj de pared (NFR-01): es hacia el lado seguro
  y la operadora puede restablecer la contraseña por consola (FEAT-001b FR-04) si lo necesita.
- La prueba e2e podría seguir fallando por otra causa no identificada (la segunda falla del cierre de FEAT-002c no se
  guardó): AC-05 se verifica con varias corridas del harness en CODE y en VERIFY.

## Dependencies

- FEAT-001b (mergeado, PR #3): `PoliticaBloqueo`, `IngresarConContrasena`, ADR-006 y el riesgo R-02 de
  `docs/ddw/security/threat-FEAT-001b.md`. ADR-006 cambia en una de sus consecuencias; el ADR nuevo que lo
  reemplace en ese punto se escribe en PLAN.
