# Spec FEAT-006: Búsqueda y PDF de presupuestos

| Field | Value |
|-------|-------|
| Ticket | FEAT-006 |
| PRD | docs/ddw/prd/prd-FEAT-006.md |
| Tier | FEATURE |
| Date | 2026-11-12 |
| Spec loops | 3 |
| Loops since last human decision | 1 |

## Summary
La búsqueda es un `POST /api/presupuestos/busqueda` (ADR-019) con un filtro del dominio que pliega Apellido y Nombre
con `PlegadoBusqueda` y normaliza el DNI; el repositorio consulta con `instr(plegar(...))`, ordena por número
descendente en páginas de 50 y suma el total en SQL. Una pantalla nueva `/presupuestos` arma el filtro, valida Desde y
Hasta y muestra la grilla paginada. El PDF lo genera `GeneradorPdfPresupuesto` con QuestPDF en Infrastructure (ADR-018)
detrás de `IGeneradorPdfPresupuesto`; `GET /api/presupuestos/{numero}/pdf` lo entrega solo para un Final (409
`borrador` en otro caso), y la pantalla del presupuesto suma el botón Descargar PDF. Decisiones en
`docs/ddw/specs/decisions-FEAT-006.md` (D-01, D-02, D-03 y D-15 del usuario); amenazas en `docs/ddw/security/threat-FEAT-006.md`.

## Coverage: PRD → blocks
| Requirement | Covered by |
|---|---|
| FR-01 | Block 1, Block 2, Block 3 |
| FR-02 | Block 1 |
| FR-03 | Block 1 |
| FR-04 | Block 1 |
| FR-05 | Block 1, Block 3 |
| FR-06 | Block 1, Block 2, Block 3 |
| FR-07 | Block 1, Block 3 |
| FR-08 | Block 1, Block 3 |
| FR-09 | Block 2, Block 3 |
| FR-10 | Block 3 |
| FR-11 | Block 4, Block 5, Block 6 |
| FR-12 | Block 5, Block 6 |
| FR-13 | Block 4 |
| FR-14 | Block 4 |
| FR-15 | Block 4 |
| FR-16 | Block 4 |
| NFR-01 | Strategy: página de 50 por la clave primaria y total en SQL (Block 1); prueba de volumen de la Api sobre 10.000 presupuestos sembrados (Block 2, D-03) y medición de la pulsación a la grilla en e2e (Block 6) |
| NFR-02 | Strategy: los e2e de búsqueda y PDF corren en los proyectos de Chrome y Edge del harness (Block 6) |
| NFR-03 | Block 4 (formato de importes con 2 decimales, D-09) |

## Dependencies between blocks
Block 1 → Block 2 → Block 3. Block 4 → Block 5 → Block 6. Block 6 también depende de Block 3 (el e2e de la búsqueda
abre un presupuesto desde la grilla). Orden de ejecución: 1, 2, 3, 4, 5, 6.

## Block 1 — Filtro y consulta de la búsqueda

**Files**
- `src/Optica.Domain/Presupuestos/FiltroPresupuestos.cs` (new) — el filtro: Apellido y Nombre plegados con
  `PlegadoBusqueda.Plegar`, DNI sin puntos, guiones ni espacios (D-06), Desde y Hasta `DateOnly?`; un texto vacío
  después de normalizar es un filtro no cargado.
- `src/Optica.Domain/Presupuestos/ResumenPresupuesto.cs` (new) — número, fecha, Apellido, Nombre, DNI, estado y total
  de un resultado.
- `src/Optica.Domain/Presupuestos/PaginaPresupuestos.cs` (new) — los resúmenes de la página y el total de coincidencias,
  como `PaginaArticulos`.
- `src/Optica.Domain/Presupuestos/IPresupuestoRepository.cs` (modified) — `BuscarAsync(FiltroPresupuestos filtro, int
  pagina, int tamanoPagina, CancellationToken)`.
- `src/Optica.Infrastructure/Persistencia/PresupuestoRepository.cs` (modified) — implementa `BuscarAsync`.
- `tests/Optica.Application.Tests/Presupuestos/Dobles.cs` (modified) — `PresupuestosEnMemoria` implementa
  `BuscarAsync` con la misma regla, para las pruebas de Block 2.
- `tests/Optica.Api.Tests/RegistroTests.cs` (modified) — `RepositorioQueCambiaLaFecha` delega `BuscarAsync`.
- `tests/Optica.Infrastructure.Tests/Persistencia/ConcurrenciaPresupuestosTests.cs` (modified) — `UltimoNumeroViejo`
  delega `BuscarAsync`.
