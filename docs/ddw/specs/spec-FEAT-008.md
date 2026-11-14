# Spec FEAT-008: Recuperación de emisiones sin respuesta de ARCA

| Field | Value |
|-------|-------|
| Ticket | FEAT-008 |
| PRD | docs/ddw/prd/prd-FEAT-008.md |
| Tier | FEATURE |
| Date | 2026-11-14 |
| Spec loops | 1 |
| Loops since last human decision | 1 |

## Summary
La emisión de FEAT-007 se reordena para registrar el número antes de enviarlo: con el candado tomado, la aplicación
pide a ARCA el próximo número, graba la emisión pendiente con ese número y los datos para recuperarla, y recién
entonces pide el CAE (ADR-025). El puerto de ARCA pasa a tener tres operaciones (próximo número, autorizar con número y
consultar por número, `FECompConsultar`) en el cliente WSFEv1 y en el simulador. Un caso de uso nuevo verifica una
pendiente (después de 1 minuto): recupera el CAE si ARCA ya lo autorizó con el mismo total, emite si no figura, o deja
una observación (importe distinto, sin número, otro entorno) que habilita un descarte confirmado y registrado
(ADR-026). La Api suma la verificación, el descarte y la lista de pendientes; la pantalla del presupuesto cambia
Facturar por "Verificar con ARCA" y la de inicio avisa las pendientes. Decisiones en
`docs/ddw/specs/decisions-FEAT-008.md`; amenazas en `docs/ddw/security/threat-FEAT-008.md`.

## Coverage: PRD → blocks
| Requirement | Covered by |
|---|---|
| FR-01 | Block 1, Block 4 |
| FR-02 | Block 6, Block 7, Block 8 |
| FR-03 | Block 1, Block 6, Block 7, Block 8 |
| FR-04 | Block 2, Block 3, Block 5, Block 6 |
| FR-05 | Block 2, Block 3, Block 5, Block 8 |
| FR-06 | Block 4, Block 5, Block 8 |
| FR-07 | Block 5, Block 6 |
| FR-08 | Block 5, Block 6, Block 7, Block 8 |
| FR-09 | Block 5, Block 6, Block 7, Block 8 |
| FR-10 | Block 1, Block 5 |
| FR-11 | Block 5, Block 6 |
| FR-12 | Block 5, Block 7, Block 8 |
| FR-13 | Block 4, Block 5, Block 8 |
| FR-14 | Block 1, Block 5, Block 6, Block 7 |
| FR-15 | Block 5, Block 6, Block 7 |
| FR-16 | Block 5, Block 6, Block 7, Block 8 |
| NFR-01 | Strategy: la consulta corre con el plazo de `Arca:TiempoMaximoSegundos` (30 s), medido con `TimeProvider` controlado en Block 5 y con 2 s en el harness (Block 8) |
| NFR-02 | Strategy: comprobaciones dentro de la transacción de la pendiente (D-08), candado tomado una vez (D-09) y espera de 1 minuto (FR-16); pruebas en Block 5 y Block 6 que cuentan los comprobantes autorizados por presupuesto en el simulador |
| NFR-03 | Strategy: los e2e de verificación corren en los proyectos de Chrome y Edge del harness (Block 8, D-02) |

## Dependencies between blocks
Block 1 (persistencia) y Block 2 (puerto) primero; Block 3 depende de Block 2; Block 4 de Blocks 1-3; Block 5 de
Block 4; Block 6 de Block 5; Block 7 de Block 6; Block 8 de Block 7. Orden: 1 → 2 → 3 → 4 → 5 → 6 → 7 → 8.

## Block 1 — Dominio y persistencia de pendientes, observaciones y descartes

**Files**
- `src/Optica.Domain/Facturacion/EmisionPendiente.cs` (modified) — suma `Numero` (long?), `Concepto`, `Importes`
  (neto, IVA, alícuota) y `ReceptorIdentificado`; constructor con número desde una `SolicitudComprobante`; fábrica
  `SinNumero` para filas de FEAT-007 (todas las columnas nuevas nulas).
- `src/Optica.Domain/Facturacion/ObservacionEmision.cs` (new) — motivo (`ImporteDistinto`, `SinNumero`,
  `OtroEntorno`), número?, total registrado, total de ARCA?, entorno de la pendiente, momento UTC.
- `src/Optica.Domain/Facturacion/DescarteEmision.cs` (new) — momento UTC, presupuesto, tipo, punto de venta, número?,
  total registrado, total de ARCA?, motivo, entorno.
- `src/Optica.Domain/Facturacion/IEmisionPendienteRepository.cs` (modified) — `ObtenerPendienteAsync(presupuesto)` y
  `ListarNumerosAsync()` (nombres distintos de `IPresupuestoRepository.ObtenerAsync`).
- `src/Optica.Domain/Facturacion/IObservacionEmisionRepository.cs` (new), `IDescarteEmisionRepository.cs` (new).
- `src/Optica.Infrastructure/Persistencia/OpticaDbContext.cs` (modified) — columnas y tablas nuevas, CHECK declarados.
- `src/Optica.Infrastructure/Persistencia/EmisionPendienteRepository.cs` (modified) — graba y lee las columnas nuevas.
- `src/Optica.Infrastructure/Persistencia/ObservacionEmisionRepository.cs` (new),
  `DescarteEmisionRepository.cs` (new).
