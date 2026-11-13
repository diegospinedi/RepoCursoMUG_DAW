# Spec FEAT-007: Emisión de factura a consumidor final (ARCA WSFEv1)

| Field | Value |
|-------|-------|
| Ticket | FEAT-007 |
| PRD | docs/ddw/prd/prd-FEAT-007.md |
| Tier | FEATURE |
| Date | 2026-11-13 |
| Spec loops | 3 |
| Loops since last human decision | 0 |

## Summary
La configuración limita la alícuota de IVA a las 6 de ARCA al grabar (la tolera al leer, D-06) y suma el punto de venta
(columna nueva, D-07). Un módulo `Facturacion` nuevo arma la solicitud a ARCA desde un presupuesto en Final y la
configuración vigente (tipo B o C, receptor por tope, neto e IVA, concepto y fechas) y guarda la factura inmutable
(triggers `registro_cerrado`). `EmitirFactura` graba una emisión pendiente en una transacción, llama a ARCA fuera de
ella, de a una emisión por vez y con 30 s de tiempo máximo, y en una segunda transacción graba la factura o resuelve la
pendiente (ADR-021). ARCA queda detrás de `IAutorizadorComprobantes`, con el simulador por defecto y el cliente propio de
WSAA y WSFEv1 (ADR-022, ADR-023). `POST /api/presupuestos/{numero}/factura` y el botón Facturar de la pantalla del
presupuesto cierran el flujo. Decisiones en `docs/ddw/specs/decisions-FEAT-007.md` (D-01 a D-05 del usuario); amenazas
en `docs/ddw/security/threat-FEAT-007.md`.

> **Aprobación.** El usuario aprobó esta spec el 2026-11-13, al llegar al límite de 3 rondas de corrección (dos de la
> auditoría de arquitectura y una del validador), junto con el cambio del nombre de la migración en `AGENTS.md` (D-24).

## Coverage: PRD → blocks
| Requirement | Covered by |
|---|---|
| FR-01 | Block 3, Block 7, Block 8 |
| FR-02 | Block 7, Block 8 |
| FR-03 | Block 4, Block 7, Block 8 |
| FR-04 | Block 3, Block 7 |
| FR-05 | Block 3 |
| FR-06 | Block 3 |
| FR-07 | Block 3, Block 7 |
| FR-08 | Block 3, Block 7, Block 8 |
| FR-09 | Block 3, Block 6 |
| FR-10 | Block 3 |
| FR-11 | Block 3, Block 6 |
| FR-12 | Block 3, Block 7 |
| FR-13 | Block 3, Block 7, Block 8 |
| FR-14 | Block 3, Block 6 |
| FR-15 | Block 3, Block 7 |
| FR-16 | Block 3, Block 6 |
| FR-17 | Block 5, Block 6, Block 7 |
| FR-18 | Block 4, Block 7 |
| FR-19 | Block 7, Block 8 |
| FR-20 | Block 7, Block 8 |
| FR-21 | Block 5, Block 6, Block 7, Block 8 |
| FR-22 | Block 7, Block 8 |
| FR-23 | Block 5, Block 6, Block 7, Block 8 |
| FR-24 | Block 4, Block 7, Block 8 |
| FR-25 | Block 4, Block 7 |
| FR-26 | Block 1, Block 2 |
| FR-27 | Block 1, Block 3, Block 7, Block 8 |
| FR-28 | Block 1, Block 2 |
| FR-29 | Block 3, Block 7, Block 8 |
| NFR-01 | Strategy: tiempo máximo de 30 s sobre toda la llamada a ARCA (`Arca:TiempoMaximoSegundos`, D-15), probado con reloj controlado en Block 6 y Block 7 |
| NFR-02 | Strategy: la llamada a ARCA es lo único lento; prueba por la Api de 20 emisiones con un autorizador que demora y registra su espera (Block 7, D-23) |
| NFR-03 | Strategy: los e2e de facturación y configuración corren en los proyectos de Chrome y Edge del harness (Block 2, Block 8; D-03) |
| NFR-04 | Block 3 (importes en centavos y neto redondeado a 2 decimales, D-22), Block 6 (formato con 2 decimales en el SOAP) |

## Dependencies between blocks
Block 1 → Block 2. Block 1 → Block 3 → Block 4 → Block 5 → Block 6. Block 7 depende de Block 1, 3, 4 y 5 (no de Block 6:
prueba con el simulador y dobles). Block 8 depende de Block 2 y Block 7. Orden de ejecución: 1, 2, 3, 4, 5, 6, 7, 8.

## Block 1 — Configuración: alícuotas de ARCA y punto de venta

**Files**
- `src/Optica.Domain/Configuracion/AlicuotasAdmitidas.cs` (new) — las 6 alícuotas admitidas (0, 2,5, 5, 10,5, 21, 27)
  y `EsAdmitida(decimal)`; sin códigos de WSFEv1 (esos viven en `Infrastructure/Arca`, D-20).
- `src/Optica.Domain/Configuracion/PoliticaConfiguracionNegocio.cs` (modified) — `Evaluar` (lectura y escritura) suma el
  rango del punto de venta (`PuntoVentaFueraDeRango`, null admitido); `EvaluarParaGrabar` = `Evaluar` más
  `AlicuotaNoAdmitida`; la alícuota de 0 a 100 sigue siendo invariante de lectura (D-06).
- `src/Optica.Domain/Configuracion/ConfiguracionNegocio.cs` (modified) — `PuntoVenta` (`int?`), `FaltaPuntoVenta`,
  `AlicuotaAdmitidaPorArca`; parámetro `puntoVenta` obligatorio en el constructor (lectura) y fábrica
  `ConfiguracionNegocio.ParaGrabar(...)` que aplica `EvaluarParaGrabar`; `Error` mapea las infracciones nuevas.
- Llamadores del constructor (modified): `tests/Compartido/SiembraCatalogo.cs`,
  `tests/Optica.Application.Tests/Catalogo/AltaArticuloTests.cs`, `tests/Optica.Domain.Tests/Catalogo/ArticuloTests.cs`,
  `tests/Optica.Infrastructure.Tests/Persistencia/ArticuloRepositoryTests.cs` (y sus `DatosConfiguracion`),
  `tests/Optica.Infrastructure.Tests/Persistencia/UnidadDeTrabajoTests.cs`, `tests/Optica.Api.Tests/Catalogo/ArticulosApiTests.cs`;
  `ConfiguracionNegocioRepositoryTests.Reemplazar_SinFila_LanzaError` pasa de alícuota 10 a 10,5.
- `src/Optica.Application/Configuracion/ValidacionConfiguracion.cs` (modified) — `Validar` pasa a
  `EvaluarParaGrabar`; mensajes "La alícuota de IVA debe ser 0,
  2,5, 5, 10,5, 21 o 27." y "El punto de venta debe ser un número entero de 1 a 99999."; `Traducir` cubre las
  infracciones nuevas.
- `src/Optica.Application/Configuracion/DatosConfiguracion.cs` (modified) — `PuntoVenta` opcional.
- `src/Optica.Application/Configuracion/ObtenerConfiguracionNegocio.cs` (modified) — `ConfiguracionVigente` suma
  `PuntoVenta` y `AlicuotasAdmitidas` (parámetros nuevos al final, con valor por defecto).
- `src/Optica.Application/Catalogo/GrabarConfiguracionConRecalculo.cs` (modified) — construye la configuración nueva
  con `ParaGrabar` dentro de la transacción, tomando `anterior.PuntoVenta` cuando el pedido no lo trae (D-27); el
  registro de la grabación suma el punto de venta anterior y el nuevo.
