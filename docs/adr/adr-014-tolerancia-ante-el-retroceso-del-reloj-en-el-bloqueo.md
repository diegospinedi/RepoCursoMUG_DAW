# ADR-014: Tolerancia de 5 minutos ante el retroceso del reloj en el bloqueo de ingreso

| Field | Value |
|-------|-------|
| Date | 2026-11-06 |
| Ticket | FIX-001 |
| Status | Accepted |

## Context
ADR-006 ancló el bloqueo de 5 minutos en la fecha del quinto rechazo y aceptó que atrasar la hora antes del ancla
anulara el bloqueo vigente, pensando en un cambio manual (R-02 de FEAT-001b). En el cierre de FEAT-002c, el e2e mostró
que el reloj de WSL2 retrocede solo al resincronizar: un retroceso de segundos levantó el bloqueo y la contraseña correcta
entró, contra FR-01 de FEAT-001b. Detalle en `docs/ddw/specs/rca-FIX-001.md`. Este ADR reemplazó solo esa
consecuencia de ADR-006; el resto de ADR-006 (cuenta derivada de eventos, orden por `Id`, ancla en el rechazo `5·n`)
siguió vigente, por eso ADR-006 conservó el estado `Accepted`.

## Options considered

### Option 1: tolerancia de 5 minutos hacia atrás
- **Pros:** cubre las resincronizaciones; un atraso grande sigue liberando, así nadie queda bloqueado horas; es una
  cota en una función pura, sin esquema ni eventos nuevos.
- **Cons:** un retroceso alarga el bloqueo hasta 10 minutos desde ese momento y puede reactivar uno recién vencido; un
  atraso que deja la hora más de 5 minutos antes del ancla sigue liberando hasta 4 intentos.

### Option 2: bloqueado ante cualquier retroceso
- **Pros:** ningún atraso libera el bloqueo.
- **Cons:** un atraso de un día deja el ingreso bloqueado hasta que el reloj vuelva a esa fecha, salvo restablecer la
  contraseña por consola.

### Option 3: registrar el retroceso y contar 5 minutos desde ahí
- **Pros:** la duración es siempre 5 minutos de reloj de pared.
- **Cons:** un tipo de evento nuevo en el registro de accesos y una escritura dentro de la evaluación.

## Decision
El usuario eligió la opción 1 el 2026-11-06. `PoliticaBloqueo` sumó `ToleranciaRetroceso` (5 minutos, igual a
`Duracion`) y `Evaluar` pasó a bloquear con `−ToleranciaRetroceso ≤ ahora − ancla < Duracion`; el fin del bloqueo siguió
siendo `ancla + Duracion`.

## Consequences
- Afectó `src/Optica.Domain/Acceso/PoliticaBloqueo.cs`, los comentarios de `IngresarConContrasena` y sus pruebas;
  ADR-006 quedó vigente salvo su consecuencia sobre el atraso de la hora.
- Las defensas de `IngresarConContrasena` para anclas futuras (orden consulta-hora y segunda evaluación) pasaron a
  importar solo para desfases mayores a 5 minutos.
- R-02 de FEAT-001b siguió abierto cuando la hora queda más de 5 minutos antes del ancla, sea por un atraso deliberado o
  por una corrección automática grande, con el mismo límite de 4 intentos.
