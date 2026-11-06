# Threat model FIX-001: El bloqueo de ingreso se levanta si el reloj del sistema retrocede

| Field | Value |
|-------|-------|
| Ticket | FIX-001 |
| Spec | docs/ddw/specs/fix-FIX-001.md |
| Tier | FIX |
| Date | 2026-11-06 |

## Components
| Component | Source in the spec |
|---|---|
| `src/Optica.Domain/Acceso/PoliticaBloqueo.cs` (`Evaluar`, `ToleranciaRetroceso`) | Solution — steps 1 a 3 |
| `src/Optica.Application/Acceso/IngresarConContrasena.cs` (comentarios, sin cambio de código) | Solution — step 4 |

## Trust boundaries
- Navegador de la operadora → API ASP.NET Core: `POST /api/acceso/ingresar` recibe la contraseña; la respuesta es 204
  con cookie de sesión, 401 o 423, sin indicar la hora de fin del bloqueo.
- Sistema operativo de la PC → API: la hora de pared que entrega `TimeProvider.System`; la mueve la sincronización
  horaria de Windows o WSL2, o quien tenga acceso de administrador a la PC.
- API → archivo SQLite `optica.db`: la tabla `EventoAcceso` con la fecha UTC de cada intento.

## STRIDE analysis
### `src/Optica.Domain/Acceso/PoliticaBloqueo.cs` (`Evaluar`, `ToleranciaRetroceso`)
- **Spoofing:** el bloqueo es la defensa contra adivinar la contraseña por fuerza bruta; un retroceso del reloj lo
  levantaba y daba hasta 4 intentos más por retroceso (R-01). Con la tolerancia, un retroceso de hasta 5 minutos ya no
  libera intentos.
- **Tampering:** un atraso que deja la hora más de 5 minutos antes del ancla sigue liberando el bloqueo vigente (R-02,
  ADR-014); hecho a mano exige acceso de administrador a la PC, que ya permite restablecer la contraseña por consola
  (FEAT-001b FR-04); hecho por una corrección automática grande es el riesgo residual aceptado R-04.
- **Repudiation:** sin cambios: cada intento queda en `EventoAcceso` como `ingreso/rechazo`, `ingreso/bloqueado` o
  `ingreso/exito`, con fecha UTC; un intento rechazado por la tolerancia queda como `ingreso/bloqueado`.
- **Information Disclosure:** la respuesta 423 sigue sin decir hasta cuándo dura el bloqueo, así que la tolerancia no
  revela la hora del ancla ni la del servidor.
- **Denial of Service:** un retroceso alarga el bloqueo hasta 10 minutos desde ese momento, puede reactivar un bloqueo
  recién vencido sin un rechazo nuevo y, repetido dentro de la tolerancia, encadenar bloqueos (R-03); la operadora espera
  o restablece la contraseña por consola.
- **Elevation of Privilege:** la política no decide permisos; solo responde bloqueado o libre antes de verificar el hash.

### `src/Optica.Application/Acceso/IngresarConContrasena.cs` (comentarios, sin cambio de código)
- **Spoofing:** el caso de uso no cambia: durante el bloqueo no calcula el hash, ni con la contraseña correcta.
- **Tampering:** la segunda evaluación (`Evaluar([.. rechazosLeidos, ahora], ahora)`) sigue cubriendo la carrera de dos
  pedidos sobre el quinto rechazo; con la tolerancia, la primera evaluación también bloquea anclas futuras de hasta 5
  minutos, que es el lado seguro.
- **Repudiation:** sin cambios en lo que se registra.
- **Information Disclosure:** los comentarios no agregan datos; no hay registros nuevos en el log.
- **Denial of Service:** sin consultas nuevas; el limitador de concurrencia de FEAT-001b sigue igual.
- **Elevation of Privilege:** sin cambios.

## Data classification
| Data | Class | At rest | In transit |
|---|---|---|---|
| Contraseña de acceso | credentials | solo el hash PBKDF2-SHA256 en `CredencialAcceso` (FEAT-001a) | TLS (HTTPS de Kestrel) |
| Fecha y resultado de cada intento de ingreso (`EventoAcceso`) | PII (registro de actividad de la operadora, R-14 de FEAT-001b) | `optica.db` en el perfil de la cuenta de la Api; no se copia a documentos ni al log | no sale de la PC; la Api no la expone |

## Risks and mitigations
Los ID de esta tabla son de FIX-001; los de FEAT-001b se citan con su ticket (por ejemplo, "R-02 de FEAT-001b").

| ID | Risk | STRIDE | Likelihood | Impact | Mitigation |
|---|---|---|---|---|---|
| R-01 | Un retroceso automático del reloj de hasta 5 minutos levanta el bloqueo y permite probar contraseñas | S | H | H | Tolerancia de 5 minutos en `PoliticaBloqueo.Evaluar` (fix-plan, pasos 1 y 2; ADR-014); pruebas de regresión en dominio, aplicación y Api |
| R-02 | Un atraso deliberado que deja la hora más de 5 minutos antes del ancla sigue liberando el bloqueo (continúa R-02 de FEAT-001b) | T | L | M | Exige acceso de administrador a la PC; libera como máximo 4 intentos y el siguiente múltiplo se ancla con la hora nueva (`PoliticaBloqueo_RelojAtrasado_DecimoRechazoBloqueaConLaHoraNueva`) |
| R-03 | Un retroceso alarga el bloqueo hasta 10 minutos desde ese momento, puede reactivar uno recién vencido y, repetido, encadenar bloqueos | D | M | L | Cota fija por retroceso (NFR-01 de FIX-001), probada con `PoliticaBloqueo_RelojAtrasado5Minutos_BloqueadoHastaAnclaMas5`; restablecimiento por consola disponible |
| R-04 | Una corrección automática grande del reloj (servicio de hora de Windows) deja la hora más de 5 minutos antes del ancla y libera el bloqueo sin intervención | S | L | M | Accepted, see below |

## Accepted risks
### R-04
- **Accepted by:** el usuario (dueño del producto), el 2026-11-06, al elegir la tolerancia de 5 minutos y al confirmar este riesgo residual.
- **Justification:** en Windows una corrección de más de 5 minutos hacia atrás es poco probable; bloquear ante cualquier retroceso podría dejar a la operadora bloqueada por horas; cada caso libera como máximo 4 intentos antes del próximo bloqueo.
- **Review conditions:** se revisa si un e2e, un registro de `EventoAcceso` o un reporte de la operadora muestra un desbloqueo por una corrección automática del reloj.

## Supply chain
Sin dependencias nuevas. Las pruebas de Api usan un `TimeProvider` ajustable propio en lugar de `FakeTimeProvider`
(`Microsoft.Extensions.TimeProvider.Testing`, ya instalado), que no retrocede.

## Availability
El único efecto sobre la disponibilidad es R-03: un ingreso bloqueado hasta 10 minutos después de un retroceso chico. No
hay consultas ni escrituras nuevas.