- `src/Optica.Infrastructure/Persistencia/ConfiguracionNegocioRepository.cs` — `Reemplazar` controla primero la regla de
  las 6 alícuotas (`ArgumentException` sin valores si `AlicuotaAdmitidaPorArca == false`) y después la fila faltante
  (`InvalidOperationException`, como hoy): la regla de las 6 es invariante de escritura en cualquier camino.
- `src/Optica.Api/Controllers/ConfiguracionController.cs` (modified) — `SolicitudConfiguracion.PuntoVenta` (`int?`
  estricto), `ConfiguracionVigente`/`ConfiguracionGrabada` con `puntoVenta` y `alicuotasAdmitidas`, arreglo `Campos`.
- `src/Optica.Infrastructure/Persistencia/OpticaDbContext.cs` (modified) — `ConfiguracionNegocioFila.PuntoVenta` con su
  CHECK.
- `src/Optica.Infrastructure/Persistencia/ConfiguracionNegocioRepository.cs` (modified) — mapea el punto de venta.
- `src/Optica.Infrastructure/Persistencia/Migraciones/<marca>_ConfiguracionPuntoVenta.cs` (new), su `.Designer.cs` (new)
  y `OpticaDbContextModelSnapshot.cs` (modified) — escrita a mano (ADR-012).
- Tests (modified): `tests/Optica.Domain.Tests/Configuracion/PoliticaConfiguracionNegocioTests.cs`,
  `ConfiguracionNegocioTests.cs`, `tests/Optica.Domain.Tests/Catalogo/CalculadoraPrecioVentaTests.cs` (100 sigue válido
  al leer), `tests/Optica.Infrastructure.Tests/Persistencia/ConfiguracionNegocioRepositoryTests.cs`,
  `tests/Optica.Application.Tests/Catalogo/GrabarConfiguracionConRecalculoTests.cs`,
  `tests/Optica.Application.Tests/Configuracion/ValidacionConfiguracionTests.cs`,
  `tests/Optica.Application.Tests/Configuracion/ObtenerConfiguracionNegocioTests.cs`,
  `tests/Optica.Api.Tests/Configuracion/ConfiguracionApiTests.cs`, `tests/Optica.Api.Tests/Acceso/SinDatosArcaTests.cs`
  (D-08), `tests/Optica.Infrastructure.Tests/Acceso/AccesoPersistenciaTests.cs`,
  `tests/Optica.Infrastructure.Tests/Persistencia/PresupuestoRepositoryTests.cs` y `ArticuloRepositoryTests.cs`
  (migraciones por nombre, D-29), `tests/Optica.Api.Tests/InstalacionTests.cs` (palabra `Clave`, D-26).

**Logic**
Implementa FR-26, FR-28 y la parte de configuración de FR-27. Al grabar, la alícuota tiene que ser una de
`AlicuotasAdmitidas` (FR-26, AC-27) y el punto de venta, si viene, un entero de 1 a 99999 (FR-28, AC-31). Al leer, una alícuota
de 0 a 100 ya guardada construye la configuración igual, con `AlicuotaAdmitidaPorArca = false` (D-06): ni el GET ni el
recálculo responden 500. El GET devuelve `alicuotasAdmitidas` para la lista de la pantalla (AC-28) y `puntoVenta` (null
mientras no se cargue). La migración agrega la columna con `ALTER TABLE ADD COLUMN` sin reconstruir la tabla; `Down` la
borra con `DROP COLUMN`. Rollback: revertir el commit y aplicar `Down` (solo desarrollo); la columna nueva es nullable y
no afecta lecturas anteriores.

**API contract**
- Method + path: `GET /api/configuracion` y `POST /api/configuracion` (existentes; cambia la forma).
- Request (POST): `alicuotaIva` (number, una de las 6), `condicionFiscal` (string), `multiploRedondeo` (number),
  `topeIdentificacion` (number|null), `puntoVenta` (integer|null, opcional; ausente o null conserva el guardado).
- Response: `alicuotaIva` (number), `condicionFiscal` (string), `multiploRedondeo` (number), `topeIdentificacion`
  (number|null), `faltaTope` (boolean), `puntoVenta` (integer|null), `alicuotasAdmitidas` (number[]); el POST suma
  `preciosCambiados` como hoy.
- Error codes: 400 `ValidationProblemDetails` con `errors.alicuotaIva` o `errors.puntoVenta`; 401 sin sesión; 413 cuerpo
  mayor a 1 KB.
- Auth: sesión obligatoria (política por defecto).

**Data model**
- `ConfiguracionNegocio`: columna nueva `PuntoVenta INTEGER NULL` con `CONSTRAINT "CK_ConfiguracionNegocio_PuntoVenta"
  CHECK ("PuntoVenta" IS NULL OR "PuntoVenta" BETWEEN 1 AND 99999)`; sin default (las filas existentes quedan en null).
  Se mantienen `CK_ConfiguracionNegocio_Alicuota` (0 a 10000 centésimas) y la fila única `Id = 1` (ADR-008).

**Input validation**
- `alicuotaIva`: número JSON estricto, uno de 0, 2,5, 5, 10,5, 21, 27.
- `puntoVenta`: número JSON entero estricto (sin decimales ni texto), de 1 a 99999; ausente o null conserva el valor.

**Error handling**
- Alícuota fuera de las 6 de ARCA (por ejemplo 19) — 400 en `alicuotaIva` con el mensaje de las 6; no se graba nada.
- Punto de venta 0, 100000 o con decimales — 400 en `puntoVenta` con su mensaje; no se graba nada.
- Alícuota guardada fuera de las 6 (base anterior) — se lee sin error, `AlicuotaAdmitidaPorArca = false`.
- Valor guardado que viola el CHECK (punto de venta 0 escrito a mano) — `InvalidOperationException` de estado corrupto,
  como hoy.

**Required tests**
- [ ] `EvaluarParaGrabar_Alicuota19_Rechazado` — AC-27 (sad path).
- [ ] `EvaluarParaGrabar_LasSeisAlicuotas_Validas` — 0, 2,5, 5, 10,5, 21, 27 sin infracciones.
- [ ] `EvaluarParaGrabar_PuntoVentaFueraDeRango_Rechazado` — 0, 100000 (AC-31, sad path).
- [ ] `Grabar_PuntoVentaConDecimales_Invalido400` — 2,5 por la Api (AC-31, sad path).
- [ ] `Grabar_Alicuota19PorApi_Rechazado400` — AC-27 por la Api, sin grabar (sad path).
- [ ] `Grabar_PuntoVentaAusente_ConservaElGuardado` — D-27.
- [ ] `Obtener_AlicuotaGuardada19_SeLeeNoAdmitida` — D-06, sin 500.
- [ ] `Obtener_PuntoVentaGuardadoCero_ErrorDeEstadoCorrupto` — CHECK eludido (sad path).
- [ ] `Get_Configuracion_DevuelveAlicuotasAdmitidasYPuntoVenta` — AC-28 por la Api.
- [ ] `Migracion_PuntoVenta_CheckRechazaCero` — el CHECK de la columna rechaza 0 (sad path).
- [ ] `SinDatosArca_Configuracion_PermitePuntoVentaYRechazaCertificado` — D-08.
- [ ] `Reemplazar_AlicuotaNoAdmitida_Rechazado` — la escritura fuera de `ValidacionConfiguracion` también se frena
  (sad path).
- [ ] `Grabar_PuntoVentaAusente_RegistroConAnteriorYNuevo` — el log de la grabación lleva el punto de venta.

**Completion criterion**
Las pruebas listadas y las existentes actualizadas pasan; `GET /api/configuracion` devuelve `puntoVenta` y
`alicuotasAdmitidas`; una alícuota 19 se rechaza al grabar y se lee sin error.

## Block 2 — Configuración: pantalla

**Files**
- `web/src/configuracion/reglas.ts` (modified) y `reglas.test.ts` (modified) — alícuota de una lista cerrada; regla del
  punto de venta (entero de 1 a 99999).