- `src/Optica.Infrastructure/Persistencia/TextosFacturacion.cs` (modified) — textos de concepto y motivo.
- `src/Optica.Infrastructure/Persistencia/Migraciones/2026111400000_RecuperacionEmisiones.cs` (new) + `.Designer.cs`
  (new) + `OpticaDbContextModelSnapshot.cs` (modified).
- `src/Optica.Infrastructure/ConfiguracionInfraestructura.cs` (modified) — registra los repositorios nuevos.
- Tests: `tests/Optica.Domain.Tests/Facturacion/EmisionPendienteTests.cs` (new), `ObservacionEmisionTests.cs` (new),
  `FacturaTests.cs` (modified, constructor de la pendiente),
  `tests/Optica.Infrastructure.Tests/Persistencia/EmisionPendienteRepositoryTests.cs` (modified),
  `ObservacionDescarteRepositoryTests.cs` (new), `MigracionRecuperacionEmisionesTests.cs` (new),
  `FacturasTriggersTests.cs` (modified: excluir las tablas nuevas de `SqlExistentes`),
  `tests/Optica.Infrastructure.Tests/Acceso/AccesoPersistenciaTests.cs` (modified: `RecuperacionEmisiones` en
  `conocidas`), `tests/Optica.Application.Tests/Facturacion/Dobles.cs` y `EmitirFacturaTests.cs` (modified: dobles con
  los métodos nuevos).

**Logic**
La pendiente nueva siempre tiene número, concepto, importes y receptor identificado (todo o nada, lo impone el
dominio). Una fila con las columnas nuevas nulas se lee como `SinNumero` (FR-14); una fila con algunas nulas y otras
no es inválida y la lectura lanza `InvalidOperationException` con texto fijo. La observación se agrega y se quita; el
descarte solo se agrega (FR-10). Ninguna de las tres guarda el DNI.

**Data model**
- `EmisionPendiente` (existente, sin reconstruir): columnas nuevas por `ALTER TABLE ADD COLUMN` en SQL a mano (D-15):
  `Numero INTEGER NULL CHECK ("Numero" IS NULL OR "Numero" BETWEEN 1 AND 99999999)`,
  `Concepto TEXT NULL CHECK ("Concepto" IS NULL OR "Concepto" IN ('productos','productos_y_servicios'))`,
  `NetoCentavos INTEGER NULL CHECK ("NetoCentavos" IS NULL OR "NetoCentavos" >= 0)`,
  `IvaCentavos INTEGER NULL CHECK ("IvaCentavos" IS NULL OR "IvaCentavos" >= 0)`,
  `Alicuota TEXT NULL` (decimal invariante, null para la C), `ReceptorIdentificado INTEGER NULL CHECK
  ("ReceptorIdentificado" IS NULL OR "ReceptorIdentificado" IN (0,1))`. Se conserva `TR_EmisionPendiente_Inmutable`.
- `ObservacionEmision`: PK `PresupuestoNumero` (FK a `EmisionPendiente.PresupuestoNumero`, Restrict), `Motivo TEXT NOT
  NULL CHECK IN ('importe_distinto','sin_numero','otro_entorno')`, `Numero INTEGER NULL`, `TotalCentavos INTEGER NOT
  NULL >= 0`, `TotalArcaCentavos INTEGER NULL >= 0`, `Entorno TEXT NOT NULL CHECK IN ('simulado','homologacion',
  'produccion')`, `Momento TEXT NOT NULL`; CHECK de tabla: `Motivo <> 'importe_distinto' OR (Numero IS NOT NULL AND
  TotalArcaCentavos IS NOT NULL)`; trigger `TR_ObservacionEmision_Inmutable` BEFORE UPDATE
  `SELECT RAISE(ABORT, 'registro_cerrado');`.
- `DescarteEmision`: PK `Id INTEGER` autoincremental, `Momento TEXT NOT NULL`, `PresupuestoNumero INTEGER NOT NULL`
  (FK a `Presupuesto`, Restrict, índice no único), `Tipo`, `PuntoVenta`, `Numero NULL`, `TotalCentavos NOT NULL`,
  `TotalArcaCentavos NULL`, `Motivo NOT NULL` y `Entorno NOT NULL` con los mismos CHECK; triggers
  `TR_DescarteEmision_SinModificar` (BEFORE UPDATE) y `TR_DescarteEmision_SinBorrar` (BEFORE DELETE), cada uno una sola
  sentencia `SELECT RAISE(ABORT, 'registro_cerrado');`.
- Down (rollback de la migración): `DROP TABLE` de las dos nuevas y `ALTER TABLE ... DROP COLUMN` de las seis
  columnas, sin reconstruir `EmisionPendiente`; las pendientes vuelven a la forma de FEAT-007 (se pierden número y
  datos de recuperación, que FEAT-007 no usa). Antes de migrar en la óptica, backup con `VACUUM INTO` (AGENTS.md).

