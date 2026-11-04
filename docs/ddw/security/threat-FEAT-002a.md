# Threat model FEAT-002a: Alta de artículos con precio de venta calculado

| Field | Value |
|-------|-------|
| Ticket | FEAT-002a |
| Spec | docs/ddw/specs/spec-FEAT-002a.md |
| Tier | FEATURE |
| Date | 2026-11-04 |

## Components
| Component | Source in the spec |
|---|---|
| `src/Optica.Domain/Catalogo/CalculadoraPrecioVenta.cs` y `PoliticaArticulo.cs` | Block 1 |
| `src/Optica.Infrastructure/Persistencia/ArticuloRepository.cs` y la migración `Catalogo` | Block 2 |
| `src/Optica.Application/Catalogo/AltaArticulo.cs` | Block 3 |
| `src/Optica.Api/Controllers/ArticulosController.cs` (`POST /api/articulos`, `GET /api/articulos/{codigo}`) | Block 4 |
| `web/src/paginas/NuevoArticulo.tsx` | Block 5 |

## Trust boundaries
- Navegador de la operadora → API ASP.NET Core: `POST /api/articulos` recibe código en el proveedor, descripción,
  precio de costo y margen; `GET /api/articulos/{codigo}` devuelve el artículo con su costo, margen y precio de
  venta; la cookie de sesión de FEAT-001a viaja en cada pedido.
- API → archivo SQLite `optica.db`: cruzan las filas de la tabla `Articulo` y la lectura de la fila
  `ConfiguracionNegocio`.

## STRIDE analysis
### `src/Optica.Domain/Catalogo/CalculadoraPrecioVenta.cs` y `PoliticaArticulo.cs`
- **Spoofing:** sin identidades; solo los invocan casos de uso detrás de la sesión.
- **Tampering:** la política rechaza largos, márgenes y escalas fuera de las reglas; la calculadora usa la identidad
  exacta de ADR-010, así ningún resto de la división cambia un precio (R-03).
- **Repudiation:** no registran nada.
- **Information Disclosure:** las excepciones no incluyen los valores recibidos.
- **Denial of Service:** cálculo de costo constante; un precio que no entra en el tipo se rechaza como error del
  campo y no como desborde (R-05).
- **Elevation of Privilege:** no deciden permisos.

### `src/Optica.Infrastructure/Persistencia/ArticuloRepository.cs` y la migración `Catalogo`
- **Spoofing:** sin identidades; conexión de `FabricaConexion`.
- **Tampering:** CHECK de largos y de margen e índice UNIQUE sobre la clave normalizada como segunda barrera (R-03,
  R-04); EF Core parametriza todas las consultas.
- **Repudiation:** el alta queda en la misma transacción de `UnidadDeTrabajoEf`.
- **Information Disclosure:** las filas no salen de la Api salvo por `GET /api/articulos/{codigo}` con sesión.
- **Denial of Service:** lecturas por clave primaria o por índice único.
- **Elevation of Privilege:** sin SQL dinámico.

### `src/Optica.Application/Catalogo/AltaArticulo.cs`
- **Spoofing:** se ejecuta solo detrás del controlador con sesión.
- **Tampering:** valida antes de leer la configuración y de grabar; un duplicado detectado por el índice en una
  carrera se informa igual que el del chequeo previo (D-07, R-04).
- **Repudiation:** los artículos guardan el costo y el margen con los que se calculó su precio; no se registra en el
  log el contenido de los pedidos (R-06).
- **Information Disclosure:** los textos de error son fijos y el de duplicado no revela datos del otro artículo (R-06).
- **Denial of Service:** una alta por pedido; el tamaño lo acota la Api (R-05).
- **Elevation of Privilege:** sin roles; una sola operadora (PRD-001).

### `src/Optica.Api/Controllers/ArticulosController.cs` (`POST /api/articulos`, `GET /api/articulos/{codigo}`)
- **Spoofing:** sin `[AllowAnonymous]` y fuera de `RutasAnonimas`; las pruebas de protección piden las rutas con
  parámetro con un valor de ejemplo en lugar de excluirlas (R-01).