- `web/src/paginas/Configuracion.tsx` (modified) y `Configuracion.test.tsx` (modified) — la alícuota como `<select>` con
  las `alicuotasAdmitidas` del GET (sin opción elegida y con aviso si la guardada no está); campo "Punto de venta".
- `web/src/api/cliente.ts` (modified) y `cliente.test.ts` (modified) — `leerConfiguracion` lee `puntoVenta` y
  `alicuotasAdmitidas`; `grabarConfiguracion` envía `puntoVenta`; mensaje nuevo de la alícuota.
- `web/src/App.test.tsx` (modified) y `web/src/paginas/Inicio.test.tsx` (modified) — mocks de la configuración con los
  campos nuevos; la prueba de la alícuota 101 pasa a la lista.
- `e2e/tests/configuracion.spec.ts`, `e2e/tests/configuracion-recalculo.spec.ts`, `e2e/tests/articulos-edicion.spec.ts`
  (modified) — eligen la alícuota con `selectOption`; AC-27, AC-28 y AC-31 en Chrome y Edge.

**Logic**
Implementa FR-26 y FR-28 en la pantalla. La lista muestra las 6 alícuotas con coma decimal ("10,5") y la guardada
elegida (AC-28); si la guardada no está entre las 6, ninguna queda elegida y un aviso pide elegir una (D-06). El punto
de venta es un campo de texto numérico; la pantalla valida antes de enviar (AC-31) y la Api valida otra vez.

**Input validation**
- Alícuota: una de las opciones de la lista; sin opción elegida no se envía y se marca el campo.
- Punto de venta: dígitos, sin decimales, de 1 a 99999; vacío conserva el guardado.

**Error handling**
- Punto de venta 0, 100000 o "2,5" — mensaje junto al campo "El punto de venta debe ser un número entero de 1 a
  99999."; no se envía.
- Alícuota sin elegir (guardada fuera de la lista) — mensaje junto al campo; no se envía.
- 400 de la Api en `alicuotaIva` o `puntoVenta` — el mensaje fijo de la pantalla junto al campo.

**Required tests**
- [ ] `alicuota lista: muestra las seis y la guardada elegida` — AC-28.
- [ ] `alicuota guardada 19: ninguna elegida y aviso, envío rechazado` — D-06 (sad path).
- [ ] `punto de venta 0, 100000 y 2,5: inválido sin enviar` — AC-31 (sad path).
- [ ] `400 de la Api en puntoVenta: error junto al campo` — sad path.
- [ ] `leerConfiguracion: respuesta sin alicuotasAdmitidas es inválida` — lector estricto (sad path).
- [ ] e2e `configuración: elegir alícuota y punto de venta y grabar` — AC-28 y AC-31 en Chrome y Edge.
- [ ] e2e `configuración: alícuota 19 por la Api rechazada` — AC-27 (sad path).

**Completion criterion**
`npm run test:coverage`, `npm run lint` y los e2e de configuración pasan en Chrome y Edge.

## Block 3 — Dominio de facturación

**Files**
- `src/Optica.Domain/Facturacion/TipoComprobante.cs` (new) — `FacturaB`, `FacturaC`.
- `src/Optica.Domain/Facturacion/ConceptoFactura.cs` (new) — `Productos`, `ProductosYServicios`.
- `src/Optica.Domain/Facturacion/EntornoArca.cs` (new) — `Simulado`, `Homologacion`, `Produccion`.
- `src/Optica.Domain/Facturacion/ReceptorFactura.cs` (new) — sin identificar o con DNI; sin `ToString` con el DNI.
- `src/Optica.Domain/Facturacion/ImportesFactura.cs` (new) — total, neto, IVA y alícuota; `ParaFacturaB(total,
  alicuota)` (FR-14) y `ParaFacturaC(total)` (FR-16).
- `src/Optica.Domain/Facturacion/SolicitudComprobante.cs` (new) — tipo, punto de venta, fecha, concepto, receptor,
  importes y, con Productos y servicios, las tres fechas iguales a la del comprobante (FR-09).
- `src/Optica.Domain/Facturacion/ReglasEmision.cs` (new) — `Armar(presupuesto, configuracion, concepto, fecha)`:
  precondiciones y solicitud.
- `src/Optica.Domain/Facturacion/Factura.cs` (new) y `LineaFactura.cs` (new) — la factura autorizada (FR-18).
- `src/Optica.Domain/Facturacion/NumeroComprobante.cs` (new) — "Factura B 0003-00000125".
- `src/Optica.Domain/Facturacion/EmisionPendiente.cs` (new) — presupuesto, momento y solicitud del intento.
- `src/Optica.Domain/Facturacion/IFacturaRepository.cs`, `IEmisionPendienteRepository.cs` (new).
- `tests/Optica.Domain.Tests/Facturacion/ReglasEmisionTests.cs`, `ImportesFacturaTests.cs`, `FacturaTests.cs`,
  `NumeroComprobanteTests.cs` (new).
- `tests/Optica.Application.Tests/Arquitectura/DependenciasDeModulosTests.cs` (modified) — `Facturacion` puede depender
  de `Presupuestos` y `Configuracion`, no al revés.

**Logic**
Implementa FR-01, FR-04 a FR-16, FR-27 y FR-29 como reglas puras. `ReglasEmision.Armar` falla, en este orden, si el
presupuesto no está en Final, si falta el tope (FR-13), si falta el punto de venta (FR-29) o si la condición es
Responsable Inscripto y la alícuota no es una de ARCA (FR-27). Si no: tipo B con Responsable Inscripto y C con
Monotributo (FR-05, FR-06); total = total del presupuesto (FR-04); receptor sin identificar si total ≤ tope, DNI del
cliente si total > tope (FR-10, FR-11), con el tope de la configuración recibida (FR-12); en B, neto =
`Math.Round(total / (1 + alicuota / 100), 2, MidpointRounding.AwayFromZero)` e IVA = total − neto (FR-14, FR-15); en C,
solo el total (FR-16); fecha = la recibida (FR-07); concepto elegido y, con Productos y servicios, las tres fechas
iguales a la del comprobante (FR-08, FR-09). `Factura` copia las líneas del presupuesto (FR-01) y no expone métodos que
la cambien (FR-25).

**Data model**
- Reuses no table: este block no cambia el esquema (las tablas las declara Block 4). Invariantes de las entidades,
  que Block 4 repite como constraints de la base: tipo NOT NULL (B o C); punto de venta NOT NULL entre 1 y 99999;
  número NOT NULL ≥ 1; fecha NOT NULL; concepto NOT NULL (dos valores); receptor DNI nullable (null = sin identificar,
  si no 7 u 8 dígitos); importes NOT NULL ≥ 0 con 2 decimales y neto + IVA = total; CAE NOT NULL de 14 dígitos;
  vencimiento del CAE NOT NULL; entorno NOT NULL (tres valores); al menos una línea; una factura unique por presupuesto
  (índice de Block 4).

**Input validation**
- `concepto`: uno de los dos valores del enum.
- Total ≤ `ReglasImporte.ImporteMaximo`; alícuota de `AlicuotasAdmitidas` para la B.

**Error handling**
- Presupuesto en Borrador — `EmisionRechazada` con motivo `Borrador`; no se arma nada.
- Tope sin valor — motivo `SinTope` (FR-13).
- Punto de venta sin valor — motivo `SinPuntoVenta` (FR-29).
- Alícuota no admitida con Responsable Inscripto — motivo `AlicuotaNoAdmitida` (FR-27).
- Datos de la factura fuera de rango al construirla (CAE de otro largo, número < 1) — `ArgumentException` con texto fijo
  sin valores.