**Input validation**
- Número de comprobante: 1 a 99.999.999; punto de venta 1 a 99999; importes con 2 decimales y no negativos; concepto
  uno de los dos; motivo uno de los tres; momento distinto del por defecto.

**Error handling**
- Valores inválidos en los constructores — `ArgumentException` con el nombre del campo y sin el valor recibido.
- Fila de pendiente con columnas nuevas parcialmente nulas — `InvalidOperationException` con texto fijo, sin datos.
- Modificar una observación o modificar o borrar un descarte en la base — el trigger aborta con `registro_cerrado`.

**Required tests**
- [ ] EmisionPendiente_ConSolicitudYNumero_GuardaNumeroConceptoEImportes — validates AC-01
- [ ] EmisionPendiente_NumeroInvalido_Rechazado — número 0 o mayor al máximo (sad path)
- [ ] ObservacionEmision_ImporteDistintoSinTotalArca_Rechazado — invalid (sad path)
- [ ] Repositorio_FilaParcialmenteNula_ErrorDeLectura — error (sad path)
- [ ] Repositorio_GrabaYLeePendienteConNumero_SinDni — validates AC-01
- [ ] Repositorio_FilaDeFeat007_SeLeeSinNumero — validates AC-15
- [ ] Repositorio_ListarNumeros_OrdenAscendente — validates AC-03
- [ ] Descarte_UpdateOBorrado_RechazadoPorTrigger — rejected (sad path), validates AC-08
- [ ] Observacion_Update_RechazadoPorTrigger — rejected (sad path)
- [ ] Migracion_SobreBaseConPendientesDeFeat007_ConservaTriggerYColumnasNulas — validates AC-15
- [ ] Migracion_Down_Revierte — el esquema vuelve al de `Facturas`

**Completion criterion**
Las pruebas listadas pasan; `HasPendingModelChanges` da falso; `TR_EmisionPendiente_Inmutable` sigue en la base.

## Block 2 — Puerto de ARCA y cliente WSFEv1

**Files**
- `src/Optica.Application/Facturacion/IAutorizadorComprobantes.cs` (modified) — `ProximoNumeroAsync(tipo, puntoVenta,
  ct)`, `AutorizarAsync(solicitud, numero, ct)` y `ConsultarAsync(tipo, puntoVenta, numero, ct)`.
- `src/Optica.Application/Facturacion/ResultadoProximoNumero.cs` (new) — `Numero(long)` | `NoEnviada(motivo)`.
- `src/Optica.Application/Facturacion/ResultadoConsulta.cs` (new) — `Autorizado(total, cae, vencimiento, fecha)` |
  `NoAutorizado` | `SinRespuesta` | `NoConsultada(motivo)`; sin DNI y sin valores en `ToString`.
- `src/Optica.Infrastructure/Arca/ClienteWsfe.cs` (modified) — `ConsultarComprobanteAsync` (FECompConsultar).
- `src/Optica.Infrastructure/Arca/CodigosWsfe.cs` (modified) — código 602 (sin datos).
- `src/Optica.Infrastructure/Arca/AutorizadorWsfe.cs` (modified) — las tres operaciones.
- `src/Optica.Infrastructure/Arca/AutorizadorNoConfigurado.cs` (modified) — no enviada / no consultada.
- `e2e/ci.mjs` (modified) — `/FECompConsultar` en `RUTAS_ARCA`; `e2e/ci.test.mjs` (modified).
- Tests: `tests/Optica.Infrastructure.Tests/Arca/ClienteWsfeTests.cs`, `AutorizadorWsfeTests.cs`,
  `ClienteWsaaTests.cs`, `OpcionesArcaTests.cs`, `EscenarioArca.cs` (modified);
  `tests/Optica.Infrastructure.Tests/Arca/Respuestas/wsfe-consultar-autorizado.xml`, `wsfe-consultar-602.xml`,
  `wsfe-consultar-error.xml` (new, con DNI ficticio que empieza con 00);
  `tests/Optica.Api.Tests/FabricaApiArcaTests.cs`, `Web/ProtectorDatosTests.cs`,
  `Facturacion/FacturaApiTests.cs`, `Facturacion/RendimientoEmisionTests.cs`,
  `tests/Optica.Application.Tests/Facturacion/Dobles.cs` (modified: dobles del puerto).

**Logic**
`AutorizarAsync` ya no pide el último número: usa el recibido (D-16 de FEAT-007 se mantiene: antes de enviar,
"no enviada"; después, "sin respuesta"). `ProximoNumeroAsync` = ticket WSAA + `FECompUltimoAutorizado` + 1; cualquier
falla es "no enviada". `ConsultarAsync` = ticket + `FECompConsultar`; `Resultado` "A" → autorizado con `ImpTotal`,
`CodAutorizacion`, `FchVto` y `CbteFch`; error 602 → no autorizado; respuesta ilegible, falla de red o cancelación →
sin respuesta; falla antes de enviar → no consultada. Solo se leen esos nodos: `DocTipo` y `DocNro` se ignoran (D-14).