- `tests/Optica.Domain.Tests/Presupuestos/FiltroPresupuestosTests.cs` (new), `PaginaPresupuestosTests.cs` (new).
- `tests/Optica.Infrastructure.Tests/Persistencia/PresupuestoRepositoryTests.cs` (modified) — pruebas de `BuscarAsync`.

**Logic**
Implementa FR-01 a FR-08. `FiltroPresupuestos.Crear(apellido, nombre, dni, desde, hasta)` lanza
`FiltroInvalidoException` (texto fijo, sin valores) si Desde es posterior a Hasta (FR-06). `BuscarAsync` arma una
consulta EF Core sin seguimiento (expresiones verificadas en la auditoría: `FuncionesSqlite.Plegar(p.Apellido).Contains(x)`
y `p.Dni.Contains(x)` se traducen a `instr(...) > 0` con parámetro; `p.Lineas.Sum(l => l.PrecioFinal)` a
`COALESCE(SUM("PrecioFinalCentavos"), 0)` a través del conversor) que combina con "Y" solo los filtros cargados (FR-02): `instr(plegar(Apellido),
@apellido) > 0` y lo mismo con Nombre (FR-03, D-05), `instr(Dni, @dni) > 0` (FR-04), `Fecha >= @desde` y/o `Fecha <=
@hasta` con ambos límites inclusive (FR-05); sin filtros, todos (FR-07). Ordena por `Numero` descendente, saltea
`(pagina - 1) * tamanoPagina` y toma `tamanoPagina` (FR-08); el total de cada presupuesto es la suma en SQL de
`PrecioFinalCentavos` de sus líneas (D-08) y el total de coincidencias es un `COUNT` con el mismo filtro. Copia los resguardos de
`ArticuloRepository.BuscarAsync`: rechaza el desborde de `(pagina - 1) * tamanoPagina` y usa
`Math.Max(total, filas.Count)` para que una edición entre las dos lecturas no deje el total menor que la página.
`FiltroPresupuestos`, `ResumenPresupuesto` y `PaginaPresupuestos` no muestran Apellido, Nombre ni DNI en `ToString`
(Ley 25.326). Sin
migración (D-12): no hay cambio de esquema que revertir; el rollback es revertir el commit, y la base de la
óptica queda igual.

**Data model**
- Reuses `Presupuesto` and `LineaPresupuesto` from FEAT-003a/c — no schema change; constraints as declared there: PK
  `Numero`, `CK_Presupuesto_Fecha`, `CK_Presupuesto_Estado`, `CK_Presupuesto_Dni` (7 u 8 dígitos), FK
  `LineaPresupuesto.PresupuestoNumero`, PK `(PresupuestoNumero, Orden)`, `CK_LineaPresupuesto_PrecioFinalCentavos`,
  y los triggers de `CierrePresupuestos`, que no se tocan.

**Input validation**
- Apellido y Nombre: texto opcional; recortado y plegado; vacío equivale a no cargado.
- DNI: texto opcional; se quitan `.`, `-` y espacios; vacío equivale a no cargado.
- Desde y Hasta: `DateOnly?`; Desde no puede ser posterior a Hasta.
- `pagina` ≥ 1 y `tamanoPagina` ≥ 1 en `BuscarAsync`, y `(pagina - 1) * tamanoPagina` sin desborde.

**Error handling**
- Desde posterior a Hasta — `FiltroInvalidoException` con texto fijo; no se consulta la base.
- Página o tamaño de página menor que 1, o un salto que desborda, en `BuscarAsync` — `ArgumentOutOfRangeException`
  con texto fijo, sin consultar.
- Una fecha guardada ilegible (pasa el CHECK de formato pero no es una fecha real) — `InvalidOperationException` con el
  texto fijo del repositorio, como `ObtenerAsync`.

