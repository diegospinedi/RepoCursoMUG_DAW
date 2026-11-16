# Spec FEAT-009: PDF, listado y búsqueda de facturas

| Field | Value |
|-------|-------|
| Ticket | FEAT-009 |
| PRD | docs/ddw/prd/prd-FEAT-009.md |
| Tier | FEATURE |
| Date | 2026-11-16 |
| Spec loops | 2 |
| Loops since last human decision | 2 |

## Summary
Los datos del emisor son un agregado propio (`DatosEmisor`, fila única) con su tabla, su endpoint
`GET/POST /api/configuracion/emisor` y su sección en la pantalla de configuración (ADR-028); el CUIT es un objeto de
valor de dominio (`Cuit`, módulo 11) que también reutiliza `OpcionesArca`. El PDF de la factura sigue el molde del PDF
del presupuesto (FEAT-006, QuestPDF): un caso de uso arma un `DocumentoFactura` con la factura guardada, el cliente del
presupuesto, los datos del emisor y la marca, y un generador lo dibuja con el QR de ARCA (`QrArca` arma la URL;
QRCoder 1.6.0 la dibuja, ADR-027) y la leyenda de comprobante simulado según el entorno. La búsqueda sigue el molde de
la de presupuestos (FEAT-006, ADR-011, ADR-019): `POST /api/facturacion/busqueda` con filtros en el cuerpo, plegado en
Application, `instr(plegar(...))` en el repositorio, 50 por página. La web suma `/facturas` y `/facturas/:numero`.
Decisiones en `docs/ddw/specs/decisions-FEAT-009.md`; amenazas en `docs/ddw/security/threat-FEAT-009.md`.

## Coverage: PRD → blocks
| Requirement | Covered by |
|---|---|
| FR-01 | Block 1, Block 2, Block 3 |
| FR-02 | Block 1, Block 2, Block 3 |
| FR-03 | Block 1, Block 2, Block 3 |
| FR-04 | Block 5, Block 7 |
| FR-05 | Block 5, Block 7 |
| FR-06 | Block 4 |
| FR-07 | Block 4 |
| FR-08 | Block 4, Block 5 |
| FR-09 | Block 4 |
| FR-10 | Block 4 |
| FR-11 | Block 4 |
| FR-12 | Block 4 |
| FR-13 | Block 4, Block 5 |
| FR-14 | Block 4 |
| FR-15 | Block 4 |
| FR-16 | Block 6, Block 7 |
| FR-17 | Block 6, Block 7 |
| FR-18 | Block 6 |
| FR-19 | Block 6 |
| FR-20 | Block 6 |
| FR-21 | Block 6, Block 7 |
| FR-22 | Block 6 |
| FR-23 | Block 6, Block 7 |
| FR-24 | Block 5, Block 7 |
| FR-25 | Block 5, Block 6 |
| NFR-01 | Strategy: e2e del Block 8 en Chrome y Edge estables (un proyecto por navegador y archivo); la versión anterior queda manual (decisión del usuario) |
| NFR-02 | Strategy: los importes del PDF se formatean desde centavos con 2 decimales en el Block 4, probado con $10,02 y $2.710,02 |

## Dependencies between blocks
Block 1 → Block 2 → Block 3. Block 4 depende de Block 1 (tipo `Cuit`). Block 5 depende de Block 1 y Block 4. Block 6 es
independiente de 1 a 5. Block 7 depende de Block 5 y Block 6. Block 8 depende de todos. Orden: 1, 2, 3, 4, 5, 6, 7, 8.

## Block 1 — Dominio y persistencia de los datos del emisor

**Files**
- `src/Optica.Domain/Configuracion/Cuit.cs` (new) — objeto de valor: 11 dígitos con dígito verificador (pesos 5, 4,
  3, 2, 7, 6, 5, 4, 3, 2; módulo 11; resto que da dígito 10 es inválido), `Interpretar(texto)` admite guiones y guarda
  sin ellos, `EsValidoCanonico(texto)` exige 11 dígitos sin guiones.
- `src/Optica.Domain/Configuracion/DatosEmisor.cs` (new) — razón social, domicilio comercial, `Cuit`, Ingresos Brutos y
  fecha de inicio; dos niveles como `ConfiguracionNegocio`: `ParaGrabar(..., DateOnly hoy)` valida todo, incluida la
  fecha no posterior a `hoy` que pasa Application, y devuelve las infracciones por campo; `Leer(...)` reconstruye la
  fila sin la regla de la fecha, para que un reloj atrasado no vuelva corrupta una fila válida (D-19). Clase sin
  `ToString` con valores; no usa tipos de Catálogo.
- `src/Optica.Domain/Configuracion/IDatosEmisorRepository.cs` (new) — `ObtenerAsync()` (null si no hay fila),
  `Reemplazar(DatosEmisor)`.
- `src/Optica.Infrastructure/Persistencia/DatosEmisorRepository.cs` (new) — fila única `Id = 1`; fila corrupta →
  `InvalidOperationException` sin valores.
- `src/Optica.Infrastructure/Persistencia/OpticaDbContext.cs` (modified) — mapeo de `DatosEmisorFila` y CHECK.
- `src/Optica.Infrastructure/Persistencia/Migraciones/20261116120000_DatosEmisor.cs` y `.Designer.cs` (new) —
  `CREATE TABLE` escrito a mano (ADR-012); `Down` con `DROP TABLE`.