**Input validation**
- Tipo B o C; punto de venta 1 a 99999; número 1 a 99.999.999; token cancelable (sin plazo → no enviada / no
  consultada sin tocar la red).
- Respuesta: `DtdProcessing.Prohibit`, tope de 1 MB, importes y fechas con formato estricto.

**Error handling**
- Token sin plazo — no enviada / no consultada con `MotivoSinPlazo`, sin red.
- Falla de WSAA o del certificado — no enviada / no consultada con el motivo fijo.
- Respuesta ilegible o sin el nodo esperado — sin respuesta.
- Timeout o cancelación después de enviar la consulta — sin respuesta.
- Error de ARCA distinto de 602 en la consulta — sin respuesta con el código en el log (sin el texto).

**Required tests**
- [ ] Consultar_Autorizado_DevuelveTotalCaeVencimientoYFecha — validates AC-05
- [ ] Consultar_Error602_NoAutorizado — validates AC-06
- [ ] Consultar_TokenSinPlazo_NoConsultadaSinRed — invalid token (sad path)
- [ ] Consultar_FallaWsaa_NoConsultada — error (sad path)
- [ ] Consultar_RespuestaIlegible_SinRespuesta — invalid response (sad path)
- [ ] Consultar_Timeout_SinRespuesta — timeout (sad path), validates AC-11
- [ ] Consultar_OtroError_SinRespuesta — rejected by ARCA (sad path)
- [ ] Consultar_RespuestaConDni_NiLogNiResultadoLoLlevan — validates R-01
- [ ] ProximoNumero_UltimoMasUno — validates AC-01
- [ ] Autorizar_UsaElNumeroRecibido — validates AC-01

**Completion criterion**
Las pruebas pasan; `npm run test:unidad` en `e2e/` acepta `/FECompConsultar`.

## Block 3 — Simulador

**Files**
- `src/Optica.Infrastructure/Arca/SimuladorArca.cs` (modified) — las tres operaciones; el archivo guarda los últimos
  50 comprobantes por tipo y punto de venta (total en centavos, CAE, vencimiento, fecha); límite 256 KB (D-13).
- `src/Optica.Infrastructure/Arca/OpcionesArca.cs` (modified) — respuestas `autorizar_sin_responder` y
  `no_responder_una_vez`; clave `Arca:Simulado:Consulta` (`responder` por defecto, `no_responder`) (D-19); clave
  `Arca:EsperaVerificacionSegundos` (60 por defecto, entero de 1 a 3600) (D-20).
- `src/Optica.Infrastructure/ConfiguracionInfraestructura.cs` (modified) — `CrearAutorizador` pasa las opciones.
- Tests: `tests/Optica.Infrastructure.Tests/Arca/SimuladorArcaTests.cs`, `OpcionesArcaTests.cs` (modified),
  `tests/Optica.Api.Tests/Arquitectura/AislamientoArcaTests.cs` (modified).

**Logic**
`ProximoNumeroAsync` lee el contador. `AutorizarAsync(solicitud, numero)`: si el número no es contador + 1, rechazo
10016; si no, registra el comprobante y responde según la respuesta configurada (`autorizar_sin_responder` registra y
espera la cancelación → sin respuesta; `no_responder_una_vez` no responde la primera solicitud del proceso y autoriza
las siguientes). `ConsultarAsync` busca el comprobante o responde no autorizado; con `no_responder` espera la
cancelación.

**Data model**
- Archivo `arca-simulado.json`: `{"contadores": {"B-3": 125}, "comprobantes": {"B-3": [{"numero":125,
  "totalCentavos":181500,"cae":"...","vencimiento":"2026-11-24","fecha":"2026-11-14"}]}}`; sin datos del receptor.
  Las versiones de FEAT-007 (solo contadores) se siguen leyendo.
- Restricciones: clave `B-<pv>` o `C-<pv>` con pv de 1 a 99999 (unique por objeto JSON); `numero` not null de 1 a
  99.999.999 y unique dentro de su lista; `totalCentavos` not null ≥ 0; `cae` not null de 14 dígitos; `vencimiento` y
  `fecha` not null `yyyy-MM-dd`; a lo sumo 50 elementos por lista (los más altos); contador not null ≥ 0 y default 0
  si falta la clave.

**Input validation**
- Archivo: objeto JSON de profundidad máxima 4, ≤ 256 KB, números en rango, fechas `yyyy-MM-dd`, CAE de 14 dígitos.
- Opciones: valores admitidos en minúsculas; otro valor es error de configuración al arrancar.

**Error handling**
- Archivo ilegible o con forma inválida — no enviada / no consultada con `MotivoContador`, log con el tipo de falla.
- Número distinto de contador + 1 — rechazo 10016.
- Opción con un valor desconocido — error de configuración al arrancar con la lista de valores admitidos.

