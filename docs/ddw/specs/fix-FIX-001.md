# Fix-plan FIX-001: El bloqueo de ingreso se levanta si el reloj del sistema retrocede

| Field | Value |
|-------|-------|
| Ticket | FIX-001 |
| Tier | FIX |
| RCA | docs/ddw/specs/rca-FIX-001.md |
| Date | 2026-11-06 |
| Spec loops | 2 |
| Loops since last human decision | 2 |

## Problem
Después del quinto intento fallido, el ingreso queda bloqueado 5 minutos (FR-01 de FEAT-001b). Si el reloj de la PC
retrocede aunque sea un instante (la resincronización de WSL2 lo hace sola), el intento siguiente encuentra el bloqueo
levantado y la contraseña correcta abre la sesión. Se vio en el e2e `[msedge-acceso] acceso.spec.ts:198` del cierre de
FEAT-002c (423 seguido de 204).

## Root cause
`PoliticaBloqueo.Evaluar` (`src/Optica.Domain/Acceso/PoliticaBloqueo.cs:72`) bloquea solo si
`transcurrido >= TimeSpan.Zero && transcurrido < Duracion`, con `transcurrido = ahora − ancla`. Con la hora actual
antes del ancla, `transcurrido` es negativo y el resultado es `Libre`. Era una consecuencia aceptada de ADR-006 (R-02 de
FEAT-001b) que contradice FR-01 cuando el retroceso ocurre solo. Análisis completo en la RCA.

## Solution — steps
1. `src/Optica.Domain/Acceso/PoliticaBloqueo.cs:38` — agregar `public static readonly TimeSpan ToleranciaRetroceso =
   TimeSpan.FromMinutes(5);` junto a `Duracion` (un `TimeSpan` no puede ser `const`), con XML que cite FR-01 y NFR-01 de
   FIX-001.
2. `src/Optica.Domain/Acceso/PoliticaBloqueo.cs:72` — cambiar la condición a
   `transcurrido >= -ToleranciaRetroceso && transcurrido < Duracion`; el fin del bloqueo sigue siendo `ancla + Duracion`
   (FR-01, AC-02). Un retroceso mayor a la tolerancia sigue liberando (FR-02, AC-03); el reloj que avanza no cambia
   (FR-03, AC-04).
3. `src/Optica.Domain/Acceso/PoliticaBloqueo.cs:23-31` — actualizar el XML de `Evaluar`, que hoy dice que una hora
   anterior al ancla no bloquea.
4. `src/Optica.Application/Acceso/IngresarConContrasena.cs:66-67` y `:81-86` — actualizar los comentarios que justifican
   el orden "consulta y después hora" y la segunda evaluación con el argumento de que un ancla futura libera: con la
   tolerancia, esas dos defensas cubren desfases mayores a 5 minutos. Sin cambio de código.
5. `tests/Optica.Domain.Tests/Acceso/PoliticaBloqueoTests.cs:126` — reemplazar
   `PoliticaBloqueo_RelojAtrasado_SinBloqueoNiError` (fija hoy el defecto con `quinto.AddTicks(-1)` libre) por las pruebas
   de regresión de la sección Tests; `:144` (`RelojAtrasado_DecimoRechazoBloqueaConLaHoraNueva`, atraso de 2 h) queda igual.
6. `tests/Optica.Application.Tests/Acceso/IngresarConContrasenaTests.cs` — prueba de regresión del caso de uso con
   `RelojAjustable` (`Dobles.cs:175`, `Avanzar` admite un lapso negativo; actualizar su XML, que dice que la prueba solo lo
   adelanta); y convertir `QuintoRechazoConfirmadoJustoAntesDeLaConsulta_Bloqueado` (`:179`) y
   `CarreraInversa_QuintoConfirmadoPorOtroConHoraPosterior_Bloqueado` (`:204`) en `[Theory]` con dos desfases: 1 s (el
   escenario realista, `Bloqueado`) y `PoliticaBloqueo.ToleranciaRetroceso + TimeSpan.FromSeconds(1)` (sin literal, así
   acompaña a la constante y sigue matando la mutación que invierte el orden de la consulta y la que quita la segunda
   evaluación); ajustar los comentarios que razonan sobre "un segundo".
7. `tests/Optica.Api.Tests/Acceso/AccesoTests.cs` — prueba de regresión de punta a punta en la Api (423 con la
   contraseña correcta tras un retroceso de 1 s). `FakeTimeProvider` no retrocede (`SetUtcNow` lanza), así que se agrega
   una clase privada anidada `RelojConDesfase` en `AccesoTests` (como `HasherBloqueante`, `:826`) que delega en
   `fabrica.Reloj` más un desfase ajustable, registrada por el parámetro `reemplazos` de `FabricaApi`
   (`tests/Optica.Api.Tests/FabricaApi.cs:24`); el `FakeTimeProvider` sigue siendo la única fuente de la hora.
8. `docs/instalacion.md:8-10` — aclarar que, si la hora de la PC retrocede, el bloqueo puede extenderse hasta 10 minutos
   desde ese momento, y remitir a la sección de restablecimiento; no describir que un atraso mayor libera el bloqueo
   (sería una receta para eludirlo).
9. `docs/adr/adr-006-bloqueo-derivado-del-registro-de-eventos.md` — sin cambios: el reemplazo es parcial (solo la
   consecuencia sobre el atraso de la hora) y `Status` admite únicamente `Accepted` o `Superseded by ADR-NNN`
   (F-ADR-04); ADR-014, escrito en PLAN, nombra la consecuencia que reemplaza.