**Required tests**
- [ ] `Armar_ResponsableInscripto_FacturaB` — AC-04.
- [ ] `Armar_Monotributo_FacturaC` — AC-05.
- [ ] `Armar_TotalDelPresupuesto_3Lineas3000` — AC-01 (total y líneas).
- [ ] `Armar_FechaDelDia` — AC-06.
- [ ] `Armar_ProductosYServicios_TresFechasIguales` — AC-07.
- [ ] `Armar_Productos_SinFechasDeServicio` — AC-08.
- [ ] `Armar_Total50000Tope100000ConDni_SinIdentificar` — AC-09.
- [ ] `Armar_TotalIgualAlTope_SinIdentificar` — AC-10.
- [ ] `Armar_TotalUnCentavoSobreElTope_ConDni` — AC-11.
- [ ] `Armar_Tope40000Total50000_ConDni` — AC-12.
- [ ] `Importes_1815Alicuota21_Neto1500Iva315` — AC-14.
- [ ] `Importes_1000Alicuota21_Neto826_45Iva173_55` — AC-15.
- [ ] `Importes_1105Alicuota10_5_Neto1000Iva105` — AC-16.
- [ ] `Importes_FacturaC_SoloTotal` — AC-17.
- [ ] `Armar_Borrador_Rechazado` — FR-02 (sad path).
- [ ] `Armar_TopeFaltante_Rechazado` — AC-13 (sad path).
- [ ] `Armar_PuntoVentaFaltante_Rechazado` — AC-32 (sad path).
- [ ] `Armar_Alicuota19ResponsableInscripto_Rechazado` — AC-29 (sad path).
- [ ] `Factura_CaeInvalido_Rechazado` — CAE de 13 dígitos (sad path).
- [ ] `NumeroComprobante_B3_125_Formato` — "Factura B 0003-00000125" (AC-19).
- [ ] `Receptor_ToString_SinDni` — Ley 25.326.
- [ ] `Dependencias_PresupuestosNoDependeDeFacturacion` — arquitectura.


**Completion criterion**
Las pruebas listadas pasan sin base de datos y `dotnet build Optica.sln` compila.

## Block 4 — Persistencia de facturas y emisiones pendientes

**Files**
- `src/Optica.Infrastructure/Persistencia/OpticaDbContext.cs` (modified) — `FacturaFila`, `LineaFacturaFila`,
  `EmisionPendienteFila` y sus CHECK.
- `src/Optica.Infrastructure/Persistencia/FacturaRepository.cs` (new) — `Agregar`, `ObtenerPorPresupuestoAsync`,
  `ExisteParaPresupuestoAsync`.
- `src/Optica.Infrastructure/Persistencia/EmisionPendienteRepository.cs` (new) — `Agregar`, `Quitar`,
  `ExisteAsync(presupuesto)`.
- `src/Optica.Infrastructure/Persistencia/Migraciones/<marca>_Facturas.cs` (new), `.Designer.cs` (new) y
  `OpticaDbContextModelSnapshot.cs` (modified) — tablas, índices y triggers, escrita a mano (ADR-012).
- `src/Optica.Infrastructure/ConfiguracionInfraestructura.cs` (modified) — registra los dos repositorios (scoped).
- `tests/Optica.Infrastructure.Tests/Persistencia/FacturaRepositoryTests.cs`, `EmisionPendienteRepositoryTests.cs`,
  `FacturasTriggersTests.cs` (new).

**Logic**
Implementa FR-18, FR-24 y FR-25 en la base y el índice de FR-03. La factura se graba con sus líneas en la misma
confirmación; no hay métodos de modificación ni de borrado. Los triggers abortan con `RAISE(ABORT, 'registro_cerrado')`,
que `UnidadDeTrabajoEf` ya traduce a `MotivoConflicto.RegistroCerrado`. La pendiente se agrega y se quita; no se
modifica. `Down` borra triggers, índices y tablas (solo desarrollo; AGENTS.md: no se borran en la base de la óptica).
Rollback: revertir el commit; las tablas nuevas no tocan las existentes.

**Data model**
- `Factura`: `Id INTEGER PK`; `PresupuestoNumero INTEGER NOT NULL` FK a `Presupuesto(Numero)` RESTRICT con índice único
  `IX_Factura_PresupuestoNumero` (FR-03); `Tipo TEXT NOT NULL CHECK IN ('b','c')`; `PuntoVenta INTEGER NOT NULL CHECK
  BETWEEN 1 AND 99999`; `Numero INTEGER NOT NULL CHECK >= 1`; índice único `IX_Factura_Comprobante (Entorno, Tipo,
  PuntoVenta, Numero)`; `Fecha TEXT NOT NULL` (`yyyy-MM-dd`, CHECK de formato); `Concepto TEXT NOT NULL CHECK IN
  ('productos','productos_y_servicios')`; `ReceptorDni TEXT NULL CHECK` 7 u 8 dígitos (null = sin identificar);
  `TotalCentavos`, `NetoCentavos`, `IvaCentavos INTEGER NOT NULL CHECK >= 0` y `CK_Factura_Importes` (neto + iva =
  total); `AlicuotaCentesimas INTEGER NULL` (solo B); `Cae TEXT NOT NULL CHECK` 14 dígitos; `VencimientoCae TEXT NOT
  NULL`; `Entorno TEXT NOT NULL CHECK IN ('simulado','homologacion','produccion')`.
- `Factura.CantidadLineas INTEGER NOT NULL CHECK >= 1`; `VencimientoCae` con el mismo CHECK de formato (GLOB) que
  `Fecha`.
- `LineaFactura`: PK `(FacturaId, Orden)`, FK `FacturaId` RESTRICT; columnas y CHECK con los mismos nombres que
  `LineaPresupuesto`: `Orden >= 1`, `CodigoArticulo`, `Descripcion` (1 a 200), `PrecioUnitarioCentavos >= 0`,
  `DescuentoCentesimos BETWEEN 0 AND 10000`, `PrecioConDescuentoCentavos >= 0`, `Cantidad >= 1`,
  `PrecioFinalCentavos >= 0`, todas NOT NULL; sin FK al artículo (la factura conserva la descripción y los importes).
- `EmisionPendiente`: `PresupuestoNumero INTEGER PK` FK RESTRICT; `Momento TEXT NOT NULL` (UTC ISO); `Tipo`,
  `PuntoVenta`, `Fecha`, `TotalCentavos`, `Entorno` NOT NULL con los CHECK de `Factura` (los datos que verifica el
  PRD 07).
- Triggers: `TR_Factura_Inmutable` (BEFORE UPDATE ON `Factura`), `TR_Factura_NoSeBorra` (BEFORE DELETE ON `Factura`),
  `TR_LineaFactura_Inmutable` (BEFORE UPDATE ON `LineaFactura`), `TR_LineaFactura_NoSeBorra` (BEFORE DELETE ON
  `LineaFactura`) y `TR_LineaFactura_Cerrada` (BEFORE INSERT ON `LineaFactura`, cuando la factura ya tiene
  `CantidadLineas` líneas), todos `RAISE(ABORT, 'registro_cerrado')`. `OpticaDbContext` lleva para las tres tablas el
  comentario "ADVERTENCIA (ADR-012, ADR-017): no se reconstruye", como `Presupuesto` y `LineaPresupuesto`.

**Input validation**
- Los valores llegan del dominio (Block 3); la base los vuelve a acotar con los CHECK listados.

**Error handling**
- Segunda factura para el mismo presupuesto — `ConflictoAlConfirmarException` con `MotivoConflicto.Unicidad`.
- Segunda pendiente para el mismo presupuesto — `ConflictoAlConfirmarException` con `MotivoConflicto.Unicidad`.
- UPDATE o DELETE de una factura o sus líneas — `ConflictoAlConfirmarException` con `MotivoConflicto.RegistroCerrado`.
- Fila guardada ilegible — `InvalidOperationException` con texto fijo, como `PresupuestoRepository`.