**Required tests**
- [ ] Simulador_AutorizaYConsulta_MismoTotal — validates AC-05
- [ ] Simulador_ConsultaNumeroNoAutorizado_NoAutorizado — validates AC-06
- [ ] Simulador_AutorizarSinResponder_RegistraYSinRespuesta — validates AC-05
- [ ] Simulador_NoResponderUnaVez_SegundaAutoriza — validates AC-06
- [ ] Simulador_ConsultaNoResponder_SinRespuesta — timeout (sad path), validates AC-11
- [ ] Simulador_NumeroIncorrecto_Rechazado — rejected (sad path)
- [ ] Simulador_ArchivoInvalido_NoConsultada — invalid file (sad path)
- [ ] Opciones_ValorDesconocido_ErrorDeConfiguracion — invalid option (sad path)
- [ ] Simulador_CrecimientoMasDe50_ConservaUltimos50YNoSuperaLimite — validates R-05
- [ ] Simulador_ArchivoDeFeat007_SeLee — compatibilidad

**Completion criterion**
Las pruebas pasan y el simulador nunca guarda datos del receptor.

## Block 4 — Emisión con número registrado

**Files**
- `src/Optica.Application/Facturacion/ProcesoEmision.cs` (new, `public sealed`, D-17) — con el turno y el plazo
  recibidos: próximo número; transacción `BEGIN IMMEDIATE` que repite las comprobaciones (Final, sin factura, sin
  pendiente o con la pendiente vieja que se reemplaza) y graba la pendiente con número (D-08, D-11); margen; autorizar;
  transacción 2 (igual que FEAT-007).
- `src/Optica.Application/Facturacion/EmitirFactura.cs` (modified) — transacción 1 de solo lectura; toma el turno y
  llama a `ProcesoEmision` (D-09).
- `src/Optica.Api/Program.cs` (modified) — registra `ProcesoEmision`.
- Tests: `tests/Optica.Application.Tests/Facturacion/EmitirFacturaTests.cs`, `ProcesoEmisionTests.cs` (new),
  `tests/Optica.Api.Tests/Facturacion/FacturaApiTests.cs` (modified: `Emitir_DosPedidosSimultaneos_UnoConflictoPendiente`
  y `Emitir_AutorizadorLanza_500ConLaPendienteGuardada` por el nuevo orden).

**Logic**
FR-01: el número queda grabado antes de FECAESolicitar. Sin respuesta de ARCA, la pendiente queda con su número (FR-13
cuando viene de la verificación). Las transacciones que graban no usan el token del plazo (D-10).

**Data model**
- Reuses `EmisionPendiente` from Block 1 — no schema change; constraints as declared there: PK por presupuesto,
  `Numero` 1 a 99.999.999 o NULL, CHECK de concepto, importes no negativos, trigger de inmutabilidad.

**Input validation**
- Concepto `productos` o `productos_y_servicios` (sin cambios respecto de FEAT-007).

**Error handling**
- Próximo número falla — no enviada, nada grabado (o la pendiente vieja intacta si venía de la verificación).
- Otro pedido grabó la factura o la pendiente primero — conflicto `facturado` o `pendiente`, sin llamar a FECAESolicitar.
- Margen insuficiente al llegar al envío — no enviada y se quita la pendiente recién grabada.
- Sin respuesta de ARCA — la pendiente con número queda.

**Required tests**
- [ ] Emitir_RegistraNumero125AntesDeEnviar — validates AC-01
- [ ] Emitir_ProximoNumeroFalla_NoEnviadaSinPendiente — error (sad path)
- [ ] Emitir_FacturaGrabadaPorOtroAntesDeLaPendiente_ConflictoSinEnviar — conflict (sad path), validates NFR-02
- [ ] Emitir_MargenInsuficiente_NoEnviadaYQuitaPendiente — timeout (sad path)
- [ ] Emitir_SinRespuesta_PendienteConNumero — timeout (sad path), validates AC-12
- [ ] Emitir_DosPedidosEnParalelo_UnSoloComprobante — duplicate (sad path), validates NFR-02

**Completion criterion**
Las pruebas pasan y las pruebas de FEAT-007 de la emisión siguen en verde.

## Block 5 — Verificación y descarte

**Files**
- `src/Optica.Application/Facturacion/VerificarEmision.cs` (new), `ResultadoVerificacion.cs` (new).
- `src/Optica.Application/Facturacion/DescartarEmision.cs` (new), `ResultadoDescarte.cs` (new).
- `src/Optica.Application/Facturacion/ListarEmisionesPendientes.cs` (new).
- `src/Optica.Application/Facturacion/ObtenerEstadoFacturacion.cs` (modified) — suma la observación.
- `src/Optica.Domain/Facturacion/Factura.cs` (modified) — fábrica `Recuperada` (D-18).
- Tests: `tests/Optica.Application.Tests/Facturacion/VerificarEmisionTests.cs` (new),
  `DescartarEmisionTests.cs` (new), `ListarEmisionesPendientesTests.cs` (new), `ObtenerEstadoFacturacionTests.cs`
  (modified), `tests/Optica.Domain.Tests/Facturacion/FacturaTests.cs` (modified).