## Dependencies between steps
Los pasos 1 a 3 van juntos (dominio). El 4 depende del 2 (describe su efecto). Los pasos 5 a 7 son las pruebas: se
escriben antes que 1 y 2 (TDD) y fallan contra el código actual. El 8 es documentación y no depende del código; el 9 no
toca archivos. ADR-014 se escribe en PLAN.

## Error handling
- Fecha no UTC en los rechazos o en `ahora` — `ArgumentException` sin valores, como hoy (`ExigirUtc`); la Api responde el
  500 genérico. La tolerancia no cambia esta validación.
- Retroceso mayor a la tolerancia — no es un error: `EstadoBloqueo.Libre`, sin excepción, y el próximo múltiplo de 5
  rechazos se ancla con la hora nueva (FR-02).

## Tests
- [ ] **Regression test** — `PoliticaBloqueo_RelojAtrasado1Segundo_SigueBloqueado`: cinco rechazos y la hora 1 s antes
      del quinto → bloqueado hasta el quinto + 5 min. Falla ANTES del arreglo (da libre), pasa DESPUÉS. Valida AC-01.
- [ ] `PoliticaBloqueo_RelojAtrasado5Minutos_BloqueadoHastaAnclaMas5` — la hora exactamente 5 min antes del quinto →
      bloqueado, `Hasta` = quinto + 5 min. Valida AC-02.
- [ ] `PoliticaBloqueo_RelojAtrasadoMasDe5Minutos_LiberadoSinError` — `ToleranciaRetroceso` + 1 tick, 5 min 1 s, 1 h y 3
      días antes del quinto → libre, `Hasta` nulo y sin excepción (el retroceso mayor no es un error rechazado). Valida
      AC-03.
- [ ] `PoliticaBloqueo_SeisRechazosYRelojAtrasado_BloqueadoDesdeElQuinto` — seis rechazos (el sexto con hora anterior al
      quinto, como en la concurrencia) y la hora 1 s antes del quinto → bloqueado, `Hasta` = quinto + 5 min. Valida AC-01.
- [ ] `PoliticaBloqueo_RelojQueAvanza_BloqueadoHasta5MinutosYLuegoLibre` — 4 min 59 s después del quinto → bloqueado; 5
      min → libre. Valida AC-04.
- [ ] `PoliticaBloqueo_FechasNoUtcONulas_Rechazadas` (existente, `:163`) — sigue rechazando fechas no UTC con la
      tolerancia aplicada.
- [ ] **Regression test** — `Ingresar_RelojAtrasadoTrasElBloqueo_ContrasenaCorrectaBloqueada`: con `RelojAjustable`, cinco
      rechazos, retroceso de 1 s, contraseña correcta → `Bloqueado`, evento `ingreso/bloqueado` y sin calcular el hash.
      Falla ANTES del arreglo. Valida AC-01.
- [ ] **Regression test** — `Ingresar_RelojAtrasado_423ConContrasenaCorrecta` (Api): cinco 401/423 y, tras retroceder 1 s
      el reloj de la Api, `POST /api/acceso/ingresar` con la contraseña correcta → 423 sin cookie de sesión. Falla ANTES
      del arreglo. Valida AC-01.
- [ ] `QuintoRechazoConfirmadoJustoAntesDeLaConsulta_Bloqueado` y
      `CarreraInversa_QuintoConfirmadoPorOtroConHoraPosterior_Bloqueado` (existentes) — con el desfase por encima de la
      tolerancia, siguen matando la mutación que invierte el orden de la consulta y la que quita la segunda evaluación.
- [ ] e2e `cinco intentos incorrectos bloquean el ingreso` (`e2e/tests/acceso.spec.ts:198`) — sin cambios; el harness
      completo pasa en 5 corridas seguidas en Chrome y Edge. Valida AC-05.

## Nota sobre la regla
La regla se mide contra el ancla, no contra el tamaño del atraso (FR-02 de la RCA): libera cuando la hora queda más de 5
minutos antes del ancla. Donde la RCA (Risks and Mitigations) dice "un atraso deliberado de más de 5 minutos", la regla
aplicable es la de FR-02. Dos efectos se siguen de ella y quedan registrados en el modelo de amenazas (R-03): un
retroceso que deja la hora hasta 5 minutos antes del ancla reactiva un bloqueo ya vencido sin un rechazo nuevo, y varios
retrocesos seguidos dentro de la tolerancia encadenan bloqueos; cada uno respeta la cota de 10 minutos desde el
retroceso (NFR-01).

## Regression risk
Bajo. El cambio es una cota de una comparación en una función pura del dominio, con tres llamadas en producción (todas en
`IngresarConContrasena`). Hacia el lado seguro: un retroceso de hasta 5 min alarga el bloqueo hasta 10 min de reloj de
pared (NFR-01 de FIX-001). Lo que podría romperse: que una prueba existente con anclas futuras de menos de 5 min deje de
detectar su mutación (pasos 6 y su prueba) o que se bloquee de más un ingreso legítimo después de un retroceso grande
(cubierto por AC-03).

## Rollback plan *(mandatory)*
- Steps: trivial: revert the commit del arreglo (dominio, comentarios y pruebas); ADR-014 pasa a `Superseded` por un ADR
  nuevo que registre la vuelta atrás.
- Indicators: reportes de la operadora de bloqueos que duran más de 10 minutos, o 423 inesperados después de un cambio de
  hora grande.
