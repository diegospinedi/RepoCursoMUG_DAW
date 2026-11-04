# Threat model FEAT-001c: Configuración de parámetros de negocio

| Field | Value |
|-------|-------|
| Ticket | FEAT-001c |
| Spec | docs/ddw/specs/spec-FEAT-001c.md |
| Tier | FEATURE |
| Date | 2026-11-04 |

## Components
| Component | Source in the spec |
|---|---|
| `src/Optica.Domain/Configuracion/ConfiguracionNegocio.cs` | Block 1 |
| `src/Optica.Infrastructure/Persistencia/ConfiguracionNegocioRepository.cs` y la migración `ConfiguracionNegocio` | Block 2 |
| `src/Optica.Application/Configuracion/GrabarConfiguracionNegocio.cs` | Block 3 |
| `src/Optica.Api/Controllers/ConfiguracionController.cs` (`GET` y `POST /api/configuracion`) | Block 4 |
| `web/src/paginas/Configuracion.tsx` | Block 5 |

## Trust boundaries
- Navegador de la operadora (PC del local o de la red local) → API ASP.NET Core: `GET /api/configuracion`
  devuelve los cuatro parámetros y `POST /api/configuracion` recibe los cuatro valores nuevos en un cuerpo
  JSON, con la cookie de sesión de FEAT-001a en cada pedido.
- API → archivo SQLite `optica.db` en `Optica:Datos:Directorio`: cruza la fila única `ConfiguracionNegocio`
  con la alícuota, la condición fiscal, el múltiplo y el tope.
- API → log de la Api: cruza el registro de cada grabación (valores anteriores y nuevos, fecha y hora).

## STRIDE analysis
### `src/Optica.Domain/Configuracion/ConfiguracionNegocio.cs`
- **Spoofing:** no maneja identidades; solo la construyen los casos de uso que corren detrás de la sesión.
- **Tampering:** la entidad rechaza con `ArgumentException` una alícuota fuera de 0 a 100, un múltiplo menor a
  0,01, un tope menor o igual a 0 y más de 2 decimales, así que ningún camino (Api, prueba, carga futura)
  construye un valor inválido (R-03).
- **Repudiation:** no registra nada por sí misma; el registro lo hace el caso de uso (R-04).
- **Information Disclosure:** los mensajes de sus excepciones no incluyen el valor recibido.
- **Denial of Service:** validaciones de costo constante sobre cuatro valores; sin vector.
- **Elevation of Privilege:** no decide permisos.

### `src/Optica.Infrastructure/Persistencia/ConfiguracionNegocioRepository.cs` y la migración `ConfiguracionNegocio`
- **Spoofing:** sin identidades; usa la conexión de `FabricaConexion` como el resto de la persistencia.
- **Tampering:** la tabla tiene `CHECK (Id = 1)`, `CHECK` de rango en los tres enteros y `CHECK` de la condición
  fiscal en ('responsable_inscripto', 'monotributo'): una fila editada a mano con un valor inválido no se graba
  (R-03). EF Core usa parámetros en todas las consultas. La copia o el reemplazo del archivo de base sigue
  cubierto por R-07 de FEAT-001a.
- **Repudiation:** la migración deja los valores iniciales en la misma transacción que crea la tabla.
- **Information Disclosure:** los parámetros no son datos personales ni credenciales; la fila no sale de la Api
  salvo por `GET /api/configuracion` con sesión.
- **Denial of Service:** una fila; lectura por clave primaria.
- **Elevation of Privilege:** sin SQL dinámico ni rutas de archivo derivadas de los datos.

### `src/Optica.Application/Configuracion/GrabarConfiguracionNegocio.cs`
- **Spoofing:** se ejecuta solo detrás del controlador con sesión.
- **Tampering:** valida cada campo antes de construir la entidad y devuelve todos los errores por campo; nunca
  graba una parte de los valores (reemplazo de la fila completa en una sola confirmación de la unidad de
  trabajo) (R-03).
- **Repudiation:** registra en el log de la Api, en nivel Information, cada grabación con los valores anteriores
  y nuevos y la fecha y hora UTC (R-04).
- **Information Disclosure:** los textos de error son fijos y no repiten lo ingresado (R-07).
- **Denial of Service:** sin cálculos costosos; el tamaño del pedido lo acota la Api (R-05).
- **Elevation of Privilege:** no hay roles; una sola operadora con la contraseña compartida (PRD-001).

### `src/Optica.Api/Controllers/ConfiguracionController.cs` (`GET` y `POST /api/configuracion`)
- **Spoofing:** sin `[AllowAnonymous]` y fuera de `RutasAnonimas`: la política por defecto de FEAT-001a exige
  sesión y responde 401 sin ella; la prueba que enumera los endpoints anónimos falla si alguno aparece (R-01).
- **Tampering:** CSRF desde otro sitio con la cookie de la operadora: cookie `SameSite=Strict`, sin CORS y
  `Content-Type: application/json` obligatorio en el POST por `LimitesDelCuerpo` (415) (R-02).