**Logic**
`VerificarEmision`: sin pendiente → `sin_pendiente`; menos de `EsperaVerificacion` (60 s) desde `Momento` →
`espera` con los segundos que faltan (FR-16); sin número → observación `sin_numero` (FR-14); entorno distinto del
configurado → observación `otro_entorno` (FR-15). Si no: toma el turno del candado con plazo de 30 s (D-09, D-10) y
consulta: autorizado con el mismo total en centavos → transacción que graba `Factura.Recuperada` y quita observación
y pendiente (FR-05); autorizado con otro total → reemplaza la observación por `importe_distinto` con ambos totales
(FR-07, FR-08); no autorizado → `ProcesoEmision` con el turno y un plazo nuevo de 30 s, reemplazando la pendiente
vieja (FR-06, D-11); sin respuesta o no consultada → pendiente intacta, `sin_respuesta` (FR-12). Nunca reintenta.
`DescartarEmision`: sin pendiente → `sin_pendiente`; sin observación vigente o con un motivo que el estado actual ya no
sostiene → `sin_observacion` (FR-11, D-12); si no, transacción que graba el descarte (FR-10) y quita observación y
pendiente (FR-09).

**Data model**
- Reuses `EmisionPendiente`, `ObservacionEmision` y `DescarteEmision` from Block 1 — no schema change; constraints as
  declared there: PK por presupuesto, CHECK de motivo y de `importe_distinto`, triggers `registro_cerrado`.

**Input validation**
- Número de presupuesto ≥ 1 (desde la ruta).

**Error handling**
- Presupuesto sin pendiente — `sin_pendiente`.
- Antes de 1 minuto — `espera` con los segundos que faltan, sin llamar a ARCA.
- Pendiente sin número u otro entorno — observación grabada, sin llamar a ARCA.
- Importe distinto — observación con los dos totales, sin emitir ni recuperar.
- Consulta sin respuesta, cancelada por el plazo o no consultada — pendiente intacta, `sin_respuesta`.
- La factura recuperada no se puede grabar o no valida (entorno, vencimiento) — pendiente intacta,
  `no_enviada` con el motivo en el log sin DNI.
- Descarte sin observación vigente — `sin_observacion`.

**Required tests**
- [ ] Verificar_AutorizadoMismoTotal_RecuperaSinEnviarSolicitud — validates AC-05
- [ ] Verificar_NoAutorizado_ConsultaPrimeroYEmiteConNumeroNuevo — validates AC-06
- [ ] Verificar_ImporteDistinto_ObservacionSinEmitir — rejected (sad path), validates AC-07
- [ ] Verificar_ConsultaSinRespuesta30s_PendienteIntactaSinReintento — timeout (sad path), validates AC-11
- [ ] Verificar_EmisionSinRespuesta_PendienteConNumeroNuevo — timeout (sad path), validates AC-12
- [ ] Verificar_SinNumero_ObservacionSinConsultar — missing number (sad path), validates AC-15
- [ ] Verificar_OtroEntorno_ObservacionSinConsultar — invalid entorno (sad path), validates AC-16
- [ ] Verificar_A20Segundos_EsperaFaltan40SinConsultar — rejected (sad path), validates AC-17
- [ ] Verificar_SinPendiente_Conflicto — missing (sad path)
- [ ] Verificar_RecuperadaNoGrabable_PendienteIntacta — error (sad path)
- [ ] Descartar_ConObservacion_RegistraYQuitaPendiente — validates AC-08
- [ ] Descartar_SinObservacion_Rechazado — rejected (sad path), validates AC-10
- [ ] Descartar_OtroEntornoQueYaCoincide_Rechazado — rejected (sad path), validates AC-10
- [ ] Verificar_EscenariosAC05aAC12_UnComprobantePorPresupuesto — validates AC-13 (simulador), NFR-02
- [ ] Listar_DosPendientes_155y160 — validates AC-03
- [ ] Listar_SinPendientes_Vacia — validates AC-04

**Completion criterion**
Las pruebas pasan con `TimeProvider` controlado (30 s y 60 s sin esperas reales).

## Block 6 — Api

**Files**
- `src/Optica.Api/Controllers/PresupuestosController.cs` (modified) — verificación, descarte y la observación en el GET.
- `src/Optica.Api/Controllers/FacturacionController.cs` (new) — lista de pendientes.
- `src/Optica.Api/Errores/RespuestasValidacion.cs` (modified) — motivos nuevos.
- `src/Optica.Api/Program.cs` (modified) — casos de uso nuevos.
- Tests: `tests/Optica.Api.Tests/Facturacion/VerificacionApiTests.cs` (new, incluye AC-15 y AC-16 con filas cargadas
  por SQL, D-06), `tests/Optica.Api.Tests/Http.cs`, `Presupuestos/PresupuestosApiTests.cs`,
  `Presupuestos/VolumenPresupuestosTests.cs`, `Presupuestos/ConcurrenciaPresupuestosApiTests.cs`,
  `Presupuestos/CierrePorDefectoPresupuestosTests.cs`, `Acceso/ProteccionTests.cs`, `Acceso/SinCacheTests.cs` (modified).

**Logic**
Los controladores solo traducen los resultados; los motivos 409 siguen `ConflictoFactura` de FEAT-007.

**API contract**
- Method + path: `POST /api/presupuestos/{numero}/factura/verificacion`
- Request: sin cuerpo (se ignora; límite 1 KB → 413).
- Response: 200 `{ "resultado": "recuperada" | "emitida", "factura": { "comprobante", "tipo", "numero", "cae",
  "vencimientoCae", "fecha", "total", "neto", "iva" } }`.