- **Tampering:** CSRF: cookie `SameSite=Strict`, sin CORS y JSON obligatorio en el POST (415) (R-02).
- **Repudiation:** sin registro del cuerpo de los pedidos.
- **Information Disclosure:** cuerpo ilegible → 400 con texto fijo; artículo inexistente → 404 sin detalles;
  errores no controlados sin traza (R-06).
- **Denial of Service:** `[RequestSizeLimit(1024)]` y 413 (R-05).
- **Elevation of Privilege:** el GET solo devuelve el artículo pedido por su código.

### `web/src/paginas/NuevoArticulo.tsx`
- **Spoofing:** verifica la sesión al montarse; un 401 lleva a `/ingreso`.
- **Tampering:** la validación del navegador es comodidad; la que vale es la del servidor.
- **Repudiation:** no aplica en el navegador.
- **Information Disclosure:** los valores viven solo en el estado del componente; no van a `localStorage`, a la URL ni
  a la consola.
- **Denial of Service:** sin consultas periódicas.
- **Elevation of Privilege:** React escapa la descripción y los códigos; sin `dangerouslySetInnerHTML`; CSP de la SPA
  (R-07).

## Data classification
| Data | Class | At rest | In transit |
|---|---|---|---|
| precio de costo, margen de utilidad y precio de venta de cada artículo | financial (información comercial confidencial de la empresa y de sus proveedores; no es PII) | tabla `Articulo` de `optica.db` en el perfil de la cuenta de la Api, con CHECK | TLS (HTTPS de Kestrel), solo con sesión |
| código en el proveedor y descripción | financial (datos comerciales de proveedores) | tabla `Articulo`, índice UNIQUE sobre la clave normalizada | TLS, solo con sesión |
| cookie de sesión | credentials | sin cambios respecto de FEAT-001a: cifrada con Data Protection, claves con DPAPI | TLS, `Secure`, `HttpOnly`, `SameSite=Strict` |

## Risks and mitigations
| ID | Risk | STRIDE | Likelihood | Impact | Mitigation |
|---|---|---|---|---|---|
| R-01 | `/api/articulos` queda accesible sin sesión y expone costos y márgenes | E | L | H | política por defecto con sesión; sin `[AllowAnonymous]`; `ProteccionTests` actualizada para pedir las rutas con parámetro con un valor en lugar de excluirlas |
| R-02 | otro sitio hace grabar artículos con la cookie de la operadora (CSRF) | T | L | M | `SameSite=Strict`, sin CORS, JSON obligatorio en el POST (415) |
| R-03 | un precio de venta mal calculado (un centavo por el resto de la división) llega a presupuestos y facturas | T | M | H | identidad exacta de ADR-010; casos medidos como pruebas de regresión; redondeo hacia +∞ probado con negativos |
| R-04 | dos artículos con el mismo código en el proveedor rompen la actualización por planilla del PRD 03 | T | M | M | clave normalizada en el dominio (ADR-009), chequeo previo e índice UNIQUE; la carrera se informa como duplicado |
| R-05 | un costo o un margen extremo desborda el entero de 64 bits y produce un 500, o un cuerpo enorme satura la Api | D | L | L | `PrecioVentaFueraDeRango` como error del campo (D-06); conversor que rechaza escalas de más; `[RequestSizeLimit(1024)]` |
| R-06 | errores o registros exponen costos, márgenes o datos de otros artículos | I | L | M | textos fijos sin valores; el mensaje de duplicado no nombra al otro artículo; sin registro de cuerpos; 404 sin detalles |
| R-07 | inyección de scripts al mostrar la descripción o los códigos | E | L | M | React escapa el contenido; CSP de la SPA; sin `dangerouslySetInnerHTML` |

## Supply chain
Sin dependencias nuevas: EF Core 10, ASP.NET Core y `Microsoft.Extensions.Logging.Abstractions` ya presentes; React y
React Router fijados en `web/package-lock.json`. El SAST de CODE revisa el código nuevo.

## Availability
El alta lee una fila de configuración y escribe una fila de artículo; el GET lee por clave primaria. El único vector
es el tamaño del cuerpo, acotado por R-05. El sistema no se publica en internet (PRD-001, Dependencias).