- **Repudiation:** el registro lo hace el caso de uso (R-04).
- **Information Disclosure:** un cuerpo ilegible responde 400 con un texto fijo, sin los mensajes internos del
  lector de JSON; los errores no controlados pasan por `ManejadorErroresNoControlados`, sin traza (R-07).
- **Denial of Service:** `[RequestSizeLimit]` de 1 KB con 413 antes de leer el cuerpo (R-05).
- **Elevation of Privilege:** solo lee y graba la fila de configuración; no expone otros recursos.

### `web/src/paginas/Configuracion.tsx`
- **Spoofing:** consulta `GET /api/configuracion`; sin sesión, el 401 lleva a `/ingreso` como en FEAT-001a.
- **Tampering:** la validación del navegador es comodidad; la que vale es la del servidor (R-03).
- **Repudiation:** no aplica en el navegador.
- **Information Disclosure:** los valores viven solo en el estado del componente; no van a `localStorage`, a la
  URL ni a la consola.
- **Denial of Service:** sin consultas periódicas, igual que el resto de la SPA.
- **Elevation of Privilege:** React escapa todo lo que muestra; sin `dangerouslySetInnerHTML`; la
  `Content-Security-Policy` de la SPA (R-11 de FEAT-001a) sigue aplicando (R-06).

## Data classification
| Data | Class | At rest | In transit |
|---|---|---|---|
| alícuota de IVA, condición fiscal, múltiplo de redondeo, tope de identificación | financial (parámetros de negocio que fijan precios y comprobantes; no son PII ni credenciales) | fila única de `optica.db` en el perfil de la cuenta de la Api, con `CHECK` de rango | TLS (HTTPS de Kestrel), solo con sesión |
| cookie de sesión | credentials | sin cambios respecto de FEAT-001a: cifrada con Data Protection, claves protegidas con DPAPI | TLS, cookie `Secure`, `HttpOnly`, `SameSite=Strict` |
| registro de grabaciones | financial | Registro de eventos de Windows (registro Aplicación) y consola de la Api, sin datos personales ni contraseñas | no sale de la PC |

## Risks and mitigations
| ID | Risk | STRIDE | Likelihood | Impact | Mitigation |
|---|---|---|---|---|---|
| R-01 | `/api/configuracion` queda accesible sin sesión | E | L | H | política por defecto con sesión; los endpoints no llevan `[AllowAnonymous]` ni están en `RutasAnonimas`; prueba de Api que pide los dos sin cookie y espera 401, además de la prueba existente que enumera los endpoints anónimos |
| R-02 | otro sitio hace grabar valores con la cookie de la operadora (CSRF) | T | L | H | `SameSite=Strict`, sin CORS, `Content-Type: application/json` obligatorio en el POST (415); prueba de Api del 415 |
| R-03 | un valor fuera de rango (por ejemplo IVA 210) llega a la base por la Api, por un cliente que no es la SPA o por una edición del archivo | T | M | H | validación por campo en el caso de uso, invariantes de la entidad y `CHECK` en la tabla; pruebas de cada borde en las tres capas |
| R-04 | no se puede reconstruir cuándo cambió un parámetro que alteró precios o comprobantes | R | M | M | registro de cada grabación con valores anteriores y nuevos y fecha y hora UTC, en nivel Information; `appsettings.json` envía esa categoría al Registro de eventos de Windows (se conserva según la política de retención del registro Aplicación de la PC) además de la consola; pruebas de aplicación y de configuración que lo verifican |
| R-05 | cuerpos enormes en `POST /api/configuracion` | D | L | L | `[RequestSizeLimit]` de 1 KB y 413 de `LimitesDelCuerpo`; prueba de Api del 413 |
| R-06 | inyección de scripts al mostrar valores o mensajes | E | L | M | valores numéricos y una condición de dos opciones, mostrados por React con escape; CSP de la SPA |
| R-07 | la respuesta de error expone detalles internos o repite lo enviado | I | L | L | textos de error fijos por campo; cuerpo ilegible con texto fijo; errores no controlados como ProblemDetails sin traza |
| R-08 | dos pestañas abiertas graban valores distintos y la última pisa a la otra | T | L | L | grabación de la fila completa en una transacción; después de grabar la pantalla vuelve a leer y muestra lo guardado, así lo vigente siempre está a la vista |

## Supply chain
Una sola dependencia nueva: `Microsoft.Extensions.Logging.Abstractions` 10.0.12 (Microsoft, NuGet oficial) en
`Optica.Application`, para el registro de R-04; es la misma versión que ya trae el framework compartido de la Api, y
la versión queda fija en el `.csproj` como las demás. El resto usa EF Core 10 y ASP.NET Core ya presentes, y React y
React Router ya fijados en `web/package-lock.json`. El SAST de CODE revisa el código y las dependencias.

## Availability
Los dos endpoints leen o graban una fila por clave primaria, sin cálculos costosos; el único vector es el tamaño
del cuerpo, acotado por R-05. El sistema no se publica en internet (PRD-001, Dependencias), así que un ataque
volumétrico externo no lo alcanza.