**Required tests**
- [ ] `Filtro_ApellidoConAcentosYMayusculas_SePliega` — "  GONZÁLEZ " queda "gonzalez" (FR-03).
- [ ] `Filtro_DniConPuntosYGuiones_SoloDigitos` — "23.456-7" queda "234567" (FR-04).
- [ ] `Filtro_TextosVacios_NoCargados` — blancos y DNI "..-" equivalen a sin filtro.
- [ ] `Filtro_DesdePosteriorAHasta_Rechazado` — `FiltroInvalidoException` sin valores (FR-06, sad path).
- [ ] `Filtro_DesdeIgualAHasta_Valido`.
- [ ] `PaginaPresupuestos_TotalInvalido_Rechazado` — total negativo o menor que la página (sad path).
- [ ] `Buscar_ApellidoParcialSinAcentos_SoloGonzalez` — "ONZALEZ" con González y Gómez (AC-01).
- [ ] `Buscar_NombreSinAcento_SoloMariaJose` — "maria" con María José y Ana (AC-02).
- [ ] `Buscar_DniParcialConPuntos_Coincide` — "3456" y "23.456" encuentran 23456789 (AC-03).
- [ ] `Buscar_ApellidoYDesde_CombinaConY` — AC-04.
- [ ] `Buscar_RangoDesdeHasta_LimitesInclusive` — AC-05.
- [ ] `Buscar_SoloHasta_IncluyeEseDiaYAnteriores` — AC-06.
- [ ] `Buscar_SinFiltros_120_PaginasDe50Descendente` — página 1: 120 a 71; página 3: 20 a 1 (AC-08).
- [ ] `Buscar_TotalEsSumaDePreciosFinales` — total 2.710,02 con las líneas de AC-14.
- [ ] `Buscar_PorcentajeYGuionBajo_NoSonComodines` — "%" y "_" no devuelven todo (R-01, sad path de entrada inválida).
- [ ] `Buscar_DniConLetras_SinCoincidencias` — D-06.
- [ ] `Buscar_PaginaInvalida_Rechazada` — página 0 lanza sin consultar (sad path).
- [ ] `Buscar_PaginaQueDesborda_Rechazada` — `int.MaxValue` lanza sin consultar (sad path).
- [ ] `Buscar_TotalMenorQueLaPagina_SeAjusta` — con un doble de conteo, el total nunca queda menor que la página.
- [ ] `Filtro_ToString_SinDatosDelCliente` y `Resumen_ToString_SinDatosDelCliente`.
- [ ] `Buscar_FechaGuardadaIlegible_ErrorSinValores` — mensaje fijo (sad path).

**Completion criterion**
Las pruebas listadas pasan, la solución compila con los tres dobles actualizados y `dotnet test` sigue en verde.

## Block 2 — Caso de uso y endpoint de la búsqueda

**Files**
- `src/Optica.Application/Presupuestos/BuscarPresupuestos.cs` (new) — caso de uso; `ResultadoBusquedaPresupuestos` y
  `PresupuestoListado` propios de Presupuestos (las pruebas de arquitectura prohíben usar tipos de
  `Optica.Application.Catalogo` y que la Api use tipos del dominio).
- `src/Optica.Application/Presupuestos/ValidacionPresupuesto.cs` (modified) — `CampoDesde`, `CampoHasta`, `CampoPagina`
  y los textos fijos de sus errores.
- `src/Optica.Api/Controllers/PresupuestosController.cs` (modified) — `SolicitudBusquedaPresupuestos` y la acción
  `Buscar`.
- `src/Optica.Api/Program.cs` (modified) — `AddScoped<BuscarPresupuestos>()`.
- `tests/Optica.Application.Tests/Presupuestos/BuscarPresupuestosTests.cs` (new).
- `tests/Optica.Api.Tests/Presupuestos/PresupuestosApiTests.cs` (modified) — búsqueda por la Api.
- `tests/Optica.Api.Tests/Presupuestos/VolumenPresupuestosTests.cs` (modified) — AC-16 (D-03).
- `tests/Optica.Api.Tests/Acceso/ProteccionTests.cs` (modified) — la ruta en `RutasNuevas_SinSesion_401`.
- `tests/Optica.Api.Tests/Acceso/SinCacheTests.cs` (modified) — `no-store` en la búsqueda.
- `tests/Optica.Api.Tests/Presupuestos/CierrePorDefectoPresupuestosTests.cs` (modified) — la solicitud nueva en
  `Solicitudes_ToString_SinValores`.

**Logic**
Implementa FR-01, FR-06 y FR-09 en el borde. `BuscarPresupuestos.EjecutarAsync(apellido, nombre, dni, desde, hasta,
pagina)` lee las fechas `yyyy-MM-dd`, valida largos, página y Desde posterior a Hasta devolviendo `ErrorValidacion`
(como `ValidacionArticulo.ValidarBusqueda`; `FiltroInvalidoException` queda como segunda barrera y no se captura), arma
`FiltroPresupuestos` y llama a `BuscarAsync` con
tamaño de página fijo 50 (FR-08). Devuelve la página con número, fecha, Apellido, Nombre, DNI, estado (`borrador` o
`final`) y total de cada resultado (FR-09). El controlador traduce: válido, 200; inválido, 400 con todos los errores
por campo. No registra el cuerpo (ADR-019). `ResultadoBusquedaPresupuestos` y `PresupuestoListado` tienen
`PrintMembers => false`, como los records de `AltaPresupuesto`.

**API contract**
- Method + path: `POST /api/presupuestos/busqueda`
- Request: `{ apellido?: string, nombre?: string, dni?: string, desde?: string (yyyy-MM-dd), hasta?: string
  (yyyy-MM-dd), pagina?: integer }`; `pagina` ausente es 1. Números JSON estrictos.
