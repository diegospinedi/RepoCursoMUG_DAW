# Threat model FEAT-003d: Concurrencia, volumen y navegadores

| Field | Value |
|-------|-------|
| Ticket | FEAT-003d |
| Spec | docs/ddw/specs/spec-FEAT-003d.md |
| Tier | FEATURE |
| Date | 2026-11-12 |

## Components
| Component | Source in the spec |
|---|---|
| `src/Optica.Infrastructure/Persistencia/FabricaConexion.cs` (`DefaultTimeout = 30`) | Block 1 |
| `tests/Compartido/ArticulosConEncuentro.cs` | Block 1 |
| `tests/Compartido/SiembraPresupuestos.cs` | Block 2 |
| `e2e/tests/presupuestos-concurrencia.spec.ts` | Block 3 |

## Trust boundaries
- Navegador de la operadora → Api (`POST /api/presupuestos`, `PUT /api/presupuestos/{numero}` y `/estado`): sin
  cambios de contrato; dos sesiones con cookie `SameSite=Strict` cruzan el mismo límite a la vez.
- Api → archivo SQLite `optica.db` (modo WAL): dos escritores compiten por `BEGIN IMMEDIATE`; el segundo espera hasta
  `DefaultTimeout` (30 s).
- Código de pruebas → base temporal: el sembrador y el decorador de encuentro escriben solo en directorios temporales
  de `BaseTemporal`, `FabricaApi` y del harness; nunca cruzan hacia una base real del local.

## STRIDE analysis
### `src/Optica.Infrastructure/Persistencia/FabricaConexion.cs` (`DefaultTimeout = 30`)
- **Spoofing:** no cambia la autenticación; cada alta sigue exigiendo sesión (FEAT-001a).
- **Tampering:** la espera serializa las escrituras: la segunda alta lee el máximo ya confirmado, así que no puede
  pisar ni repetir el número de la primera (R-01); el PK `Numero` y los triggers de cierre siguen como red de seguridad.
- **Repudiation:** sin cambio: cada presupuesto conserva su número y su fecha fijos (FEAT-003c); no hay registro nuevo.
- **Information Disclosure:** un SQLITE_BUSY tras la espera termina en el 500 genérico sin datos del pedido ni ruta del
  archivo (`ManejadorErroresNoControlados`, eventos de EF bajados a Debug en `FabricaConexion`).
- **Denial of Service:** una transacción larga (el recálculo de 10.000 artículos de `GrabarConfiguracionConRecalculo`)
  hace esperar a un alta de presupuesto; si supera 30 s, el alta falla con 500 sin grabar (R-02).
- **Elevation of Privilege:** el cambio es solo de cadena de conexión; no agrega rutas ni permisos.

### `tests/Compartido/ArticulosConEncuentro.cs`
- **Spoofing:** solo existe en proyectos de prueba; no se registra en la raíz de composición de la Api (R-04).
- **Tampering:** decora `IArticuloRepository` sin cambiar lo que devuelve; solo retiene la llamada.
- **Repudiation:** no aplica: no escribe nada propio.
- **Information Disclosure:** no maneja datos del cliente; el mensaje de tiempo agotado es fijo.
- **Denial of Service:** un encuentro que no llega colgaría la suite; el tope de 10 s lo convierte en una falla (R-05).
- **Elevation of Privilege:** no aplica: no toca autenticación ni autorización.

### `tests/Compartido/SiembraPresupuestos.cs`
- **Spoofing:** no aplica: escribe directo en la base temporal, sin sesión, y solo desde pruebas.
- **Tampering:** respeta las reglas del sistema: graba en Borrador y después pasa a Final, con los triggers activos;
  una prueba confirma que un INSERT de línea sobre un Final sembrado sigue rechazado (R-03).
- **Repudiation:** no aplica.
- **Information Disclosure:** genera datos de cliente ficticios y deterministas (DNI 99.999.9xx, `@example.com`,
  teléfono `00 0000-0000`) y sus errores no repiten valores; nunca toma una copia de una base real (R-06).
- **Denial of Service:** 10.000 presupuestos alargan la suite; las pruebas van en `ColeccionVolumen` sin paralelismo y
  la CI conserva el tope de 45 minutos (R-07).
- **Elevation of Privilege:** no aplica.