- `src/Optica.Infrastructure/Persistencia/Migraciones/OpticaDbContextModelSnapshot.cs` (modified).
- `src/Optica.Infrastructure/ConfiguracionInfraestructura.cs` (modified) — registro de `IDatosEmisorRepository`.
- `src/Optica.Infrastructure/Arca/OpcionesArca.cs` (modified) — `EsCuitValido` y `PesosCuit` se reemplazan por
  `Cuit.EsValidoCanonico` (11 dígitos sin guiones, dígito verificador 10 inválido, como hoy), así `Arca:Cuit` con
  guiones sigue rechazado y nunca llega con guiones a WSAA/WSFEv1 (hallazgo 18 del impact scan, FAIL 1 de la
  auditoría, D-18).
- Pruebas: `tests/Optica.Domain.Tests/Configuracion/CuitTests.cs`, `DatosEmisorTests.cs` (new);
  `tests/Optica.Infrastructure.Tests/Persistencia/DatosEmisorRepositoryTests.cs`,
  `MigracionDatosEmisorTests.cs` (new).

**Logic**
Implementa FR-01, FR-02 y FR-03 en el dominio: los cinco datos son obligatorios y se validan al construir; el CUIT se
guarda sin guiones. La migración crea una tabla nueva y no toca `Factura`, `LineaFactura`, `Presupuesto` ni sus
triggers (R-07).

**Data model**
- Entidad `DatosEmisor` (tabla nueva): `Id` INTEGER PK con CHECK `Id = 1`; `RazonSocial` TEXT NOT NULL CHECK largo 1 a
  120; `DomicilioComercial` TEXT NOT NULL CHECK largo 1 a 200; `Cuit` TEXT NOT NULL CHECK largo 11 y solo dígitos;
  `IngresosBrutos` TEXT NOT NULL CHECK largo 1 a 30; `InicioActividades` TEXT NOT NULL (`yyyy-MM-dd`). Sin índices
  extra (fila única).
- Rollback: el `Down` de la migración hace `DROP TABLE DatosEmisor` y deja el esquema de FEAT-008; los datos del emisor
  cargados se pierden (se vuelven a cargar en la configuración). Probado por `Migracion_Down_Revierte`.

**Input validation**
- Razón social: texto recortado, 1 a 120 caracteres, sin caracteres de control.
- Domicilio comercial: texto recortado, 1 a 200, sin caracteres de control.
- CUIT: 11 dígitos con dígito verificador válido; se admiten guiones en cualquier posición; se rechaza cualquier otro
  carácter.
- Ingresos Brutos: texto recortado, 1 a 30, sin caracteres de control.
- Fecha de inicio de actividades: fecha válida, no posterior a la fecha `hoy` que recibe `ParaGrabar` (Application la
  toma de `TimeProvider`); al leer no se aplica.

**Error handling**
- Dato faltante o vacío — infracción por campo; `DatosEmisor.Crear` no construye la entidad.
- CUIT inválido (largo, dígito verificador o caracteres) — infracción del campo CUIT.
- Fecha de inicio posterior a hoy — infracción del campo fecha de inicio.
- Fila corrupta en la base — `InvalidOperationException` con texto fijo, sin valores.

**Required tests**
- [ ] Cuit_ConGuiones_SeGuardaSinGuiones — validates AC-03
- [ ] Cuit_DigitoVerificadorInvalido_Rechazado — "20-12345678-5", validates AC-04 (sad path)
- [ ] Cuit_LargoInvalido_Rechazado — "2012345678", validates AC-04 (sad path)
- [ ] DatosEmisor_SinRazonSocial_Rechazado — faltante, validates AC-02 (sad path)
- [ ] DatosEmisor_InicioFuturo_Rechazado — invalid (sad path)
- [ ] DatosEmisor_TextoLargo_Rechazado — más de 120 caracteres, invalid (sad path)
- [ ] Repositorio_GrabaYLeeLosCincoDatos — validates AC-01
- [ ] Repositorio_SinFila_DevuelveNull
- [ ] Repositorio_FilaCorrupta_ErrorSinValores — error (sad path)
- [ ] Migracion_DatosEmisor_ConservaTodosLosTriggers — el conjunto completo de triggers de `sqlite_master` (cierre de
  presupuestos, facturas, líneas, `TR_EmisionPendiente_Inmutable`, observación y descarte) es igual antes y después del
  `Up` y después del `Down`
- [ ] Migracion_Down_Revierte
- [ ] OpcionesArca_CuitInvalido_SigueRechazado — la validación de `Arca:Cuit` usa `Cuit` (sad path)
- [ ] OpcionesArca_CuitConGuiones_SigueRechazado — rejected (sad path)
- [ ] DatosEmisor_LeerConInicioFuturo_NoEsCorrupta — un reloj atrasado no rompe la lectura

**Completion criterion**
Las 14 pruebas pasan, `dotnet build` sin advertencias, y la migración aplicada sobre una base de FEAT-008 crea
`DatosEmisor` sin cambiar los triggers de facturación.

## Block 2 — Casos de uso y Api de los datos del emisor

**Files**
- `src/Optica.Application/Configuracion/ObtenerDatosEmisor.cs` (new) — devuelve los cinco datos o vacío.
- `src/Optica.Application/Configuracion/GrabarDatosEmisor.cs` (new) — dentro de
  `IUnidadDeTrabajo.EjecutarEnTransaccionAsync` (como `GrabarConfiguracionConRecalculo`, ADR-013): lee los datos
  vigentes, valida con `DatosEmisor.ParaGrabar(..., hoy)`, reemplaza y registra en el log la fecha y hora UTC y los
  nombres de los campos que cambiaron, nunca sus valores (la razón social y el CUIT pueden ser de una persona física,
  R-06). `DatosEmisorVigentes` y la solicitud del controller con `PrintMembers => false`.