**Required tests**
- [ ] `Agregar_Factura_SeLeeConSusLineasYEntorno` — AC-18 (FR-18).
- [ ] `Agregar_SegundaFacturaMismoPresupuesto_ConflictoDuplicado` — AC-03 en la base (sad path).
- [ ] `Agregar_SegundaPendiente_ConflictoDuplicado` — FR-24 (sad path).
- [ ] `Update_Factura_RechazadoRegistroCerrado` — AC-23 en SQLite (sad path).
- [ ] `Delete_Factura_RechazadoRegistroCerrado` — AC-23 (sad path).
- [ ] `Update_LineaFactura_RechazadoRegistroCerrado` — AC-23 (sad path).
- [ ] `Check_ImportesQueNoSuman_Rechazado` — `CK_Factura_Importes` (sad path).
- [ ] `Obtener_FilaIlegible_ErrorSinValores` — sad path.
- [ ] `Quitar_Pendiente_YaNoExiste`.
- [ ] `Insert_LineaEnFacturaCompleta_RechazadoRegistroCerrado` — AC-23 (sad path).
- [ ] `Migraciones_TodasAplicadas_LosCincoTriggersExisten` — los triggers siguen después de todas las migraciones.

**Completion criterion**
Las pruebas listadas pasan contra una base temporal y la migración aplica y revierte sobre una base vacía.

## Block 5 — Puerto de ARCA, opciones y simulador

**Files**
- `src/Optica.Application/Facturacion/IAutorizadorComprobantes.cs` (new) — `AutorizarAsync(SolicitudComprobante,
  CancellationToken)` → `ResultadoAutorizacion`; `Entorno`; `TiempoMaximo` (el plazo que aplica `EmitirFactura`). El
  autorizador no aplica un plazo propio: solo clasifica la cancelación que recibe.
- `src/Optica.Application/Facturacion/ResultadoAutorizacion.cs` (new) — `Autorizada(numero, cae, vencimiento)`,
  `Rechazada(codigo, descripcion)`, `NoEnviada`, `SinRespuesta` (D-16).
- `src/Optica.Infrastructure/Arca/OpcionesArca.cs` (new) — lee `Arca:Entorno` (por defecto `Simulado`), `Arca:Cuit`,
  `Arca:Certificado:Ruta`, `Arca:Certificado:Clave`, `Arca:TiempoMaximoSegundos` (30), `Arca:Simulado:Respuesta`.
- `src/Optica.Infrastructure/Arca/SimuladorArca.cs` (new) — D-17.
- `src/Optica.Infrastructure/Arca/CodigosWsfe.cs` (new) — códigos de WSFEv1 de D-20 (tipo, documento, condición del
  receptor, concepto y alícuota → `Id`); solo los usan `ClienteWsfe` y el simulador.
- `src/Optica.Infrastructure/ConfiguracionInfraestructura.cs` (modified) — registra el autorizador según el entorno;
  advertencia en el log al arrancar en simulado (R-10).
- `src/Optica.Api/appsettings.json` (modified) — `Arca:Entorno` = `Simulado`, sin certificado ni clave.
- `tests/Optica.Infrastructure.Tests/Arca/OpcionesArcaTests.cs`, `SimuladorArcaTests.cs` (new).

**Logic**
Implementa la parte de FR-17, FR-21 y FR-23 que no depende de la red. `OpcionesArca` valida las claves y elige el
entorno; sin `Arca:Entorno` es simulado (R-14). El simulador numera por tipo y punto de venta en `arca-simulado.json`
(escritura atómica: archivo temporal y reemplazo), devuelve un CAE de 14 dígitos y vencimiento a 10 días, y responde
según `Arca:Simulado:Respuesta`: `autorizar`, `rechazar` (código 10016 con descripción fija) o `no_responder` (espera
hasta la cancelación). El entorno se informa en el resultado para guardarlo en la factura (FR-18).

**Input validation**
- `Arca:Entorno`: `Simulado`, `Homologacion` o `Produccion` (sin distinguir mayúsculas).
- `Arca:TiempoMaximoSegundos`: entero de 1 a 120.
- `Arca:Simulado:Respuesta`: `autorizar`, `rechazar` o `no_responder`.
- `Arca:Cuit` (fuera de simulado): 11 dígitos con dígito verificador válido.

**Error handling**
- Entorno, tiempo máximo o respuesta del simulador inválidos — la Api no arranca: crítico en el log con la clave y sin el
  valor, como el directorio de datos.
- Homologación o producción sin CUIT, certificado o clave — la Api arranca y cada emisión termina "no enviada" con el
  mensaje fijo "La facturación con ARCA no está configurada."; advertencia en el log al arrancar.
- `arca-simulado.json` ilegible — el simulador responde "no enviada" y registra el error sin el contenido.

**Required tests**
- [ ] `Opciones_SinEntorno_Simulado` — R-14.
- [ ] `Opciones_EntornoInvalido_Rechazado` — sad path.
- [ ] `Opciones_TiempoMaximoInvalido_Rechazado` — 0 y 121 (sad path).
- [ ] `Opciones_CuitInvalido_Rechazado` — dígito verificador (sad path).
- [ ] `Opciones_HomologacionSinCertificado_NoEnviada` — sad path.
- [ ] `Simulador_Autorizar_NumeraConsecutivoPorTipoYPuntoVenta` — 1, 2 en B/3; 1 en C/3.
- [ ] `Simulador_Rechazar_DevuelveCodigoYDescripcion` — AC-20 (rechazo).
- [ ] `Simulador_NoResponder_TimeoutPorCancelacion` — AC-21 (timeout).
- [ ] `Simulador_ArchivoIlegible_NoEnviadaError` — sad path.

**Completion criterion**
Las pruebas listadas pasan y la Api arranca en simulado sin ninguna clave `Arca`.

## Block 6 — Cliente de WSAA y WSFEv1

**Files**
- `src/Optica.Infrastructure/Arca/CertificadoArca.cs` (new) — carga el PFX con su clave
  (`X509CertificateLoader.LoadPkcs12FromFile`), sin dejar la clave en mensajes.
- `src/Optica.Infrastructure/Arca/ClienteWsaa.cs` (new) — pedido de acceso (TRA) para `wsfe`, firma con `SignedCms`,
  `loginCms`, ticket en memoria y en `arca-ta-<entorno>.json` cifrado con `IProtectorDatos` (D-14, D-30).
- `src/Optica.Application/Comun/IProtectorDatos.cs` (new) y `src/Optica.Api/Web/ProtectorDatos.cs` (new) — puerto
  neutro `Proteger`/`Desproteger` de bytes con un propósito, implementado en la Api con `IDataProtectionProvider` y
  registrado en `Program.cs`; `ClienteWsaa` lo usa con el propósito "Optica.Arca.Ticket"; un archivo que no se descifra se
  descarta y se pide otro ticket. La Api no conoce tipos de ARCA.
- `tests/Optica.Api.Tests/Arquitectura/AislamientoArcaTests.cs` (new).
- `src/Optica.Infrastructure/Arca/ClienteWsfe.cs` (new) — SOAP de `FECompUltimoAutorizado` y `FECAESolicitar` con
  `System.Xml.Linq` (D-12, D-20).
- `src/Optica.Infrastructure/Arca/AutorizadorWsfe.cs` (new) — orquesta token → último número → solicitud y clasifica el
  resultado (D-16); la cancelación que recibe (el plazo de `EmitirFactura`) antes de enviar `FECAESolicitar` es "no
  enviada" y después es "sin respuesta".