- Response: 200 `{ presupuestos: [{ numero: long, fecha: "yyyy-MM-dd", apellido: string, nombre: string, dni: string,
  estado: "borrador" | "final", total: decimal }], pagina: int, tamanoPagina: 50, total: int }`; una página después de
  la última trae la lista vacía y el total.
- Error codes: 400 (cuerpo ilegible, fecha inválida, Desde posterior a Hasta en `desde`, página menor que 1 o mayor que 1.000.000, texto
  demasiado largo), 401 sin sesión, 413 cuerpo de más de 4 KB, 415 sin JSON (`LimitesDelCuerpo`).
- Auth: sesión obligatoria (política por defecto; no está en `RutasAnonimas`); `Cache-Control: no-store` (`SinCacheApi`).

**Input validation**
- `apellido`, `nombre`: string opcional, hasta 100 caracteres después de recortar (D-07).
- `dni`: string opcional, hasta 20 caracteres después de recortar (D-07).
- `desde`, `hasta`: string opcional con formato exacto `yyyy-MM-dd` y fecha real.
- `pagina`: entero opcional entre 1 y 1.000.000.
- Cuerpo: JSON, como máximo 4 KB (`[RequestSizeLimit]`); la solicitud tiene `PrintMembers => false`.

**Error handling**
- Fecha con formato inválido o inexistente — 400 en `desde` o `hasta`, "La fecha no es válida."; no se busca.
- Desde posterior a Hasta — 400 en `desde`, "La fecha Desde no puede ser posterior a Hasta."; no se busca (AC-07).
- Página menor que 1 o mayor que 1.000.000 — 400 en `pagina`; no se busca.
- Texto demasiado largo — 400 en su campo; no se busca.
- Cuerpo ilegible (JSON inválido o tipo incorrecto) — 400 con un solo error y acción fija, sin el valor.
- Sin sesión — 401, antes de leer el cuerpo.

**Required tests**
- [ ] `Buscar_SinFiltros_Pagina1De50` — validates AC-08 en el caso de uso.
- [ ] `Buscar_ResultadoTraeLosSieteDatos` — validates AC-09.
- [ ] `Buscar_FechaInvalida_Rechazada` — 400 en `desde` (sad path).
- [ ] `Buscar_DesdePosteriorAHasta_Rechazado` — 400 en `desde` con el texto de AC-07 (sad path).
- [ ] `Buscar_PaginaInvalida_Rechazada` — 400 en `pagina` (sad path).
- [ ] `Buscar_ApellidoDemasiadoLargo_Rechazado` — 400 en `apellido` (sad path).
- [ ] `PostBusqueda_CuerpoIlegible_400` — JSON inválido y `pagina` como texto, error sin el valor (sad path).
- [ ] `PostBusqueda_SinSesion_401NoAutorizado` — en `ProteccionTests` (sad path).
- [ ] `PostBusqueda_RespuestaNoStore` — en `SinCacheTests`.
- [ ] `PostBusqueda_GonzalezYGomez_SoloGonzalez` — AC-01 por la Api.
- [ ] `Buscar_PaginaFueraDeTope_Rechazada` — 1.000.001 da 400 en `pagina` (sad path).
- [ ] `Volumen_20BusquedasPorApellido_19BajoDosSegundos` — AC-16 sobre 10.000 sembrados (D-03): 10 búsquedas de
  "Ficticio" (coinciden las 10.000, peor caso, primera y última página) y 10 de textos que no coinciden.
- [ ] `ResultadoBusqueda_ToString_SinValores` — los records de Application sin datos personales.
- [ ] `SolicitudBusqueda_ToString_SinValores` — sin datos personales en el log.

**Completion criterion**
Las pruebas pasan; `POST /api/presupuestos/busqueda` responde 200 con la página y 400/401/413/415 como dice el
contrato; la prueba de volumen cumple 19 de 20 bajo 2 s.

## Block 3 — Pantalla de búsqueda

**Files**
- `web/src/paginas/BuscarPresupuestos.tsx` (new) — formulario, grilla y paginación.
- `web/src/paginas/BuscarPresupuestos.test.tsx` (new).
- `web/src/api/cliente.ts` (modified) — `buscarPresupuestos(filtro, pagina)` y sus tipos.
- `web/src/api/cliente.test.ts` (modified).
- `web/src/presupuestos/reglas.ts` (modified) — `validarRangoFechas(desde, hasta)` y el texto de AC-07.
- `web/src/presupuestos/reglas.test.ts` (modified).
- `web/src/App.tsx` (modified) — ruta `/presupuestos`.
- `web/src/App.test.tsx` (modified) — `/presupuestos` en `PANTALLAS` y en las rutas.
- `web/src/paginas/Inicio.tsx` (modified) — enlace "Buscar presupuestos".
- `web/src/paginas/Inicio.test.tsx` (modified).