- `src/Optica.Application/Configuracion/ValidacionConfiguracion.cs` (modified) — campos y acciones de los datos del
  emisor.
- `src/Optica.Api/Controllers/ConfiguracionController.cs` (modified) — `GET` y `POST /api/configuracion/emisor`.
- `src/Optica.Api/Errores/RespuestasValidacion.cs` (modified) — acción de cuerpo ilegible del emisor.
- `src/Optica.Api/Program.cs` (modified) — registro de los dos casos de uso.
- Pruebas: `tests/Optica.Application.Tests/Configuracion/GrabarDatosEmisorTests.cs` (new),
  `tests/Optica.Application.Tests/Configuracion/Dobles.cs` (modified, doble de `IDatosEmisorRepository`),
  `tests/Optica.Api.Tests/Configuracion/DatosEmisorApiTests.cs` (new), `tests/Optica.Api.Tests/Acceso/ProteccionTests.cs`,
  `SinCacheTests.cs`, `SinDatosArcaTests.cs` (modified).

**Logic**
Implementa FR-01, FR-02 y FR-03 por la Api: lectura de los datos vigentes y grabación de los cinco juntos, con todos
los errores por campo.

**API contract**
- Method + path: `GET /api/configuracion/emisor` y `POST /api/configuracion/emisor`.
- Request (POST): `razonSocial` (string), `domicilioComercial` (string), `cuit` (string), `ingresosBrutos` (string),
  `inicioActividades` (string `yyyy-MM-dd`); JSON estricto, `Content-Type: application/json`, cuerpo hasta 2 KB (con
  caracteres de 3 bytes una carga válida pasa 1 KB, D-21).
- Response: `GET` 200 con los cinco campos (null cada uno si nunca se cargaron); `POST` 200 con los cinco campos
  grabados (CUIT sin guiones).
- Error codes: 400 con `errors` por campo; 401 sin sesión; 413 cuerpo mayor a 2 KB; 415 sin JSON; 500 genérico.
- Auth: sesión con cookie de FEAT-001a (política por defecto, fuera de `RutasAnonimas`).

**Data model**
- Reuses `DatosEmisor` from Block 1 — no schema change; constraints as declared there: `Id = 1`, NOT NULL en los cinco
  campos, CHECK de largo y de CUIT de 11 dígitos.

**Input validation**
- Las mismas reglas del Block 1 por campo; además JSON con los cinco campos de tipo string (un número en lugar de texto
  es cuerpo ilegible).

**Error handling**
- Campo faltante, vacío o inválido — 400 con un error por cada campo afectado; no se graba nada.
- Cuerpo ilegible o con tipos equivocados — 400 con un error en el campo del cuerpo; no se graba nada.
- Cuerpo mayor a 2 KB — 413.
- Sin sesión — 401.

**Required tests**
- [ ] Grabar_CincoDatos_ReemplazaYRegistraSinValores — validates AC-01
- [ ] Grabar_SinRazonSocial_RechazadoSinGrabar — faltante, validates AC-02 (sad path)
- [ ] PostEmisor_CuitConGuiones_200SinGuiones — validates AC-03
- [ ] PostEmisor_CuitInvalido_400JuntoAlCampo — invalid, validates AC-04 (sad path)
- [ ] PostEmisor_CuerpoIlegible_400 — invalid (sad path)
- [ ] PostEmisor_CuerpoGrande_413 — rejected (sad path)
- [ ] RutasEmisor_SinSesion_401 — unauthorized (sad path)
- [ ] GetEmisor_SinDatos_200ConNulls — validates AC-05 (estado previo)
- [ ] RutasEmisor_NoStore

**Completion criterion**
Las 9 pruebas pasan, `SinDatosArcaTests` y `ProteccionTests` siguen en verde con las rutas nuevas y el log de la
grabación no contiene ninguno de los cinco valores.

## Block 3 — Pantalla de los datos del emisor

**Files**
- `web/src/api/cliente.ts` (modified) — `consultarDatosEmisor`, `grabarDatosEmisor` y lectores estrictos.
- `web/src/configuracion/reglas.ts` (modified) — validación espejo de los cinco campos y del CUIT.
- `web/src/paginas/Configuracion.tsx` (modified) — sección "Datos del emisor" con su propio Grabar y errores junto al
  campo; "sin cargar" es el 200 con nulls del contrato (sin tolerar 404, D-22); el `servidorFalso` de las pruebas
  existentes suma la ruta (hallazgo 13).
- Pruebas: `web/src/paginas/Configuracion.test.tsx`, `web/src/api/cliente.test.ts`,
  `web/src/configuracion/reglas.test.ts`, `web/src/App.test.tsx` (modified).

**Logic**
Implementa FR-01 a FR-03 en la pantalla: carga, muestra y graba los cinco datos; los errores del servidor se muestran
junto a su campo.

**Input validation**
- Los cinco campos obligatorios; CUIT con dígito verificador (espejo del servidor); fecha con `type="date"`.

**Error handling**
- Error de validación del servidor — se muestra junto a cada campo; los valores cargados quedan en la pantalla.
- Fallo de red o 500 — mensaje general sin datos; nada se da por grabado.