### `e2e/tests/presupuestos-concurrencia.spec.ts`
- **Spoofing:** dos contextos ingresan con la contraseña de prueba del harness, válida solo en la base temporal.
- **Tampering:** solo usa la pantalla y la Api como la operadora; no escribe en la base.
- **Repudiation:** no aplica.
- **Information Disclosure:** datos ficticios; la contraseña de prueba no es un secreto real.
- **Denial of Service:** dos proyectos más en el harness; mismo control que R-07.
- **Elevation of Privilege:** no aplica.

## Data classification
| Data | Class | At rest | In transit |
|---|---|---|---|
| Datos del cliente del presupuesto (apellido, nombre, DNI, domicilio, email, teléfono) | PII | SQLite sin cifrar, permisos NTFS del directorio de datos (riesgo aceptado en FEAT-003a, R-08 abajo) | HTTP local `localhost` entre navegador y Api, `Cache-Control: no-store` |
| Datos sembrados por las pruebas | PII (ficticia) | bases temporales que se borran al terminar | sin tránsito fuera del proceso de pruebas o `localhost` |
| Contraseña de prueba del e2e | credentials | PBKDF2-SHA256 en la base temporal | `localhost` del harness |
| Número y estado del presupuesto | public | SQLite, PK y triggers de cierre | HTTP local |

## Risks and mitigations
| ID | Risk | STRIDE | Likelihood | Impact | Mitigation |
|---|---|---|---|---|---|
| R-01 | dos altas simultáneas reciben el mismo número o una pisa a la otra | T | M | H | `BEGIN IMMEDIATE` con espera explícita de 30 s (Block 1); pruebas con encuentro forzado en integración, Api y navegador; PK `Numero` como red |
| R-02 | una transacción larga hace esperar más de 30 s a un alta, que falla con 500 | D | L | M | tiempo de espera explícito y probado (`Alta_BaseOcupadaMasQueElTiempo_ErrorDeBloqueoSinGrabar`); el alta revierte sin grabar; W-PRD-04 aceptado por el usuario |
| R-03 | el sembrador saltea los triggers y deja Finales con líneas inconsistentes en las pruebas | T | L | M | siembra en Borrador y después Final; `Siembra_FinalesComoEnLaApp_TriggersIntactos` |
| R-04 | el decorador de encuentro llega a producción y retiene altas reales | E | L | H | vive en `tests/Compartido`, enlazado solo en los .csproj de prueba; la Api no lo referencia |
| R-05 | un encuentro roto cuelga la suite y la CI | D | L | M | tope de 10 s con mensaje fijo; `Encuentro_SinSegundaAlta_FallaPorTiempo` |
| R-06 | datos reales de clientes usados como volumen de prueba | I | L | H | sembrador determinista con datos ficticios; errores sin valores; prohibido usar copias de bases del local |
| R-07 | la suite de volumen alarga la CI más allá del tope | D | M | L | `ColeccionVolumen` sin paralelismo; medición en el informe de pruebas; tope de 45 minutos sin cambios |
| R-08 | quien accede a `optica.db` o a un respaldo lee los datos del cliente en claro | I | L | H | sin cambios en esta parte; accepted, see below |

## Accepted risks
### R-08
- **Accepted by:** el usuario, dueño del proyecto, al aprobar el modelo de amenazas de FEAT-003a (R-01 de ese ticket)
- **Justification:** el sistema corre en una PC del local con la base fuera del árbol del código y permisos NTFS;
  `docs/instalacion.md` advierte que la base y los backups guardan los datos sin cifrar; este ticket no cambia cómo se
  guardan.
- **Review conditions:** antes de instalar el sistema con datos reales de clientes, o cuando se implemente el backup
  del PRD 09.

## Supply chain
Sin dependencias nuevas en .NET, web ni e2e. `DefaultTimeout` es una propiedad de Microsoft.Data.Sqlite 10.0.12, ya
fijada en `packages.lock.json`.

## Availability
El vector es la espera por la escritura (R-02): con 2 sesiones (NFR-01) la espera es de milisegundos; solo una
transacción excepcionalmente larga, como el recálculo del catálogo, puede acercarse a 30 s. Superado el tiempo, el alta
falla sin grabar y la operadora puede reintentar.