**Logic**
Implementa FR-01, FR-05 a FR-10 en pantalla. Campos Desde y Hasta (`input type="date"`), Apellido, Nombre y DNI
(`maxLength` 100, 100 y 20) y el botón Buscar (también con Enter). Si Desde es posterior a Hasta no llama a la Api y
muestra junto a Desde "La fecha Desde no puede ser posterior a Hasta." (AC-07), con el mismo patrón de mensaje junto
al campo de FEAT-001. La grilla muestra Número, Fecha (dd/mm/aaaa), Apellido, Nombre, DNI, Estado (Borrador/Final) y
Total (`mostrarImporte`) (FR-09); el número es un enlace a `/presupuestos/:numero` (FR-10). Paginación con Anterior y
Siguiente y "Página X de Y" (FR-08). Los filtros viven solo en el estado del componente (R-02). Un 400 muestra cada
error junto a su campo con `todosLosErrores` de `cliente.ts` (no `primerError`); un 413 usa un `mensaje413` propio
("La búsqueda es demasiado larga."); un 401 vuelve a `/ingreso`.

**Input validation**
- Desde ≤ Hasta cuando están las dos; Apellido y Nombre hasta 100 caracteres y DNI hasta 20 (`maxLength`).

**Error handling**
- Desde posterior a Hasta — mensaje junto a Desde, sin pedido a la Api.
- 400 de la Api — cada error junto a su campo, sin repetir el texto del servidor fuera de su campo.
- 413 de la Api — mensaje general fijo, no en un campo.
- Falla de red o 500 — mensaje general fijo arriba de la grilla; la grilla anterior no se muestra como actual.

**Required tests**
- [ ] `buscar sin filtros muestra la primera página y Siguiente` — validates AC-08 en pantalla.
- [ ] `cada resultado muestra los siete datos` — validates AC-09.
- [ ] `el número abre el presupuesto` — validates AC-10.
- [ ] `desde posterior a hasta: rechazado sin llamar a la Api` — validates AC-07 (sad path).
- [ ] `error 400 inválido de la Api junto al campo` — (sad path).
- [ ] `error de red: mensaje general` — (sad path).
- [ ] `413 rechazado: mensaje general fijo` — (sad path).
- [ ] `validarRangoFechas` — iguales válidas, Desde posterior inválida, una sola válida.
- [ ] `buscarPresupuestos envía el cuerpo y no pone filtros en la URL` — R-02.
- [ ] `App: /presupuestos tiene pantalla` y `Inicio: enlace Buscar presupuestos`.

**Completion criterion**
`npm run test:coverage`, `npm run lint` y `npm run build` en verde con las pruebas listadas.

## Block 4 — Generador del PDF

**Files**
- `src/Optica.Application/Presupuestos/IGeneradorPdfPresupuesto.cs` (new) — `byte[] Generar(Presupuesto presupuesto,
  IdentidadVisual marca)`.
- `src/Optica.Infrastructure/Pdf/GeneradorPdfPresupuesto.cs` (new) — implementación con QuestPDF; fija la licencia
  Community en un constructor estático (D-13), sin fuentes del sistema y con reemplazo de glifos faltantes
  (D-14, D-15).
- `src/Optica.Infrastructure/ConfiguracionInfraestructura.cs` (modified) — registra `IGeneradorPdfPresupuesto`.
- `src/Optica.Infrastructure/Optica.Infrastructure.csproj` (modified) — `QuestPDF` 2026.9.1.
- `src/Optica.Infrastructure/packages.lock.json`, `src/Optica.Api/packages.lock.json`,
  `tests/Optica.Api.Tests/packages.lock.json`, `tests/Optica.Infrastructure.Tests/packages.lock.json` (modified).
- `tests/Optica.Infrastructure.Tests/Optica.Infrastructure.Tests.csproj` (modified) — `PdfPig` 0.1.16.
- `tests/Optica.Infrastructure.Tests/Pdf/GeneradorPdfPresupuestoTests.cs` (new).