**Required tests**
- [ ] Emisor_GrabaCincoDatos_LosMuestraAlVolver — validates AC-01
- [ ] Emisor_SinRazonSocial_ErrorJuntoAlCampo — faltante, validates AC-02 (sad path)
- [ ] Emisor_CuitInvalido_ErrorJuntoAlCampo — invalid, validates AC-04 (sad path)
- [ ] Emisor_ErrorDelServidor_MensajeGeneral — error (sad path)
- [ ] Reglas_Cuit_DigitoVerificador — validates AC-03

**Completion criterion**
Las 5 pruebas pasan, `npm run lint`, `npm run build` y `npm run test:coverage` en verde, y las pruebas existentes de
Configuración siguen pasando.

## Block 4 — QR de ARCA y generador del PDF

**Files**
- `src/Optica.Domain/Facturacion/ReglasComprobanteImpreso.cs` (new) — reglas de negocio del comprobante impreso, para
  que valgan desde cualquier interfaz: condición frente al IVA del emisor por tipo (B "Responsable Inscripto", C
  "Responsable Monotributo") y `TieneValidezFiscal(EntornoArca)` (solo Produccion).
- `src/Optica.Application/Facturacion/QrArca.cs` (new) — arma la URL `https://www.afip.gob.ar/fe/qr/?p=` + base64 del
  JSON (`ver` 1, `fecha`, `cuit`, `ptoVta`, `tipoCmp` 6 o 11, `nroCmp`, `importe`, `moneda` "PES", `ctz` 1,
  `tipoDocRec` 96 o 99, `nroDocRec` DNI o 0, `tipoCodAut` "E", `codAut` CAE) con `System.Text.Json`. El DNI sale de
  `Factura.Receptor` (lo que autorizó ARCA). Fuera de Produccion el QR lleva `tipoDocRec` 99 y `nroDocRec` 0 aunque
  la factura tenga DNI: el comprobante no existe en ARCA y escanearlo no debe mandar el DNI a `www.afip.gob.ar`
  (D-23). Los códigos 6, 11, 96, 99, "PES" y 1 duplican los de `Infrastructure/Arca/CodigosWsfe.cs`; una prueba de
  Infrastructure.Tests compara ambos (ADR-027, D-24).
- `src/Optica.Application/Facturacion/DocumentoFactura.cs` (new) — lo que se dibuja: emisor, condición frente al IVA
  (de `ReglasComprobanteImpreso`), comprobante, fecha, receptor ("Consumidor Final" o DNI de `Factura.Receptor` con
  Apellido y Nombre del cliente del presupuesto), líneas, total, IVA contenido (solo B), CAE, vencimiento, URL del QR,
  si lleva la leyenda de comprobante simulado (`!TieneValidezFiscal`), identidad visual. Clase sin `ToString` con
  datos (DNI, nombre y URL del QR).
- `src/Optica.Infrastructure/Pdf/ConfiguracionQuestPdf.cs` (new) — la configuración global de QuestPDF (licencia
  Community, sin fuentes del sistema) en un solo lugar; `src/Optica.Infrastructure/Pdf/GeneradorPdfPresupuesto.cs`
  (modified) la usa en lugar de fijarla por su cuenta.
- `src/Optica.Application/Facturacion/IGeneradorPdfFactura.cs` (new) — puerto `byte[] Generar(DocumentoFactura)`.
- `src/Optica.Infrastructure/Pdf/GeneradorPdfFactura.cs` (new) — QuestPDF (por `ConfiguracionQuestPdf`), QR con
  `PngByteQRCode` de QRCoder, leyenda "COMPROBANTE SIMULADO — SIN VALIDEZ FISCAL" en el encabezado de cada página
  cuando corresponde, leyenda Ley 27.743 en la Factura B. Las fallas de QuestPDF y QRCoder se relanzan como
  `InvalidOperationException` con texto fijo y sin la causa, como `GeneradorPdfPresupuesto` (D-25).
- `src/Optica.Infrastructure/Optica.Infrastructure.csproj` (modified) — `QRCoder` 1.6.0.
- `src/Optica.Infrastructure/packages.lock.json`, `src/Optica.Api/packages.lock.json`,
  `tests/Optica.Api.Tests/packages.lock.json`, `tests/Optica.Infrastructure.Tests/packages.lock.json` (modified,
  hallazgo 1).
- `src/Optica.Infrastructure/ConfiguracionInfraestructura.cs` (modified) — registro singleton de `IGeneradorPdfFactura`.
- `docs/adr/adr-027-qrcoder-para-el-qr-de-arca.md` (new).
- `e2e/ci.mjs` y `e2e/ci.test.mjs` (modified) — `['www.afip.gob.ar', new Set(['/fe/qr/'])]` en `RUTAS_ARCA` (es dato,
  no tráfico; hallazgo 9); el comentario distingue la URL de verificación del QR de los hosts de emisión, donde
  producción sigue sin agregarse.
- Pruebas: `tests/Optica.Application.Tests/Facturacion/QrArcaTests.cs` (new),
  `tests/Optica.Infrastructure.Tests/Pdf/GeneradorPdfFacturaTests.cs` (new, PdfPig),
  `tests/Optica.Api.Tests/Acceso/ProteccionTests.cs` (modified, `Api_NoReferenciaQrCoder`).

