# Threat model FEAT-002b: Búsqueda, edición y baja de artículos

| Field | Value |
|-------|-------|
| Ticket | FEAT-002b |
| Spec | docs/ddw/specs/spec-FEAT-002b.md |
| Tier | FEATURE |
| Date | 2026-11-04 |

## Components
| Component | Source in the spec |
|---|---|
| `src/Optica.Domain/Catalogo/Articulo.cs` (`Modificar`, `ConEstado`) y `PlegadoBusqueda.cs` | Block 1 |
| `src/Optica.Infrastructure/Persistencia/ArticuloRepository.cs`, `FabricaConexion.cs` (función `plegar`) y la migración `CatalogoActivo` | Block 2 |
| `src/Optica.Application/Catalogo/BuscarArticulos.cs`, `ModificarArticulo.cs` y `CambiarEstadoArticulo.cs` | Block 3 |
| `src/Optica.Api/Controllers/ArticulosController.cs` (`GET /api/articulos`, `PUT /api/articulos/{codigo}`, `PUT /api/articulos/{codigo}/estado`) y `LimitesDelCuerpo.cs` | Block 4 |
| `web/src/paginas/Articulos.tsx` y `EditarArticulo.tsx` | Block 5 |

## Trust boundaries
- Navegador de la operadora → API ASP.NET Core: `GET /api/articulos` recibe el texto buscado y la página en la query
  string y devuelve el listado; los dos PUT reciben los datos del artículo, la versión o el estado; la cookie de
  sesión de FEAT-001a viaja en cada pedido.
- API → archivo SQLite `optica.db`: cruzan las lecturas paginadas y las actualizaciones de la tabla `Articulo`, y la
  función `plegar` se ejecuta dentro del motor con código de la aplicación.

## STRIDE analysis
### `src/Optica.Domain/Catalogo/Articulo.cs` (`Modificar`, `ConEstado`) y `PlegadoBusqueda.cs`
- **Spoofing:** sin identidades; los invocan casos de uso detrás de la sesión.
- **Tampering:** `Modificar` rechaza un artículo inactivo y aplica `PoliticaArticulo` completa, así ningún caso de uso
  modifica un inactivo ni guarda datos inválidos (R-03); el precio se recalcula con la calculadora exacta de ADR-010.
- **Repudiation:** no registran nada; la versión del artículo cambia en cada escritura.
- **Information Disclosure:** las excepciones nombran el campo y no el valor.
- **Denial of Service:** `Plegar` es lineal en el largo del texto, que la validación acota a 200 caracteres; nunca
  lanza con UTF-16 inválido (R-05).
- **Elevation of Privilege:** no deciden permisos.

### `src/Optica.Infrastructure/Persistencia/ArticuloRepository.cs`, `FabricaConexion.cs` (función `plegar`) y la migración `CatalogoActivo`
- **Spoofing:** sin identidades; conexión de `FabricaConexion` con sus PRAGMA.
- **Tampering:** consultas de EF Core parametrizadas, incluido el texto buscado (R-02); `instr` en lugar de `LIKE`
  evita comodines; CHECK de `Activo` y `Version`, índice UNIQUE de la clave y token `Version` (R-03, R-04).
- **Repudiation:** cada escritura incrementa `Version` dentro de la transacción de `UnidadDeTrabajoEf`.
- **Information Disclosure:** las filas corruptas se informan sin valores; las excepciones de conflicto no adjuntan
  la causa.
- **Denial of Service:** la búsqueda recorre a lo sumo 10.000 filas y devuelve 50; el desplazamiento se calcula en
  `long` (R-05).
- **Elevation of Privilege:** sin SQL dinámico; `plegar` no se usa en objetos del esquema, así una herramienta externa
  o el backup con `VACUUM INTO` siguen abriendo la base (R-07).

### `src/Optica.Application/Catalogo/BuscarArticulos.cs`, `ModificarArticulo.cs` y `CambiarEstadoArticulo.cs`
- **Spoofing:** se ejecutan solo detrás del controlador con sesión.
- **Tampering:** validan antes de leer o escribir; la versión enviada por la pantalla, obligatoria, detecta una
  edición pisada por otra pestaña (D-08, D-17); el cambio de estado escribe solo `Activo` y `Version` y responde 409
  si otra escritura se cruza (D-09, D-16) (R-04).
- **Repudiation:** no registran en el log el texto buscado ni los datos del pedido (R-06).
- **Information Disclosure:** el duplicado no revela el otro artículo; el listado no incluye costo ni margen (R-01).
- **Denial of Service:** página de 1 a 10.000 y tamaño fijo de 50 (R-05).
- **Elevation of Privilege:** sin roles; una sola operadora (PRD-001).