**Logic**
Implementa FR-13 a FR-16 y NFR-03. Página A4 con fondo `ColorFondo`; arriba el logo (PNG o JPEG como imagen, SVG como
SVG) y "Presupuesto N.º {numero}" y "Fecha: dd/mm/aaaa" en `ColorPrimario`, sin otros datos de la óptica (FR-16).
Datos del cliente con etiqueta, solo los cargados: Apellido, Nombre, DNI, Domicilio, Email, Nro. de Teléfono (FR-13).
Tabla con Código de Artículo, Descripción, Precio Unitario ($), Cantidad, Porcentaje de Descuento (%), Precio con
Descuento ($) y Precio Final ($), con los encabezados en `ColorPrimario`, cada línea en su orden con los importes
grabados (FR-14), el total "Total: $ {total}" y la leyenda "Precios finales, IVA incluido", sin ningún
importe de IVA (FR-15). Importes con D-09. El título de los metadatos del PDF es "Presupuesto {numero}" y Author, Subject
y Keywords quedan vacíos. Un carácter que la fuente Lato no tiene se dibuja con el símbolo de reemplazo y el PDF se
genera igual (D-15). Toda excepción de QuestPDF se captura y se relanza como `InvalidOperationException` con texto
fijo y sin la excepción interna, porque su mensaje copia el texto que no pudo dibujar (datos del cliente). Las pruebas
arman sus propios logos PNG y JPEG decodificables y usan el `marca-ejemplo/logo.svg` del repo; nunca leen `/Marca/`.

**Data model**
- Reuses `Presupuesto` and `LineaPresupuesto` from FEAT-003a/c — no schema change; constraints as declared
  there: PK `Numero`, `CK_Presupuesto_Estado`, FK `LineaPresupuesto.PresupuestoNumero`, PK `(PresupuestoNumero, Orden)`,
  importes en centésimos con `CK_LineaPresupuesto_PrecioFinalCentavos`, y los triggers de `CierrePresupuestos` intactos.

**Input validation**
- `presupuesto` no nulo y en Final; `marca` no nula (sus colores y su logo ya los validó el dominio, `IdentidadVisual`
  y `LogoMarca`).

**Error handling**
- Presupuesto en Borrador — `ArgumentException` con texto fijo: el generador no arma un PDF de un Borrador aunque se lo
  llame directo (FR-12).
- Presupuesto o marca nulos — `ArgumentNullException`.
- Logo que QuestPDF no puede dibujar (por ejemplo, un PNG con firma válida pero corrupto) o cualquier otra falla de
  QuestPDF — `InvalidOperationException` con texto fijo, sin la ruta, los datos ni la excepción original; la Api
  responde el 500 genérico.

**Required tests**
- [ ] `Pdf_LogoYColoresDeLaMarca` — imagen o SVG del logo presente, texto en #0903A0 y fondo #FFFFFF (AC-11), leído con
  PdfPig.
- [ ] `Pdf_LogoPng_SeDibuja`, `Pdf_LogoJpeg_SeDibuja` y `Pdf_LogoSvg_SeDibuja` — PNG y JPEG generados en la prueba y
  el `marca-ejemplo/logo.svg` real (con texto en Arial), leído desde la raíz del repositorio con un helper como
  `Repositorio.Raiz()` de las pruebas de la Api, también en la CI de Linux.
- [ ] `Pdf_ClienteConGlifoFueraDeLaFuente_SeGeneraConReemplazo` — un emoji en el Apellido no corta la generación (D-15).
- [ ] `Pdf_FuenteIncluidaEnLaSalida` — `QuestPDF.Fonts.Lato.br` está junto al ensamblado; además `PublicacionSinRutasTests`
  verifica que está en la salida de `dotnet publish` de la Api.
- [ ] `Pdf_ClienteSinDomicilioNiEmail_SoloCuatroDatos` — número 155, fecha 10/03/2026, Apellido, Nombre, DNI y Teléfono,
  sin las etiquetas Domicilio ni Email (AC-13).
- [ ] `Pdf_LineasConSieteCamposYTotal` — 6,67, 3, 50, 3,34, 10,02, 2.700,00 y 2.710,02 (AC-14).
- [ ] `Pdf_LeyendaIvaIncluido_SinIvaDiscriminado` — la leyenda presente, la palabra "IVA" solo en ella y ninguna
  alícuota ni importe de IVA (AC-15).
- [ ] `Pdf_MetadatosSinDatosDelCliente` — el título lleva solo el número; Author, Subject y Keywords vacíos.
- [ ] `Pdf_PresupuestoEnBorrador_Rechazado` — `ArgumentException` (sad path).
- [ ] `Pdf_PresupuestoNulo_Rechazado` — (sad path).
- [ ] `Pdf_LogoIlegible_ErrorSinRuta` — PNG corrupto: `InvalidOperationException` con texto fijo, sin excepción
  interna (sad path).

**Completion criterion**
Las pruebas pasan en Windows y en la CI de Linux (`dotnet test` con los lock files actualizados en modo bloqueado).

## Block 5 — Caso de uso y endpoint del PDF

**Files**
- `src/Optica.Application/Presupuestos/GenerarPdfPresupuesto.cs` (new) — lee el presupuesto, rechaza el Borrador, lee
  la marca con `IProveedorMarca` y llama al generador.