- Error codes: 404 (sin presupuesto); 409 `{ "motivo": "sin_pendiente" | "espera" (con `segundosRestantes`) |
  "importe_distinto" (con `comprobante`, `importeRegistrado`, `importeArca`) | "sin_numero" | "otro_entorno" (con
  `entorno`) | "sin_respuesta" | "sin_respuesta_emision" | "rechazada" (con `codigoArca`, `descripcionArca`) |
  "no_enviada" | "facturado" }`; 401 sin sesión.
- Auth: sesión obligatoria (cookie), como el resto de `/api`.
- Method + path: `POST /api/presupuestos/{numero}/factura/descarte`
- Request: sin cuerpo.
- Response: 204.
- Error codes: 404; 409 `sin_pendiente` | `sin_observacion`; 401.
- Auth: sesión obligatoria.
- Method + path: `GET /api/facturacion/emisiones-pendientes`
- Request: sin parámetros.
- Response: 200 `{ "presupuestos": [155, 160] }` (solo números, ascendente).
- Error codes: 401.
- Auth: sesión obligatoria.
- `GET /api/presupuestos/{numero}` suma `observacionEmision`: null o `{ "motivo", "comprobante" | null,
  "importeRegistrado", "importeArca" | null, "entorno" }`.

**Data model**
- Reuses `EmisionPendiente`, `ObservacionEmision` y `DescarteEmision` from Block 1 — no schema change; constraints as
  declared there: PK por presupuesto, CHECK de motivo, triggers `registro_cerrado`.

**Input validation**
- `{numero}`: `long` de la ruta (`{numero:long}`); otro formato → 404 de enrutamiento. Cuerpo ignorado, ≤ 1 KB.

**Error handling**
- Sin sesión — 401.
- Presupuesto inexistente — 404.
- Motivos de la verificación o del descarte — 409 con el motivo y sin datos del cliente.
- Cuerpo mayor a 1 KB — 413.
- Resultado no previsto — excepción (500, cerrado por defecto).

**Required tests**
- [ ] Verificacion_Recuperada_200ConFactura — validates AC-05
- [ ] Verificacion_ImporteDistinto_409ConImportes — rejected (sad path), validates AC-07
- [ ] Verificacion_PendienteSinNumero_409SinNumero — missing (sad path), validates AC-15
- [ ] Verificacion_PendienteOtroEntorno_409OtroEntorno — invalid (sad path), validates AC-16
- [ ] Verificacion_Antes60s_409Espera — rejected (sad path), validates AC-17
- [ ] Descarte_DirectoSinVerificar_409SinObservacion — rejected (sad path), validates AC-10
- [ ] Descarte_TrasObservacion_204YGetSinPendiente — validates AC-08
- [ ] RutasNuevas_SinSesion_401 — unauthorized (sad path)
- [ ] Verificacion_PresupuestoInexistente_404 — missing (sad path)
- [ ] Verificacion_CuerpoGrande_413 — invalid (sad path)
- [ ] Verificacion_ResultadoNoPrevisto_Error500 — error (sad path)
- [ ] EmisionesPendientes_SoloNumeros — validates AC-03, AC-04
- [ ] Get_ConObservacion_SinDni — validates AC-02, R-01

**Completion criterion**
Las pruebas pasan y las rutas nuevas tienen `no-store`.

## Block 7 — Pantallas

**Files**
- `web/src/api/cliente.ts` (modified) — `verificarEmision`, `descartarEmision`, `listarEmisionesPendientes`, tipo
  `ObservacionEmision`, `observacionEmision` en `PresupuestoConsultado`.
- `web/src/paginas/Presupuesto.tsx` (modified) — "Verificar con ARCA" en lugar de Facturar con pendiente; avisos;
  "Descartar la emisión" con diálogo de confirmación.
- `web/src/paginas/Inicio.tsx` (modified) — aviso de pendientes.
- `web/src/presupuestos/usePresupuestoDeLaRuta.ts` (modified, tipo).
- Tests: `web/src/paginas/Presupuesto.test.tsx`, `Inicio.test.tsx`, `web/src/api/cliente.test.ts`, `App.test.tsx`,
  `BuscarPresupuestos.test.tsx`, `EditarPresupuesto.test.tsx`, `NuevoPresupuesto.test.tsx` (modified).

**Logic**
Textos: botón "Verificar con ARCA"; recuperada "Se recuperó la Factura B 0003-00000125 que ARCA ya había
autorizado."; emitida, el mensaje de FEAT-007; importe distinto "Revisá el punto de venta en ARCA: la Factura B
0003-00000125 figura autorizada por $ 2.000,00 y esta venta registró $ 1.815,00."; sin número "Revisá el punto de
venta en ARCA: esta emisión no tiene número de comprobante registrado."; otro entorno "Revisá el punto de venta en
ARCA: esta emisión se envió al entorno Simulado y el sistema está configurado en otro entorno."; espera "Esperá 40
segundos para verificar con ARCA."; sin respuesta "ARCA no respondió. La emisión sigue pendiente de verificación.";
diálogo "¿Descartar la emisión pendiente? El presupuesto va a poder volver a facturarse." con "Descartar" y
"Cancelar"; tras descartar "Se descartó la emisión pendiente."; Inicio "Hay emisiones pendientes de verificación en
los presupuestos 155 y 160." (enlaces a cada presupuesto). Inicio pide la lista una vez al cargar; una falla no
muestra nada.