### `src/Optica.Api/Controllers/ArticulosController.cs` (`GET /api/articulos`, `PUT /api/articulos/{codigo}`, `PUT /api/articulos/{codigo}/estado`) y `LimitesDelCuerpo.cs`
- **Spoofing:** sin `[AllowAnonymous]` y fuera de `RutasAnonimas`; `ProteccionTests` recorre las rutas nuevas (R-01).
- **Tampering:** CSRF: cookie `SameSite=Strict`, sin CORS y JSON obligatorio también en PUT (415); números estrictos
  (R-02).
- **Repudiation:** sin registro de la query string ni del cuerpo; el manejador de errores registra solo la ruta.
- **Information Disclosure:** 400 con textos fijos sin el valor recibido; 404 y 409 con ProblemDetails sin detalles
  internos; 500 genérico (R-06).
- **Denial of Service:** `RequestSizeLimit` de 1024 y 64 bytes (413) y página acotada (R-05).
- **Elevation of Privilege:** las tres acciones exigen la misma sesión; no hay operación de borrado.

### `web/src/paginas/Articulos.tsx` y `EditarArticulo.tsx`
- **Spoofing:** usan la sesión de la SPA; un 401 vuelve al ingreso.
- **Tampering:** el precio de venta es de solo lectura y lo calcula el servidor; la pantalla valida, pero manda el
  servidor (R-03).
- **Repudiation:** sin registro en el navegador.
- **Information Disclosure:** React escapa descripciones y códigos; sin `dangerouslySetInnerHTML`; el texto buscado no
  se guarda en `localStorage` (R-06).
- **Denial of Service:** una búsqueda por acción de la operadora; sin búsqueda en cada tecla.
- **Elevation of Privilege:** sin permisos propios; CSP de la SPA intacta.

## Data classification
| Data | Class | At rest | In transit |
|---|---|---|---|
| Precio de costo y margen de utilidad | financial (información comercial confidencial de la empresa y de sus proveedores; no es PII) | tabla `Articulo` de `optica.db` en el perfil de la cuenta de la Api, con CHECK; no se registran en el log | TLS (HTTPS de Kestrel); solo en el detalle con sesión, nunca en el listado |
| Precio de venta, código en el proveedor y descripción | financial (datos comerciales de proveedores) | tabla `Articulo`, índice UNIQUE sobre la clave normalizada | TLS, solo con sesión |
| Texto buscado | public (palabras sobre el catálogo, sin datos personales) | no se guarda | query string sobre TLS; no se registra |
| Cookie de sesión | credentials | sin cambios respecto de FEAT-001a: cifrada con Data Protection, claves con DPAPI | TLS, `Secure`, `HttpOnly`, `SameSite=Strict` |

## Risks and mitigations
| ID | Risk | STRIDE | Likelihood | Impact | Mitigation |
|---|---|---|---|---|---|
| R-01 | Listado o edición accesible sin sesión, con exposición de costos y márgenes | I | L | H | Política de sesión por defecto, rutas fuera de `RutasAnonimas`, `ProteccionTests`; el listado no trae costo ni margen (D-12) |
| R-02 | CSRF o inyección por la búsqueda o los PUT | T | L | H | `SameSite=Strict`, sin CORS, JSON obligatorio en PUT (D-11); EF Core parametriza; `instr` sin comodines (D-05) |
| R-03 | Modificar un artículo inactivo o con datos inválidos por un camino que no pase por la pantalla | T | M | M | Regla en el dominio (`Articulo.Modificar`, D-02); `PoliticaArticulo`; CHECK en la tabla |
| R-04 | Edición pisada por otra pestaña o por un cambio de estado, o código duplicado en una carrera | T | L | M | Token `Version` con la versión leída por la pantalla (D-08); cambio de estado de solo dos columnas (D-09); índice UNIQUE con motivo `Unicidad` |
| R-05 | Búsqueda o página que agota recursos o desborda el desplazamiento | D | L | M | Texto ≤ 200, página de 1 a 10.000, tamaño fijo 50, desplazamiento en `long`, límites de cuerpo (D-07) |
| R-06 | Texto buscado, costos o detalles internos en el log o en mensajes de error | I | L | M | Sin registro de query string ni cuerpo; textos fijos; excepciones sin valores (D-12) |
| R-07 | Base imposible de abrir fuera de la aplicación si `plegar` u `orden_articulo` entran en el esquema | D | L | H | ADR-011 y D-15: las funciones nunca se usan en índices, CHECK, triggers ni vistas; `Esquema_NingunObjetoUsaPlegar` lo fija con una conexión cruda |

## Supply chain
Sin dependencias nuevas: la función `plegar` usa `Microsoft.Data.Sqlite` (ya referenciado) y la normalización Unicode
de .NET; la web usa React y React Router ya instalados. `npm audit` y `dotnet list package --vulnerable` se corren en
el SAST de CODE.

## Availability
Los vectores de DoS son la búsqueda y los cuerpos de los PUT, acotados por R-05; la aplicación es local, con una sola
operadora, y el catálogo no supera 10.000 artículos (FEAT-002c mide el rendimiento).