**Logic**
Implementa FR-06 a FR-15: el contenido del PDF y el QR. Los importes se formatean desde centavos con 2 decimales
(NFR-02). La leyenda de comprobante simulado depende del entorno guardado en la factura (Simulado u Homologacion sí,
Produccion no).

**Data model**
- Reuses `Factura` and `LineaFactura` from FEAT-007 — no schema change; constraints as declared there: NOT NULL en
  comprobante, importes en centavos, CAE de 14 dígitos, entorno, índice único `PresupuestoNumero`, triggers
  `TR_Factura_Inmutable` y `TR_LineaFactura_Inmutable`.

**Input validation**
- `DocumentoFactura` rechaza al construir: textos vacíos del emisor, CAE que no sea de 14 dígitos, total negativo, tipo
  que no sea B ni C.

**Error handling**
- Documento inválido (dato faltante o CAE inválido) — `ArgumentException` con el nombre del campo, sin valores; el caso
  de uso del Block 5 responde 500 genérico.
- Logo que QuestPDF no puede dibujar — se dibuja sin logo, como en `GeneradorPdfPresupuesto`.
- Falla interna de QuestPDF o QRCoder — `InvalidOperationException` con texto fijo, sin la causa.

**Required tests**
- [ ] QrArca_FacturaBConDniEnProduccion_UrlYJsonDeArca — decodifica `p` y compara los 13 campos, validates AC-12
- [ ] QrArca_ConsumidorFinal_TipoDoc99SinDni — validates AC-09
- [ ] QrArca_FueraDeProduccion_SinDni — homologación con DNI lleva 99 y 0 (D-23)
- [ ] QrArca_HostYRutaConstantes — validates AC-12
- [ ] QrArca_CodigosCoincidenConCodigosWsfe — en Infrastructure.Tests (D-24)
- [ ] ReglasComprobanteImpreso_CondicionYValidezFiscal — validates AC-07, AC-27
- [ ] Pdf_FallaInterna_ErrorConTextoFijo — error (sad path)
- [ ] Documento_CaeInvalido_Rechazado — invalid (sad path)
- [ ] Documento_EmisorFaltante_Rechazado — faltante (sad path)
- [ ] Pdf_LogoYColores — validates AC-06
- [ ] Pdf_EmisorYCondicionResponsableInscripto — validates AC-07
- [ ] Pdf_FacturaCCondicionMonotributo — validates AC-27
- [ ] Pdf_ComprobanteYFecha_B000300000125 — validates AC-08
- [ ] Pdf_ConsumidorFinalSinDni — validates AC-09
- [ ] Pdf_ReceptorConDniApellidoYNombre — validates AC-10
- [ ] Pdf_LineasYTotal_271002 — validates AC-11
- [ ] Pdf_CaeVencimientoEImagenQr — validates AC-12
- [ ] Pdf_FacturaB_LeyendaLey27743Con31500 — validates AC-13
- [ ] Pdf_FacturaC_SinIvaContenido — validates AC-14
- [ ] Pdf_SimuladoYHomologacion_LeyendaEnCadaPagina — dos páginas, validates AC-15
- [ ] Pdf_Produccion_SinLeyenda — validates AC-16
- [ ] Pdf_LogoInvalido_SeGeneraSinLogo — error (sad path)
- [ ] Api_NoReferenciaQrCoder

**Completion criterion**
Las 23 pruebas pasan, las de `GeneradorPdfPresupuesto` siguen en verde con `ConfiguracionQuestPdf`, `dotnet build` sin
advertencias con la restauración en modo bloqueado, y
`npm run test:unidad` de e2e en verde con la ruta del QR permitida.

## Block 5 — Detalle y PDF de la factura

**Files**
- `src/Optica.Application/Facturacion/ObtenerFactura.cs` (new) — detalle: factura (por
  `IFacturaRepository.ObtenerPorPresupuestoAsync`, sin cambiar la interfaz) más Apellido, Nombre y DNI del cliente del
  presupuesto (FR-25); el receptor del detalle sale de `Factura.Receptor`. Sin registrador; el resultado
  `FacturaDetalle` con `PrintMembers => false`.
- `src/Optica.Application/Facturacion/GenerarPdfFactura.cs` (new) — factura + cliente + `IDatosEmisorRepository` +
  `IProveedorMarca` → `DocumentoFactura` → `IGeneradorPdfFactura`; resultado `NoEncontrado`, `SinDatosEmisor` o
  `Generado` con el nombre de archivo `factura-B-0003-00000125.pdf`. Sin registrador, como `GenerarPdfPresupuesto`.
  Los datos del emisor son los vigentes al generar (Out of Scope del PRD; consecuencia registrada en ADR-028).
- `src/Optica.Api/Controllers/PresupuestosController.cs` (modified) — `GET {numero:long}/factura` y
  `GET {numero:long}/factura/pdf`.
- `src/Optica.Api/Errores/RespuestasValidacion.cs` (modified) — 409 `sin_datos_emisor` con título fijo, por el helper de conjunto cerrado de motivos como `ConflictoPresupuesto` (D-26).
- `src/Optica.Api/Program.cs` (modified) — registro de los dos casos de uso.
- Pruebas: `tests/Optica.Application.Tests/Facturacion/ObtenerFacturaTests.cs`, `GenerarPdfFacturaTests.cs` (new),
  `tests/Optica.Api.Tests/Facturacion/FacturaApiTests.cs` (modified o new según exista),
  `tests/Optica.Api.Tests/Presupuestos/CierrePorDefectoPresupuestosTests.cs` (modified, constructor con 12 argumentos),
  `SinDatosArcaTests.cs`, `SinCacheTests.cs` (modified).

