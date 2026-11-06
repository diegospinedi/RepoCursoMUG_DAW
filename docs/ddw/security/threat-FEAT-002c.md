# Threat model FEAT-002c: Recálculo del catálogo por configuración y volumen

| Field | Value |
|-------|-------|
| Ticket | FEAT-002c |
| Spec | docs/ddw/specs/spec-FEAT-002c.md |
| Tier | FEATURE |
| Date | 2026-11-05 |

## Components
| Component | Source in the spec |
|---|---|
| `src/Optica.Domain/Catalogo/Articulo.cs` (`ConPrecioRecalculado`) y `src/Optica.Domain/Configuracion/ConfiguracionNegocio.cs` | Block 1 |
| `src/Optica.Infrastructure/Persistencia/UnidadDeTrabajoEf.cs` (`EjecutarEnTransaccionAsync`) y `ArticuloRepository.cs` | Block 2 |
| `src/Optica.Application/Catalogo/GrabarConfiguracionConRecalculo.cs` | Block 3 |
| `src/Optica.Api/Controllers/ConfiguracionController.cs` (`POST /api/configuracion`) y `web/src/paginas/Configuracion.tsx` | Block 4 |

## Trust boundaries
- Navegador de la operadora → API ASP.NET Core: `POST /api/configuracion` recibe los cuatro parámetros y responde la
  configuración con `preciosCambiados` o el error con los códigos de los artículos; la cookie de sesión viaja en cada
  pedido.
- API → archivo SQLite `optica.db`: una transacción de escritura lee la configuración y los 10.000 artículos y graba la
  configuración y los precios cambiados.

## STRIDE analysis
### `src/Optica.Domain/Catalogo/Articulo.cs` (`ConPrecioRecalculado`) y `src/Optica.Domain/Configuracion/ConfiguracionNegocio.cs`
- **Spoofing:** sin identidades; los invoca el caso de uso detrás de la sesión.
- **Tampering:** el precio sale de la calculadora exacta de ADR-010 sobre el costo y el margen guardados; un precio
  fuera de rango se informa como `FueraDeRango`, nunca como desborde (R-03).
- **Repudiation:** no registran nada.
- **Information Disclosure:** las excepciones no incluyen valores.
- **Denial of Service:** cálculo de costo constante por artículo.
- **Elevation of Privilege:** no deciden permisos.

### `src/Optica.Infrastructure/Persistencia/UnidadDeTrabajoEf.cs` (`EjecutarEnTransaccionAsync`) y `ArticuloRepository.cs`
- **Spoofing:** sin identidades; conexión de `FabricaConexion`.
- **Tampering:** una sola transacción de escritura abarca lecturas y grabación: o se graban la configuración y todos
  los precios cambiados, o nada (FR-02, R-01); los tokens `Version` y `Activo` detectan escrituras cruzadas (R-02).
- **Repudiation:** cada precio cambiado incrementa `Version`.
- **Information Disclosure:** las excepciones de conflicto no adjuntan la causa ni valores.
- **Denial of Service:** el recálculo toma el bloqueo de escritura durante su duración; las demás escrituras esperan
  hasta 30 s y luego fallan con un error genérico; las lecturas siguen por WAL (R-04).
- **Elevation of Privilege:** sin SQL dinámico; consultas de EF parametrizadas.

### `src/Optica.Application/Catalogo/GrabarConfiguracionConRecalculo.cs`
- **Spoofing:** solo detrás del controlador con sesión.
- **Tampering:** valida antes de leer; detecta todos los precios fuera de rango antes de registrar cambios (D-03).
- **Repudiation:** registra en el log, después del commit y con la categoría `Optica.Application.Configuracion.Grabacion`
  que llega al Registro de eventos de Windows (D-17), los valores anteriores y nuevos de la configuración, la fecha UTC
  y la cantidad de precios cambiados (D-11).
- **Information Disclosure:** el log no lleva códigos ni importes de artículos; el error nombra solo códigos
  autonuméricos (D-12) a la operadora con sesión (R-05).
- **Denial of Service:** a lo sumo 10.000 artículos por recálculo (NFR-01), medido en Block 5.
- **Elevation of Privilege:** sin roles; una sola operadora.

### `src/Optica.Api/Controllers/ConfiguracionController.cs` (`POST /api/configuracion`) y `web/src/paginas/Configuracion.tsx`
- **Spoofing:** sesión obligatoria, fuera de `RutasAnonimas` (R-01 de FEAT-001c).
- **Tampering:** CSRF con `SameSite=Strict`, sin CORS y JSON obligatorio; números estrictos.
- **Repudiation:** el caso de uso registra la grabación.
- **Information Disclosure:** 400 con textos fijos más códigos; 500 genérico sin traza; la pantalla valida
  `preciosCambiados` y escapa los textos con React.
- **Denial of Service:** límite de tamaño del cuerpo de FEAT-001c (413).
- **Elevation of Privilege:** sin permisos propios.

## Data classification
| Data | Class | At rest | In transit |
|---|---|---|---|
| Precio de costo, margen y precio de venta | financial (información comercial confidencial de la empresa y de sus proveedores; no es PII) | tabla `Articulo` de `optica.db` en el perfil de la cuenta de la Api, con CHECK; no se registran en el log | TLS (HTTPS de Kestrel), solo con sesión |
| Parámetros de configuración | financial (configuración comercial) | fila única `ConfiguracionNegocio` | TLS, solo con sesión |
| Códigos autonuméricos en el error | public (identificador interno del catálogo) | tabla `Articulo` | TLS, solo con sesión |
| Cookie de sesión | credentials | sin cambios respecto de FEAT-001a: cifrada con Data Protection, claves con DPAPI | TLS, `Secure`, `HttpOnly`, `SameSite=Strict` |

## Risks and mitigations
| ID | Risk | STRIDE | Likelihood | Impact | Mitigation |
|---|---|---|---|---|---|
| R-01 | Configuración nueva grabada con precios viejos, o precios a medio recalcular | T | M | H | Una transacción y un `SaveChanges` (D-03, D-05, ADR-013); detección de fuera de rango antes de registrar |
| R-02 | Alta o edición que graba un precio con la configuración anterior a un recálculo | T | L | M | Lectura de la configuración dentro de la transacción de escritura (D-05); `Version` incrementada en los precios cambiados (D-04) |
| R-03 | Precio que no entra en el tipo y desborda | T | L | M | `FueraDeRango` desde la calculadora; rechazo con códigos (D-01) |
| R-04 | Recálculo que bloquea las escrituras o una operación que se bloquea contra sí misma | D | L | M | Ninguna conexión adicional dentro de la operación; espera de 30 s y error genérico; tiempo medido con 10.000 artículos (Block 5) |
| R-05 | Códigos, costos o márgenes en el log o en mensajes | I | L | M | Log con la configuración y la cantidad (D-11); error con códigos autonuméricos solamente (D-12); records sin imprimir sus miembros (D-28 de FEAT-002b) |

## Supply chain
Sin dependencias nuevas: EF Core, Microsoft.Data.Sqlite y React ya instalados. `npm audit` y
`dotnet list package --vulnerable` se corren en el SAST de CODE.

## Availability
El recálculo de 10.000 artículos toma el bloqueo de escritura del orden de un segundo (R-04); con una sola operadora y
WAL, las lecturas siguen; Block 5 mide la búsqueda con ese volumen (D-02).