- `src/Optica.Infrastructure/Optica.Infrastructure.csproj` (modified) — `System.Security.Cryptography.Pkcs` 10.0.12.
- `src/Optica.Infrastructure/packages.lock.json`, `src/Optica.Api/packages.lock.json`,
  `tests/Optica.Api.Tests/packages.lock.json`, `tests/Optica.Infrastructure.Tests/packages.lock.json` (modified).
- `src/Optica.Infrastructure/ConfiguracionInfraestructura.cs` (modified) — un `HttpClient` propio (singleton, sin
  callback de certificados) para homologación y producción.
- `tests/Optica.Infrastructure.Tests/Arca/ClienteWsaaTests.cs`, `ClienteWsfeTests.cs`, `AutorizadorWsfeTests.cs` (new),
  `tests/Optica.Infrastructure.Tests/Arca/Respuestas/*.xml` (new) — respuestas grabadas con datos ficticios y un
  certificado autofirmado generado en la prueba.

**Logic**
Implementa FR-17 contra ARCA real y la clasificación de FR-21 y FR-23. URLs constantes por entorno (homologación:
`wsaahomo.afip.gov.ar` y `wswhomo.afip.gov.ar`; producción: `wsaa.afip.gov.ar` y `servicios1.afip.gov.ar`). El ticket se
reutiliza mientras falten más de 10 minutos para su vencimiento. El pedido de `FECAESolicitar` lleva `CbteDesde =
CbteHasta = último + 1`, `CbteFch` `yyyyMMdd`, importes con punto decimal y 2 decimales (NFR-04), `DocTipo`/`DocNro`
(FR-10, FR-11), `CondicionIVAReceptorId` 5, `Concepto` y fechas de servicio (FR-09), e `Iva/AlicIva` solo en B (FR-14,
FR-16). Resultado `A` → autorizada; `R` o `Errors` → rechazada con el primer código y su descripción (recortada a 500
caracteres); falla antes de enviar `FECAESolicitar` → no enviada; sin respuesta legible después de enviarlo, o
cancelación por el tiempo máximo (NFR-01) → sin respuesta. Ningún cuerpo SOAP, token, firma ni DNI va al log; las
excepciones se relanzan con texto fijo y sin causa (R-02, R-07). Lectura de XML con `DtdProcessing.Prohibit` y tope de
1 MB (R-12).

**Input validation**
- Respuestas de ARCA: XML de hasta 1 MB, sin DTD; `CAE` de 14 dígitos y `CAEFchVto` `yyyyMMdd`, si no es "sin respuesta".
- `arca-ta-<entorno>.json`: JSON con token, firma y vencimiento; ilegible o vencido se descarta y se pide otro.

**Error handling**
- WSAA rechaza (por ejemplo "ya posee un TA válido" sin ticket guardado) — no enviada; log con el código de WSAA.
- WSAA sin respuesta o error de red antes de `FECAESolicitar` — no enviada.
- `FECompUltimoAutorizado` con error — no enviada.
- `FECAESolicitar` con resultado `R` o `Errors` — rechazada con código y descripción.
- Tiempo máximo vencido después de enviar `FECAESolicitar`, o respuesta ilegible — sin respuesta.
- PFX ilegible o clave incorrecta — no enviada; log sin la clave ni la ruta completa.

**Required tests**
- [ ] `Wsaa_FirmaCms_VerificableConElCertificado` — FR-17.
- [ ] `Wsaa_TicketVigente_SeReutiliza` — D-14.
- [ ] `Wsaa_TicketGuardado_CifradoEnDisco` — el archivo no contiene el token en claro (D-30).
- [ ] `Dependencias_SoloInfrastructureArcaUsaTiposDeArca` (en `tests/Optica.Api.Tests/Arquitectura/AislamientoArcaTests.cs`)
  — revisa los tipos de Domain, Application, Infrastructure y Api, afirma que `SimuladorArca` y `ClienteWsfe` están entre
  los revisados y que ningún tipo fuera de `Optica.Infrastructure.Arca` usa tipos de ese espacio, salvo la raíz de
  composición `ConfiguracionInfraestructura` (aislamiento de AGENTS.md).
- [ ] `Wsaa_TicketGuardadoIlegible_SePideOtro` — sad path de entrada inválida.
- [ ] `Wsaa_Rechazo_NoEnviadaError` — sad path.
- [ ] `Wsaa_SinRespuesta_NoEnviadaTimeout` — sad path.
- [ ] `Wsfe_FacturaB_XmlConIvaYDni` — AC-11, AC-14 sobre el SOAP enviado.
- [ ] `Wsfe_FacturaC_XmlSinIva` — AC-17.
- [ ] `Wsfe_ProductosYServicios_XmlConFechas` — AC-07.
- [ ] `Wsfe_UltimoAutorizado_Error_NoEnviada` — sad path.
- [ ] `Autorizador_ResultadoA_Autorizada` — AC-18 contra respuesta grabada.
- [ ] `Autorizador_ResultadoR_RechazadaConCodigo` — AC-20 (rechazo).
- [ ] `Autorizador_Timeout30sDespuesDeEnviar_SinRespuesta` — AC-21 con reloj controlado.
- [ ] `Autorizador_RespuestaIlegible_SinRespuestaError` — sad path.
- [ ] `Autorizador_PfxClaveIncorrecta_NoEnviadaError` — sad path.
- [ ] `Autorizador_XmlConDtd_Rechazado` — R-12 (sad path).
- [ ] `Autorizador_Log_SinTokenNiDniNiClave` — R-02, R-07.
- [ ] `Autorizador_Homologacion_PuntoVentaDeLaConfiguracionSinCertificadoEnPantalla` — AC-24 contra respuestas grabadas
  de homologación; la obtención de un CAE real queda como verificación manual del usuario con el certificado (D-02).

**Completion criterion**
Las pruebas listadas pasan sin red (respuestas grabadas) y los `packages.lock.json` se restauran en modo bloqueado.

## Block 7 — Caso de uso y endpoint de emisión

**Files**
- `src/Optica.Application/Facturacion/EmitirFactura.cs` (new) — D-09, D-10, D-16, D-32.
- `src/Optica.Application/Facturacion/ResultadoEmision.cs` (new) — `Emitida(FacturaEmitida)`, `Invalido`,
  `NoEncontrado`, `Conflicto(motivo, codigo?, descripcion?)`.
- `src/Optica.Application/Facturacion/FacturaEmitida.cs` (new) — `comprobante` y datos de la respuesta.
- `src/Optica.Application/Facturacion/ICandadoEmision.cs` (new) e
  `src/Optica.Infrastructure/Facturacion/CandadoEmision.cs` (new) — una emisión a la vez (`SemaphoreSlim`, singleton).
- `src/Optica.Application/Facturacion/ObtenerEstadoFacturacion.cs` (new) — para un número de presupuesto devuelve
  `EstadoFacturacion` (`Comprobante` o null, `EmisionPendiente`); `PresupuestoGrabado` y `ObtenerPresupuesto` no cambian
  (D-28).
- `src/Optica.Api/Controllers/PresupuestosController.cs` (modified) — `POST {numero}/factura` con
  `[RequestSizeLimit(1024)]`; el `GET {numero}` combina `ObtenerPresupuesto` con `ObtenerEstadoFacturacion` en una
  respuesta de la Api (`PresupuestoConFacturacion`) que suma `factura` y `emisionPendiente`.
- `src/Optica.Api/Errores/RespuestasValidacion.cs` (modified) — `ConflictoFactura(motivo, codigo?, descripcion?)` con
  título fijo "No se pudo emitir la factura.", `detail` fijo por motivo, validación de los 10 motivos de D-18 y D-31, y
  `codigo`/`descripcion` como extensiones solo en `rechazada` (descripción recortada a 500 caracteres);
  `AccionCuerpoIlegibleFactura`.
- `src/Optica.Api/Program.cs` (modified) — `AddScoped<EmitirFactura>()`, `AddScoped<ObtenerEstadoFacturacion>()`, el
  protector del ticket (D-30).