**Input validation**
- Sin campos de entrada: la pantalla solo envía el número del presupuesto que muestra.

**Error handling**
- 409 de la verificación — el texto del motivo; vuelve a leer el presupuesto.
- 401 — va a `/ingreso`.
- Falla de red o respuesta ilegible — mensaje fijo del cliente y vuelve a leer el presupuesto.
- Falla al listar pendientes en Inicio — no muestra el aviso.

**Required tests**
- [ ] Presupuesto_ConPendiente_MuestraVerificarYNoFacturar — validates AC-02
- [ ] Presupuesto_ImporteDistinto_MuestraAvisoYDescartar — rejected (sad path), validates AC-07
- [ ] Presupuesto_DescartarConfirmado_MuestraFacturar — validates AC-08
- [ ] Presupuesto_DescartarCancelado_SigueVerificar — validates AC-09
- [ ] Presupuesto_SinRespuesta_MensajeYSigueVerificar — timeout (sad path), validates AC-11
- [ ] Presupuesto_SinNumero_Aviso — missing (sad path), validates AC-15
- [ ] Presupuesto_OtroEntorno_Aviso — invalid (sad path), validates AC-16
- [ ] Presupuesto_Espera_MuestraSegundos — rejected (sad path), validates AC-17
- [ ] Presupuesto_SinSesion_VaAIngreso — unauthorized (sad path)
- [ ] Inicio_ConPendientes_Aviso155y160 — validates AC-03
- [ ] Inicio_SinPendientes_SinAviso — validates AC-04
- [ ] Inicio_FallaAlListar_SinAvisoNiError — error (sad path)

**Completion criterion**
Las pruebas de Vitest pasan con la cobertura del proyecto y `npm run lint` limpio.

## Block 8 — e2e

**Files**
- `e2e/tests/verificacion-recupera.spec.ts` (new) — `autorizar_sin_responder`: AC-02, AC-05, AC-03, AC-04.
- `e2e/tests/verificacion-emite.spec.ts` (new) — `no_responder_una_vez`: AC-06, AC-17.
- `e2e/tests/verificacion-sin-respuesta.spec.ts` (new) — `no_responder` y consulta `no_responder`: AC-11, AC-12.
- `e2e/tests/verificacion-importe-distinto.spec.ts` (new) — archivo del simulador sembrado con otro total: AC-07,
  AC-08, AC-09.
- `e2e/tests/facturacion-sin-respuesta.spec.ts` (modified) — "Verificar con ARCA" en lugar de Facturar deshabilitado.
- `e2e/compilacion.mjs` (modified) — `variablesDeArca` y `contadorSimulado` por archivo, con
  `Arca__EsperaVerificacionSegundos=3`; `e2e/compilacion.test.mjs` (modified); `e2e/README.md` (modified).
- `docs/instalacion.md` (modified) — opciones nuevas, archivo del simulador, verificación y descarte.

**Logic**
Cada archivo tiene su Api por navegador (Chrome y Edge, AC-14) con ARCA simulada y tiempos cortos (2 s de plazo, 3 s
de espera). Clientes ficticios con DNI que empieza con 00 y emails `@example.com`.

**Input validation**
- Las variables por archivo admiten solo los valores de `OpcionesArca`; otro valor detiene el harness.

**Error handling**
- Variable desconocida en el mapa de archivos — el harness se detiene con el nombre del archivo.
- Api que no responde al arrancar — el harness se detiene como en FEAT-007.

**Required tests**
- [ ] e2e verificación recupera — validates AC-02, AC-05, AC-03, AC-04
- [ ] e2e verificación emite y espera — validates AC-06, AC-17
- [ ] e2e consulta sin respuesta y emisión sin respuesta — timeout (sad path), validates AC-11, AC-12
- [ ] e2e importe distinto, descarte y cancelación — rejected (sad path), validates AC-07, AC-08, AC-09
- [ ] compilacion.test: valor de simulador inválido detiene el harness — invalid (sad path)
- [ ] los cuatro archivos corren en los proyectos de Chrome y de Edge estables — validates AC-14

**Completion criterion**
`npm run test:harness` pasa en Chrome y Edge, y `npm run test:unidad` pasa.

## Final verification
`dotnet build` y `dotnet test Optica.sln`, `npm run lint`, `npm run test:lint`, `npm run test:coverage` y
`npm run build` en `web/`, `npm run test:unidad` y `npm run test:harness` en `e2e/` en verde; AC-13 queda pendiente de
verificación manual contra homologación (D-01); `TR_EmisionPendiente_Inmutable` sigue en la base migrada; ningún log,
respuesta, tabla nueva ni archivo del simulador contiene el DNI.