- `src/Optica.Api/Controllers/PresupuestosController.cs` (modified) — acción `DescargarPdf`.
- `src/Optica.Api/Errores/RespuestasValidacion.cs` (modified) — `MotivoBorrador = "borrador"` aceptado por
  `ConflictoPresupuesto`, que elige el título según el motivo ("No se pudo generar el PDF del presupuesto." para
  `borrador`) y actualiza su validación y su comentario (D-11, D-16). El detalle fijo "El presupuesto está en
  Borrador." lo devuelve `GenerarPdfPresupuesto` en su resultado, como `ResultadoEdicionPresupuesto`.
- `src/Optica.Api/Program.cs` (modified) — `AddScoped<GenerarPdfPresupuesto>()`.
- `tests/Optica.Application.Tests/Presupuestos/GenerarPdfPresupuestoTests.cs` (new) — con un doble de
  `IProveedorMarca` y del generador.
- `tests/Optica.Api.Tests/Optica.Api.Tests.csproj` (modified) — `PdfPig` 0.1.16.
- `tests/Optica.Api.Tests/Presupuestos/PresupuestosApiTests.cs` (modified) — PDF por la Api.
- `tests/Optica.Api.Tests/Acceso/ProteccionTests.cs`, `SinCacheTests.cs`, `SinDatosArcaTests.cs` (modified) — la ruta
  del PDF (en `SinDatosArcaTests`, con el presupuesto pasado a Final y el cuerpo `application/pdf`).
- `tests/Optica.Api.Tests/Presupuestos/CierrePorDefectoPresupuestosTests.cs` (modified) — el motivo nuevo.
- `tests/Optica.Api.Tests/Acceso/ProteccionTests.cs` también verifica que la Api no referencia QuestPDF.

**Logic**
Implementa FR-11 y FR-12 en el servidor. `GenerarPdfPresupuesto.EjecutarAsync(numero)` devuelve el PDF, no encontrado o
conflicto `borrador`; con un Borrador no llama al generador. El controlador responde 200 `application/pdf` con
`Content-Disposition: attachment; filename="presupuesto-{numero}.pdf"`, sin datos del cliente en el nombre.

**API contract**
- Method + path: `GET /api/presupuestos/{numero:long}/pdf`
- Request: `numero` (long) en la ruta; sin cuerpo ni query string.
- Response: 200 con el PDF (`application/pdf`, `attachment`, `presupuesto-{numero}.pdf`).
- Error codes: 404 presupuesto inexistente (título fijo de FEAT-003a); 409 `{ motivo: "borrador" }` en Borrador; 401 sin
  sesión; 500 genérico si el generador falla.
- Auth: sesión obligatoria (política por defecto); `Cache-Control: no-store`.

**Input validation**
- `numero`: entero `long` por la restricción de ruta; uno que no es entero no coincide con la ruta (404 del ruteo).

**Error handling**
- Presupuesto inexistente — 404 con título fijo.
- Presupuesto en Borrador — 409 `borrador`, sin generar el PDF (AC-12).
- Sin sesión — 401.
- Falla del generador — se propaga al manejador de errores, 500 genérico sin datos del pedido.

**Required tests**
- [ ] `Pdf_PresupuestoFinal_DevuelvePdf` — validates AC-11: `application/pdf`, `attachment`, nombre con solo el número.
- [ ] `GetPdf_Final_ContenidoDeAC13AC14AC15` — leído con PdfPig.
- [ ] `Pdf_PresupuestoBorrador_ConflictoSinGenerar` — el generador no se llama (AC-12, sad path).
- [ ] `GetPdf_Borrador_409ConflictoBorrador` — pedido directo a la Api (AC-12, sad path).
- [ ] `Pdf_PresupuestoInexistente_NoEncontrado404` — (sad path).
- [ ] `GetPdf_SinSesion_401NoAutorizado` — (sad path).
- [ ] `GetPdf_GeneradorConError_500Generico_LogSinDatosDelCliente` — con el generador real, un logo PNG corrupto en la
  marca instalada y un Apellido identificable: ni la respuesta 500 ni el log contienen ese Apellido (sad path).
- [ ] `Api_NoReferenciaQuestPdf`.
- [ ] `GetPdf_RespuestaNoStore`.
- [ ] `ConflictoPresupuesto_MotivoBorrador_Aceptado`.

**Completion criterion**
Las pruebas pasan y `GET /api/presupuestos/{numero}/pdf` responde 200 para un Final y 409 para un Borrador.

## Block 6 — Botón Descargar PDF y e2e