- `tests/Optica.Application.Tests/Facturacion/EmitirFacturaTests.cs` (new), `tests/Optica.Application.Tests/Facturacion/Dobles.cs` (new).
- `tests/Optica.Application.Tests/Facturacion/ObtenerEstadoFacturacionTests.cs` (new).
- `tests/Optica.Api.Tests/Facturacion/FacturaApiTests.cs`, `RendimientoEmisionTests.cs` (new).
- `tests/Optica.Api.Tests/Presupuestos/PresupuestosApiTests.cs`, `VolumenPresupuestosTests.cs` (modified) — claves
  `factura` y `emisionPendiente`.
- `tests/Optica.Api.Tests/Acceso/ProteccionTests.cs`, `SinCacheTests.cs`, `SinDatosArcaTests.cs`,
  `tests/Optica.Api.Tests/Presupuestos/CierrePorDefectoPresupuestosTests.cs`, `tests/Optica.Api.Tests/RegistroTests.cs`
  (modified) — la ruta nueva.

**Logic**
Implementa FR-01 a FR-04, FR-07, FR-12, FR-13, FR-15, FR-17 a FR-25, FR-27 y FR-29 de punta a punta. (1) Valida el
concepto. (2) Transacción 1: lee el presupuesto (404), rechaza Borrador, con factura o con pendiente (FR-02, FR-03,
FR-24); lee la configuración vigente (FR-12, FR-15); arma la solicitud con la fecha local del día (`ReglasEmision`,
FR-07); graba la pendiente y confirma; un conflicto de unicidad en la pendiente o en la factura es `pendiente` o
`facturado`. (3) Un único plazo, `IAutorizadorComprobantes.TiempoMaximo` (30 s, NFR-01), medido desde este paso con un
`CancellationTokenSource` sobre el `TimeProvider`: cubre la espera del candado de emisión y la llamada a ARCA (D-32). Si
después de tomar el candado quedan menos de 10 s, responde `no_enviada` sin llamar a ARCA y quita la pendiente.
El token no se enlaza con el del pedido HTTP: si el navegador corta, la emisión sigue. (4) Transacción 2, con
`CancellationToken.None`: autorizada → graba la factura con sus líneas, el entorno y el número asignado, y quita la
pendiente (FR-18); rechazada o no enviada → quita la pendiente (FR-21, FR-22); sin respuesta → deja la pendiente (FR-23,
FR-24). Si la factura autorizada no se puede grabar (conflicto o cualquier excepción al confirmar), la pendiente queda,
se registra en el log tipo, punto de venta, número y CAE (sin datos del cliente) y responde 409
`pendiente_verificacion` (D-31). Ninguna reintenta por su cuenta. El GET del presupuesto suma `factura` y `emisionPendiente` (FR-20).
No hay `PUT` ni `DELETE` de facturas (FR-25). AC-25 se mide como D-23.

**API contract**
- Method + path: `POST /api/presupuestos/{numero}/factura`
- Request: `concepto` (string: `productos` | `productos_y_servicios`).
- Response: 201 `{ "comprobante": "Factura B 0003-00000125", "tipo": "b", "numero": 125, "cae": string,
  "vencimientoCae": "yyyy-MM-dd", "fecha": "yyyy-MM-dd", "total": number, "neto": number, "iva": number }`, con
  `Location` al presupuesto; `GET /api/presupuestos/{numero}` suma `factura` (`{ "comprobante": string }` | null) y
  `emisionPendiente` (boolean).
- Error codes: 400 concepto inválido o cuerpo ilegible; 401 sin sesión; 404 presupuesto inexistente; 409 con `motivo`
  `borrador`, `facturado`, `pendiente`, `sin_tope`, `sin_punto_venta`, `alicuota_no_admitida`, `rechazada` (con
  `codigo` y `descripcion` de ARCA), `sin_respuesta`, `no_enviada` o `pendiente_verificacion`; 413 cuerpo mayor a 1 KB;
  415 sin JSON.
- Auth: sesión obligatoria (política por defecto); `no-store` en la respuesta.

**Data model**
- Reuses `Factura`, `LineaFactura` y `EmisionPendiente` from Block 4 — no schema change; constraints as declared there:
  índice único `IX_Factura_PresupuestoNumero`, PK `EmisionPendiente.PresupuestoNumero`, `CK_Factura_Importes`, CHECK
  del CAE y del entorno, y los triggers `registro_cerrado`. Reuses `ConfiguracionNegocio` from Block 1 —
  `CK_ConfiguracionNegocio_PuntoVenta`.

**Input validation**
- `numero`: entero de ruta ≥ 1.
- `concepto`: texto obligatorio, exactamente `productos` o `productos_y_servicios`; JSON estricto, cuerpo ≤ 1 KB.

**Error handling**
- Concepto faltante o inválido — 400 en `concepto`; no se lee la base.
- Presupuesto inexistente — 404.
- Borrador, ya facturado o con pendiente — 409 `borrador`, `facturado`, `pendiente`, sin llamar a ARCA (AC-02, AC-03,
  AC-22).
- Sin tope, sin punto de venta o alícuota no admitida — 409 `sin_tope`, `sin_punto_venta`, `alicuota_no_admitida`, sin
  llamar a ARCA (AC-13, AC-32, AC-29).
- Rechazo de ARCA — 409 `rechazada` con código y descripción; sin factura; Facturar sigue habilitado (AC-20).
- Sin respuesta — 409 `sin_respuesta`; la pendiente queda (AC-21).
- No enviada (configuración incompleta, WSAA, otra emisión en curso) — 409 `no_enviada`; sin pendiente.
- Factura autorizada que no se puede grabar — 409 `pendiente_verificacion`; la pendiente queda (D-31).

**Required tests**
- [ ] `Emitir_Final3Lineas_FacturaConLineasYTotal3000` — AC-01.
- [ ] `Emitir_Borrador_Conflicto409SinLlamarArca` — AC-02 (sad path).
- [ ] `Emitir_YaFacturado_Conflicto409SinLlamarArca` — AC-03 (sad path).
- [ ] `Emitir_FechaDelDia30_10_2026` — AC-06 con reloj controlado.
- [ ] `Emitir_TopeCambiadoA40000_UsaElVigente` — AC-12.
- [ ] `Emitir_TopeFaltante_Conflicto409SinLlamarArca` — AC-13 (sad path).
- [ ] `Emitir_AlicuotaCambiadaA10_5_UsaLaVigente` — AC-16.
- [ ] `Emitir_CondicionCambiadaAMonotributo_FacturaC` — AC-05.
- [ ] `Emitir_Autorizada_GuardaFacturaYQuitaPendiente` — AC-18.
- [ ] `ObtenerPresupuesto_ConFactura_MuestraComprobante` — AC-19.
- [ ] `Emitir_Rechazada_SinFacturaYSinPendiente` — AC-20 (rechazo, sad path).
- [ ] `Emitir_SinRespuesta30s_TimeoutDejaPendienteSinReintentar` — AC-21 (timeout, sad path).
- [ ] `Emitir_ConPendiente_Conflicto409SinLlamarArca` — AC-22 (sad path).
- [ ] `PutYDeleteFactura_NoExisten_Rechazado405o404` — AC-23 por la Api (sad path).
- [ ] `Emitir_Alicuota19_Conflicto409SinLlamarArca` — AC-29 (sad path).
- [ ] `Emitir_PuntoVenta3_Numero125_Comprobante` — AC-30.
- [ ] `Emitir_PuntoVentaFaltante_Conflicto409SinLlamarArca` — AC-32 (sad path).
- [ ] `Emitir_ConceptoInvalido_400` — sad path.
- [ ] `Emitir_PresupuestoInexistente_404Error` — sad path.
- [ ] `Emitir_NoEnviada_SinPendienteError` — sad path.
- [ ] `Emitir_OtraEmisionEnCurso_NoEnviadaTimeout` — D-32 (sad path).
- [ ] `Emitir_DosPedidosSimultaneos_UnoConflictoPendiente` — R-09 (sad path).
- [ ] `Emitir_AutorizadaConConflictoAlGrabar_409PendienteVerificacionError` — D-31 (sad path).
- [ ] `Emitir_PedidoHttpCancelado_LaFacturaAutorizadaSeGraba` — WARN-2 de la auditoría.
- [ ] `Emitir_CandadoYArca_UnSoloPlazoDe30s` — el plazo cubre la espera del candado y la llamada (D-32).
- [ ] `Emitir_CandadoConsumioElPlazo_NoEnviadaSinLlamarArca` — quedan menos de 10 s (D-32, sad path).
- [ ] `ObtenerEstadoFacturacion_ConFacturaYSinFactura` — FR-20.
- [ ] `Emitir_20Facturas_DiferenciaMenorA2sEn19` — AC-25.
- [ ] `Emitir_Log_SinDniNiDatosDelCliente` — R-07.
- [ ] `Proteccion_RutaFactura_SinSesion401` y `SinCache_RutaFactura_NoStore` — R-03.