**Logic**
Implementa FR-04, FR-05, FR-24 y FR-25 en la Api: el detalle de la factura de un presupuesto (sin acciones de edición)
y la descarga del PDF; sin los cinco datos del emisor no se genera el PDF.

**API contract**
- Method + path: `GET /api/presupuestos/{numero:long}/factura` y `GET /api/presupuestos/{numero:long}/factura/pdf`.
- Request: `numero` (long) en la ruta; sin cuerpo.
- Response: detalle 200 `{ comprobante: "Factura B 0003-00000125", fecha: "2026-10-30", presupuesto: 155, apellido,
  nombre, dni, receptor: "Consumidor Final" | "DNI 00123456", lineas: [{ descripcion, cantidad, importe }], total,
  ivaContenido (null en C), cae, vencimientoCae, simulada: bool }`; PDF 200 `application/pdf` con
  `Content-Disposition: attachment; filename=factura-B-0003-00000125.pdf`.
- Error codes: 404 presupuesto inexistente o sin factura; 409 `sin_datos_emisor` (solo PDF); 401 sin sesión; 500
  genérico sin el texto de la excepción.
- Auth: sesión con cookie de FEAT-001a.

**Data model**
- Reuses `Factura`, `LineaFactura` (FEAT-007), `Presupuesto` (FEAT-003) and `DatosEmisor` from Block 1 — no schema
  change; constraints as declared there: NOT NULL en comprobante, importes, CAE y entorno; índice único
  `PresupuestoNumero`; triggers de inmutabilidad; `DatosEmisor.Id = 1`.

**Input validation**
- `numero`: entero `long` por la restricción de ruta (otro texto → 404 de ruta).

**Error handling**
- Presupuesto inexistente o sin factura — 404 con título fijo.
- Faltan datos del emisor — 409 `sin_datos_emisor` con título y detalle fijos que piden completarlos en la
  configuración.
- Error al generar — 500 genérico sin el texto de la excepción.
- Sin sesión — 401.

**Required tests**
- [ ] ObtenerFactura_ConsumidorFinal_ClienteDelPresupuesto — validates AC-24, AC-26
- [ ] GenerarPdf_SinRazonSocial_SinDatosEmisor — faltante, validates AC-05 (sad path)
- [ ] GenerarPdf_Inexistente_NoEncontrado — invalid (sad path)
- [ ] GetFacturaPdf_Descarga_NombreSinDatos — validates AC-06
- [ ] GetFacturaPdf_SinDatosEmisor_409 — conflict, validates AC-05 (sad path)
- [ ] GetFactura_Detalle_Presupuesto155SinAcciones — validates AC-24
- [ ] GetFactura_SinFactura_404 — rejected (sad path)
- [ ] GetFacturaPdf_GeneradorLanza_500SinTexto — error (sad path)
- [ ] RutasFactura_SinSesion_401 — unauthorized (sad path)
- [ ] RutasFactura_NoStore

**Completion criterion**
Las 10 pruebas pasan y `SinDatosArcaTests` sigue en verde con las dos rutas nuevas y el segundo PDF.

## Block 6 — Búsqueda de facturas

**Files**
- `src/Optica.Domain/Presupuestos/PlegadoCliente.cs` (new) — pliega Apellido y Nombre del cliente delegando en
  `PlegadoBusqueda`, como ya hace `FiltroPresupuestos`; así el plegado le llega a Facturación a través de Presupuestos
  (regla de `DependenciasDeModulosTests`, hallazgo 14, WARN 3 de la auditoría, D-13).
- `src/Optica.Domain/Facturacion/FiltroFacturas.cs` (new) — pliega Apellido y Nombre con `PlegadoCliente`, quita
  puntos, guiones y espacios de DNI y número, y `Crear` rechaza Desde posterior a Hasta con
  `FiltroFacturasInvalidoException` (propia de Facturación, para no chocar con la de Presupuestos en la Api). Clase sin
  `ToString` con datos.
- `src/Optica.Domain/Facturacion/FiltroFacturasInvalidoException.cs` (new).
- `src/Optica.Domain/Facturacion/ResumenFactura.cs` y `PaginaFacturas.cs` (new) — clases sin `ToString` con datos.
- `src/Optica.Infrastructure/Persistencia/FuncionesSqlite.cs` (modified) — función `digitos_comprobante(pv, numero)`
  registrada al abrir la conexión (ADR-011, fuera de índices, CHECK y triggers) que devuelve los dígitos del texto
  visible: punto de venta con mínimo 4 dígitos (`D4`, 5 si es 10000 o más) más el número con 8; mapeada para EF Core
  (WARN 9, D-27).
- `src/Optica.Domain/Facturacion/IFacturaRepository.cs` (modified) — `BuscarAsync(FiltroFacturas, pagina, tamano)`.
- `src/Optica.Infrastructure/Persistencia/FacturaRepository.cs` (modified) — join `Factura`–`Presupuesto`,
  `instr(plegar(...))`, número con `instr(digitos_comprobante(...))`, orden `Fecha` desc, `Numero` desc y `Id` desc
  como desempate total (el número solo es único por entorno, tipo y punto de venta; FAIL 2 de la auditoría, D-20),
  `Skip`/`Take` y `Count`. Recorrido completo aceptado como en ADR-011 (una PC, sin índice sobre `plegar`).