**Files**
- `web/src/paginas/Presupuesto.tsx` (modified) — botón Descargar PDF.
- `web/src/paginas/Presupuesto.test.tsx` (modified).
- `web/src/api/cliente.ts` (modified) — `descargarPdfPresupuesto(numero)` y `'borrador'` en `MotivoConflicto` (D-11).
- `web/src/api/cliente.test.ts` (modified).
- `e2e/tests/presupuestos-busqueda.spec.ts` (new) — búsqueda en Chrome y Edge.
- `e2e/tests/presupuestos-pdf.spec.ts` (new) — descarga en Chrome y Edge.
- `e2e/package.json`, `e2e/package-lock.json` (modified) — `pdfjs-dist` 6.4.299 como dependencia de desarrollo, sin
  worker de hosts externos.
- `e2e/README.md` (modified) — tabla de proyectos y puertos.

**Logic**
Implementa FR-11 y FR-12 en pantalla. La descarga lee la respuesta con `blob()`, por un camino propio de `cliente.ts` que
reusa el manejo de 401 y de 409 pero no `leerJson`. Descargar PDF está habilitado en Final y deshabilitado (`disabled`) en Borrador
(AC-12). Al pulsarlo: `fetch` del PDF, URL de objeto, enlace temporal con `download="presupuesto-{numero}.pdf"`, clic y
revocación (D-10); mientras descarga no lanza otra. Un 409 `borrador` (la pantalla quedó vieja) muestra "El presupuesto
está en Borrador: pasalo a Final para descargar el PDF." y relee el presupuesto; un 401 vuelve a `/ingreso`; otra falla
muestra un mensaje fijo. Los e2e graban presupuestos como la app (Borrador y después Final), buscan y descargan en los
proyectos de Chrome y Edge, y verifican la CSP de la descarga. El PDF descargado se lee en Node (sobre
`download.path()`) con el build `legacy` de pdfjs-dist, sin worker ni descarga de fuentes; el `package-lock.json` se
comprueba con `npm ci` en Windows y en la CI de Linux. Las pruebas de Vitest simulan `URL.createObjectURL`.

**Data model**
- Reuses `Presupuesto` and `LineaPresupuesto` from FEAT-003a/c — no schema change; constraints as declared
  there: PK `Numero`, `CK_Presupuesto_Estado`, FK `LineaPresupuesto.PresupuestoNumero`, PK `(PresupuestoNumero, Orden)`,
  importes en centésimos con `CK_LineaPresupuesto_PrecioFinalCentavos`, y los triggers de `CierrePresupuestos` intactos.

**Input validation**
- `numero` de la ruta de la pantalla: lo valida `usePresupuestoDeLaRuta` (FEAT-003a); el botón usa el número del
  presupuesto cargado.

**Error handling**
- 409 `borrador` — mensaje propio y relectura del presupuesto.
- 401 — navegación a `/ingreso`, como las demás llamadas.
- Falla de red o 500 — mensaje fijo, el botón vuelve a habilitarse.

**Required tests**
- [ ] `Descargar PDF habilitado en Final y descarga presupuesto-N.pdf` — validates AC-11 en pantalla.
- [ ] `Descargar PDF deshabilitado en Borrador` — validates AC-12 (sad path de acción rechazada).
- [ ] `409 borrador: conflicto con mensaje propio y relectura` — (sad path).
- [ ] `error de red al descargar: mensaje fijo` — (sad path).
- [ ] `401 al descargar: no autorizado, va a ingreso` — (sad path).
- [ ] `cliente: 409 con motivo borrador se lee como conflicto`.
- [ ] e2e `búsqueda por apellido, nombre, DNI y fechas` — AC-01 a AC-07 en Chrome y Edge (AC-17).
- [ ] e2e `paginación y abrir un resultado` — AC-08 a AC-10.
- [ ] e2e `buscar muestra la grilla en menos de 2 s` — medición de la pulsación a la grilla (NFR-01, D-03).
- [ ] e2e `descarga el PDF de un Final con el número` — AC-11 con pdfjs-dist; la CSP no bloquea la descarga.
- [ ] e2e `Borrador: botón deshabilitado y la Api rechaza el pedido directo` — AC-12 (sad path).

**Completion criterion**
Pruebas de la web en verde con cobertura, `npm run test:unidad && npm run test:harness` en verde en Chrome y Edge, y la
tabla de `e2e/README.md` coincide con los proyectos del harness.

## Final verification
`validate_spec.py --tier FEATURE` pasa sobre este documento; `dotnet test Optica.sln`, `npm run test:coverage`,
`npm run lint`, `npm run build` en `web/` y el harness e2e en Chrome y Edge pasan; los 17 AC del PRD tienen prueba; la
búsqueda no pone datos del cliente en la URL ni en el log y el PDF de un Borrador no se genera por ningún camino.