**Completion criterion**
Las pruebas listadas pasan con el simulador y los dobles; `dotnet test Optica.sln` sigue en verde.

## Block 8 — Pantalla de facturación y e2e

**Files**
- `web/src/api/cliente.ts` (modified) y `cliente.test.ts` (modified) — `emitirFactura(numero, concepto)`;
  `leerPresupuesto` lee `factura` y `emisionPendiente`; motivos 409 nuevos.
- `web/src/paginas/Presupuesto.tsx` (modified) y `Presupuesto.test.tsx` (modified) — botón Facturar, diálogo con el
  concepto (Productos / Productos y servicios, sin opción elegida al abrir), estados deshabilitados, texto "Factura B
  0003-00000125" del presupuesto y mensajes.
- `web/src/paginas/EditarPresupuesto.test.tsx`, `NuevoPresupuesto.test.tsx`, `web/src/App.test.tsx` (modified) —
  fixtures con `factura: null` y `emisionPendiente: false`.
- `e2e/servidor.mjs` y `e2e/playwright.config.ts` (modified) — variables `Arca__Entorno=Simulado`,
  `Arca__TiempoMaximoSegundos=2` y `Arca__Simulado__Respuesta` por archivo de prueba.
- `e2e/tests/facturacion.spec.ts`, `facturacion-rechazo.spec.ts`, `facturacion-sin-respuesta.spec.ts` (new).
- `e2e/README.md` (modified) — tabla de proyectos y puertos.
- `docs/instalacion.md` (modified) — claves `Arca:*`, certificado fuera del repositorio, simulado por defecto (R-10).
- `AGENTS.md` (modified) — nombre de la migración de las facturas (D-24).

**Logic**
Implementa la pantalla de FR-01, FR-02, FR-03, FR-08, FR-13, FR-19 a FR-24, FR-27 y FR-29. Facturar se muestra en el
presupuesto, deshabilitado en Borrador, con factura o con emisión pendiente (AC-02, AC-03, AC-22). Abre un diálogo que
pide el concepto; Emitir queda deshabilitado mientras la emisión está en curso. Con 201 muestra "Se emitió la Factura B
0003-00000125." y el presupuesto pasa a mostrar "Factura B 0003-00000125" (AC-18, AC-19, AC-30). Cada 409 muestra su
mensaje fijo: rechazo con "ARCA rechazó la factura: código – descripción" y Facturar habilitado (AC-20); sin respuesta
"ARCA no respondió. La emisión quedó pendiente de verificación." (AC-21); pendiente "La emisión anterior está pendiente
de verificación." (AC-22); sin tope, sin punto de venta o alícuota no admitida, con el enlace a la configuración
(AC-13, AC-32, AC-29).

**Data model**
- Reuses `Factura` and `EmisionPendiente` from Block 4 — no schema change; constraints as declared there: índice unique
  `IX_Factura_PresupuestoNumero`, PK `EmisionPendiente.PresupuestoNumero`, NOT NULL en tipo, punto de venta, número y
  CAE, y los triggers `registro_cerrado`. `AGENTS.md` solo corrige el nombre de la migración (D-24).

**Input validation**
- Concepto: una de dos opciones; sin elegir, Emitir queda deshabilitado.
- Respuesta de la Api: `leerPresupuesto` y la lectura de la factura rechazan formas inesperadas (resultado `general`).

**Error handling**
- 409 `rechazada` — mensaje con código y descripción; Facturar habilitado.
- 409 `sin_respuesta` — mensaje de pendiente; Facturar deshabilitado al recargar.
- 409 `borrador`, `facturado`, `pendiente`, `sin_tope`, `sin_punto_venta`, `alicuota_no_admitida`, `no_enviada`,
  `pendiente_verificacion` — mensaje fijo de cada uno y recarga del presupuesto.
- 401 — vuelve a `/ingreso`; respuesta ilegible o 5xx — mensaje general sin conexión.

**Required tests**
- [ ] `Facturar: Final con concepto Productos emite y muestra el comprobante` — AC-01, AC-18, AC-19.
- [ ] `Facturar: deshabilitado en Borrador, con factura y con pendiente` — AC-02, AC-03, AC-22.
- [ ] `Facturar: 409 rechazada muestra código y descripción y sigue habilitado` — AC-20 (rechazo, sad path).
- [ ] `Facturar: 409 sin_respuesta muestra que ARCA no respondió` — AC-21 (timeout, sad path).
- [ ] `Facturar: 409 sin_tope, sin_punto_venta y alicuota_no_admitida, mensajes con enlace` — AC-13, AC-32, AC-29
  (sad path).
- [ ] `Facturar: 409 no_enviada y respuesta ilegible, error general` — sad path.
- [ ] `Facturar: 409 pendiente_verificacion, mensaje de pendiente y botón deshabilitado` — D-31 (sad path).
- [ ] `Facturar: sin concepto elegido, Emitir deshabilitado` — FR-08 (inválido, sad path).
- [ ] `Facturar: 401 vuelve al ingreso` — no autorizado (sad path).
- [ ] e2e `facturación: emitir Factura B y verla en el presupuesto` — AC-01, AC-18, AC-19, AC-30 en Chrome y Edge.
- [ ] e2e `facturación: Borrador y pedido directo a la Api rechazados` — AC-02, AC-03 (sad path).
- [ ] e2e `facturación rechazo: mensaje de ARCA y Facturar habilitado` — AC-20 (rechazo).
- [ ] e2e `facturación sin respuesta: mensaje y pendiente al volver a facturar` — AC-21, AC-22 (timeout).
- [ ] e2e `facturación: configuración incompleta frena la emisión` — AC-13, AC-32 (sad path).
- [ ] Los e2e de facturación corren en los proyectos de Chrome y Edge del harness — AC-26 (D-03).

**Completion criterion**
`npm run test:coverage`, `npm run lint`, `npm run build`, `npm run test:unidad` y `npm run test:harness` pasan, con los
e2e de facturación en Chrome y Edge.

## Final verification
`dotnet build Optica.sln`, `dotnet test Optica.sln`, `npm run test:coverage`, `npm run lint`, `npm run build` y los e2e
pasan; los 32 AC tienen al menos una prueba (AC-24 queda pendiente de verificación manual con el certificado de
homologación, D-02; AC-26 se cumple con los e2e de Chrome y Edge); la Api arranca en simulado sin claves `Arca`; ninguna
respuesta lleva `certificado`; los triggers rechazan modificar o borrar una factura.