- `src/Optica.Application/Facturacion/BuscarFacturas.cs` (new) — arma el filtro y pide la página de 50; sin
  registrador; el resultado con `PrintMembers => false`.
- `src/Optica.Api/Controllers/FacturacionController.cs` (modified) — `POST /api/facturacion/busqueda` junto a la
  lista de pendientes existente (WARN 13, D-28); `SolicitudBusquedaFacturas` con `PrintMembers => false`; valida en el
  borde fechas estrictas, página y largo de los textos, como la búsqueda de presupuestos.
- `src/Optica.Api/Errores/RespuestasValidacion.cs` (modified) — acción de cuerpo ilegible de la búsqueda.
- `src/Optica.Api/Program.cs` (modified) — registro.
- `tests/Optica.Application.Tests/Facturacion/Dobles.cs` y `EmitirFacturaTests.cs` (modified, `BuscarAsync` en
  `AlmacenFacturacion` y `AlmacenFacturacionConCarrera`).
- `tests/Compartido/SiembraFacturas.cs` (new) y su enlace en `tests/Optica.Api.Tests/Optica.Api.Tests.csproj` y
  `tests/Optica.Infrastructure.Tests/Optica.Infrastructure.Tests.csproj` (modified) — inserta facturas por la
  aplicación, sin tocar triggers.
- Pruebas: `tests/Optica.Domain.Tests/Facturacion/FiltroFacturasTests.cs`,
  `tests/Optica.Application.Tests/Facturacion/BuscarFacturasTests.cs`,
  `tests/Optica.Infrastructure.Tests/Persistencia/BusquedaFacturasTests.cs`,
  `tests/Optica.Api.Tests/Facturacion/BusquedaFacturasApiTests.cs` (new); `ProteccionTests.cs`, `SinCacheTests.cs`,
  `SinDatosArcaTests.cs` (modified).

**Logic**
Implementa FR-16 a FR-23 y FR-25: búsqueda combinada con "Y", Apellido y Nombre parciales sin mayúsculas ni acentos,
DNI y número parciales sin puntos ni guiones, rango de fechas inclusivo, sin filtros lista todo, 50 por página de la más
reciente a la más antigua, y cada resultado con Apellido, Nombre y DNI del cliente del presupuesto.

**API contract**
- Method + path: `POST /api/facturacion/busqueda`.
- Request: `apellido`, `nombre`, `dni`, `numero` (string, opcionales, hasta 100), `desde`, `hasta` (string
  `yyyy-MM-dd`, opcionales), `pagina` (int ≥ 1); JSON estricto, cuerpo hasta 4 KB.
- Response: 200 `{ facturas: [{ presupuesto, comprobante: "Factura B 0003-00000125", fecha, apellido, nombre, dni,
  total }], pagina, tamanoPagina: 50, total }`.
- Error codes: 400 con `errors` (Desde posterior a Hasta junto a `desde`; fecha o página inválidas); 401 sin sesión;
  413 cuerpo mayor a 4 KB; 415 sin JSON.
- Auth: sesión con cookie de FEAT-001a.

**Data model**
- Reuses `Factura` (FEAT-007) and `Presupuesto` (FEAT-003) — no schema change; constraints as declared there: NOT NULL
  en comprobante, fecha y presupuesto; índice único `PresupuestoNumero`; DNI del cliente guardado ya normalizado.

**Input validation**
- Textos: hasta 100 caracteres; DNI y número: se quitan puntos, guiones y espacios; fechas `yyyy-MM-dd` estrictas;
  `pagina` entero ≥ 1.

**Error handling**
- Desde posterior a Hasta — 400 con el error junto a `desde`; no se busca.
- Fecha o página inválidas — 400 por campo.
- Cuerpo ilegible — 400; cuerpo grande — 413.
- Sin sesión — 401.

**Required tests**
- [ ] Filtro_DesdePosteriorAHasta_Rechazado — invalid, validates AC-22 (sad path)
- [ ] Filtro_DniYNumero_SinPuntosNiGuiones — validates AC-19, AC-20
- [ ] Repositorio_120Facturas_Paginas50y20 — validates AC-17
- [ ] Repositorio_EmpatesDeFechaYNumero_SinRepetirNiPerderEntrePaginas — validates AC-17
- [ ] Repositorio_PuntoDeVentaDeCincoDigitos_PorNumero — validates AC-19
- [ ] Repositorio_ApellidoYDesde_SoloGonzalez20 — validates AC-18
- [ ] Repositorio_Numero34561 — validates AC-19
- [ ] Repositorio_Dni3456y00345 — validates AC-20
- [ ] Repositorio_NombreMaria_SoloMariaJose — validates AC-21
- [ ] Repositorio_ConsumidorFinal_PorDniDelCliente — validates AC-26
- [ ] PostBusqueda_Resultado_ColumnasDeAC23 — validates AC-23
- [ ] PostBusqueda_DesdePosterior_400JuntoADesde — invalid (sad path)
- [ ] PostBusqueda_CuerpoIlegible_400 — invalid (sad path)
- [ ] PostBusqueda_CuerpoGrande_413 — rejected (sad path)
- [ ] PostBusqueda_SinSesion_401 — unauthorized (sad path)
- [ ] PostBusqueda_Registro_SinDatosPersonales — valores testigo fuera del log (R-01)

**Completion criterion**
Las 16 pruebas pasan, `DependenciasDeModulosTests` sigue en verde y el dominio de Facturación no usa tipos de Catálogo.

## Block 7 — Pantallas de facturas

**Files**
- `web/src/api/cliente.ts` (modified) — `buscarFacturas`, `consultarFactura`, `descargarPdfFactura` con lectores
  estrictos; 409 `sin_datos_emisor` como resultado propio.
- `web/src/paginas/BuscarFacturas.tsx` (new) — filtros (Apellido, Nombre, DNI, número, Desde, Hasta) en `useState`,
  resultados con Comprobante, Fecha, Apellido, Nombre, DNI, Total y Presupuesto, paginado; sin el texto "punto de
  venta" (hallazgo 8).
- `web/src/paginas/DetalleFactura.tsx` (new) — datos de la factura, enlace al presupuesto, Descargar PDF; aviso con
  enlace a la configuración ante `sin_datos_emisor`; sin acciones de edición ni de eliminación.
- `web/src/App.tsx` (modified) — rutas `/facturas` y `/facturas/:numero`, donde `:numero` es el número del
  presupuesto de origen (único por factura, D-14, D-28).
- `web/src/paginas/Inicio.tsx` (modified) — enlace "Facturas".
- `web/src/paginas/Presupuesto.tsx` (modified) — la fila "Factura" enlaza a `/facturas/:numero`.
- Pruebas: `web/src/paginas/BuscarFacturas.test.tsx`, `DetalleFactura.test.tsx` (new); `web/src/App.test.tsx`
  (`PANTALLAS`), `Inicio.test.tsx`, `Presupuesto.test.tsx`, `web/src/api/cliente.test.ts` (modified).

**Logic**
Implementa FR-04, FR-05, FR-16, FR-17, FR-21, FR-23 y FR-24 en la web.

**Input validation**
- Desde y Hasta con `type="date"`; Desde posterior a Hasta se rechaza en la pantalla junto a Desde (espejo del
  servidor).

**Error handling**
- Desde posterior a Hasta — error junto a Desde; no se pide la búsqueda.
- 409 `sin_datos_emisor` — aviso con enlace a Configuración; no se descarga nada.
- Respuesta ilegible o 500 — mensaje general sin datos.

**Required tests**
- [ ] Buscar_Resultado_ColumnasDeAC23 — validates AC-23
- [ ] Buscar_DesdePosterior_ErrorSinPedido — invalid, validates AC-22 (sad path)
- [ ] Buscar_FiltrosFueraDeLaUrl
- [ ] Detalle_Presupuesto155_SinAccionesDeEdicion — validates AC-24
- [ ] Detalle_DescargarPdf — validates AC-06
- [ ] Detalle_SinDatosEmisor_AvisoConEnlace — conflict, validates AC-05 (sad path)
- [ ] Detalle_RespuestaIlegible_MensajeGeneral — error (sad path)
- [ ] Pantallas_NingunaMuestraDatosArca_IncluyeFacturas

**Completion criterion**
Las 8 pruebas pasan y `npm run lint`, `npm run build` y `npm run test:coverage` siguen en verde.

## Block 8 — e2e

**Files**
- `e2e/tests/configuracion-emisor.spec.ts` (new) — AC-01 a AC-04.
- `e2e/tests/facturas-pdf.spec.ts` (new) — AC-05 a AC-16 y AC-27 con `pdfjs-dist` (texto, leyenda en cada página).
- `e2e/tests/facturas-busqueda.spec.ts` (new) — AC-17 a AC-24 y AC-26 (siembra por la Api).
- `e2e/compilacion.mjs` y `e2e/compilacion.test.mjs` (modified) — `'facturas-pdf': { simulado: { contadores:
  { 'B-3': 124 } } }` para la Factura B 0003-00000125 (hallazgo 10).
- `docs/instalacion.md` (modified) — datos del emisor y que el CUIT sea el mismo que `Arca:Cuit` (R-10).

**Logic**
Implementa NFR-01: los criterios de FR-01 a FR-25 de punta a punta en Chrome y Edge estables, con una Api y una base
por navegador y archivo.

**Input validation**
- `ARCA_POR_ARCHIVO` rechaza claves de archivo inexistentes y contadores no enteros (validación existente del harness).

**Error handling**
- Configuración inválida del harness — `compilacion.mjs` falla antes de levantar la Api, como hoy.

**Required tests**
- [ ] configuracion-emisor: graba, CUIT con guiones y CUIT inválido — validates AC-01, AC-02, AC-03, AC-04
- [ ] facturas-pdf: sin datos del emisor y PDF completo — validates AC-05 a AC-16, AC-27
- [ ] facturas-busqueda: paginado, filtros, detalle y Consumidor Final — validates AC-17 a AC-24, AC-26
- [ ] compilacion: contador inválido para facturas-pdf rechazado — invalid (sad path)
- [ ] los tres archivos en los proyectos `chrome-*` y `msedge-*` — validates AC-25

**Completion criterion**
Los tres archivos pasan en `chrome-*` y `msedge-*` con `npm run test:harness`, y `npm run test:unidad` en verde.

## Final verification
`dotnet build` y `dotnet test Optica.sln`, `npm run lint`, `npm run test:lint`, `npm run test:coverage` y
`npm run build` en `web/`, `npm run test:unidad` y `npm run test:harness` en `e2e/` en verde; los triggers de `Factura`
y `LineaFactura` siguen en la base migrada; ningún log contiene los filtros de la búsqueda ni los datos del emisor; el
escaneo del QR con un teléfono queda como verificación manual del usuario (decisión del agente, `decisions-FEAT-009.md`).
