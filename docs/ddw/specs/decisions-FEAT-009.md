# Decisiones FEAT-009 — PDF, listado y búsqueda de facturas

Formato: decisión — quién la aprobó — fecha — dónde aplica.

## DEFINE
- **D-01 (del usuario).** Un solo ticket con los 27 AC (W-PRD-06 aceptado); se descartaron las divisiones en dos y en
  tres partes. — usuario — 2026-11-16 — PRD.
- **D-02 (del usuario).** NFR-01 y AC-25 solo en la versión estable vigente de Chrome y Edge; la anterior queda como
  verificación manual. — usuario — 2026-11-16 — PRD, Block 8.
- **D-03 (del usuario).** El listado y la búsqueda usan Apellido, Nombre y DNI del cliente del presupuesto de origen
  para todas las facturas, también las de Consumidor Final (FR-25, AC-26); solo el PDF dice "Consumidor Final". —
  usuario — 2026-11-16 — PRD, Blocks 5 y 6.
- **D-04 (del usuario).** La leyenda de la Ley 27.743 (FR-11, AC-13) se incluye con riesgo abierto: el contador debe
  confirmarla antes de facturar en producción. — usuario — 2026-11-16 — PRD, Block 4.
- **D-05 (del usuario).** Modo `minimal` para este ticket. — usuario — 2026-11-16 — todo el ticket.
- **D-06 (del usuario).** Delegación: ante una decisión no escrita, el agente toma la opción recomendada que menos
  tiempo lleve y la registra acá (memoria del 2026-11-14, vigente). — usuario — 2026-11-14 — todo el ticket.
- **D-07 (del agente).** Condición frente al IVA del emisor según el tipo de comprobante (B "Responsable Inscripto",
  C "Responsable Monotributo") y no según el parámetro vigente, para que un cambio posterior del parámetro no altere el
  PDF de una factura vieja (FR-06, AC-07, AC-27). — agente (D-06) — 2026-11-16 — PRD, Block 4.
- **D-08 (del agente).** DNI ficticios que empiezan con 00 en los ejemplos del PRD y en las pruebas, para no usar un
  DNI que pueda ser de una persona real. — agente (D-06) — 2026-11-16 — PRD, todas las pruebas.

## PLAN
- **D-09 (del agente).** Datos del emisor como agregado propio con tabla, endpoint y sección de pantalla propios
  (ADR-028). — agente (D-06) — 2026-11-16 — Blocks 1 a 3.
- **D-10 (del agente).** Límites de los datos del emisor: razón social 1 a 120, domicilio 1 a 200, Ingresos Brutos 1 a
  30 (texto libre), fecha de inicio no posterior a hoy. — agente (D-06) — 2026-11-16 — Block 1.
- **D-11 (del agente).** QRCoder 1.6.0 con `PngByteQRCode` (ADR-027); la 1.8.0 arrastra `System.Drawing.Common`. —
  agente (D-06) — 2026-11-16 — Block 4.
- **D-12 (del agente).** `Cuit` como objeto de valor de dominio, reutilizado por `OpcionesArca` en lugar de mantener
  dos implementaciones del módulo 11. — agente (D-06) — 2026-11-16 — Block 1.
- **D-13 (del agente, corregida tras la auditoría).** El plegado de Apellido y Nombre de la búsqueda le llega a
  Facturación a través de Presupuestos (`PlegadoCliente`, que delega en `PlegadoBusqueda` como `FiltroPresupuestos`);
  `FiltroFacturas` pliega por su cuenta. Primero se había propuesto plegar en Application (WARN 3). — agente (D-06) —
  2026-11-16 — Block 6.
- **D-14 (del agente).** El detalle reutiliza `IFacturaRepository.ObtenerPorPresupuestoAsync`; la factura se
  identifica por el número de presupuesto en la Api y en la web (`/facturas/:numero`). — agente (D-06) — 2026-11-16 —
  Blocks 5 y 7.
- **D-15 (del agente).** El comprobante viaja y se muestra como texto ("Factura B 0003-00000125"), sin campo ni
  columna "punto de venta", por `SinDatosArcaTests` y `Pantallas_NingunaMuestraDatosArca`. — agente (D-06) —
  2026-11-16 — Blocks 5 a 7.
- **D-16 (del agente).** AC-12: el contenido del QR se verifica sobre la URL y el JSON de `QrArca` y la presencia de la
  imagen en el PDF; el escaneo con un teléfono queda como verificación manual del usuario (no hay decodificador en las
  pruebas). — agente (D-06) — 2026-11-16 — Blocks 4 y 8.
- **D-17 (del agente).** El CUIT del QR sale de los datos del emisor y no se contrasta con `Arca:Cuit`; la guía de
  instalación pide que coincidan (R-10). Revisar antes de facturar en producción. — agente (D-06) — 2026-11-16 —
  Blocks 4 y 8.

## PLAN — auditoría de arquitectura (24 hallazgos, todos aplicados o registrados)
- **D-18 (del agente).** `OpcionesArca` valida `Arca:Cuit` con `Cuit.EsValidoCanonico` (sin guiones), para que un CUIT
  con guiones no llegue a WSAA/WSFEv1 (FAIL 1). — agente (D-06) — 2026-11-16 — Block 1.
- **D-19 (del agente).** `DatosEmisor` valida la fecha de inicio solo al grabar, con la fecha `hoy` que pasa
  Application; la lectura no la aplica (WARN 4, precedente ADR-014). — agente (D-06) — 2026-11-16 — Block 1.
- **D-20 (del agente).** Orden de la búsqueda `Fecha` desc, `Numero` desc, `Id` desc como desempate total (FAIL 2). —
  agente (D-06) — 2026-11-16 — Block 6.
- **D-21 (del agente).** Tope del cuerpo de los datos del emisor en 2 KB (INFO 19). — agente (D-06) — 2026-11-16 —
  Block 2.
- **D-22 (del agente).** La pantalla no tolera 404 en el GET del emisor; "sin cargar" es el 200 con nulls (WARN 12).
  — agente (D-06) — 2026-11-16 — Block 3.
- **D-23 (del agente).** Fuera de Produccion el QR lleva `tipoDocRec` 99 y `nroDocRec` 0 aunque la factura tenga DNI:
  el comprobante no existe en ARCA y escanearlo no debe mandar el DNI (INFO 20, R-11). — agente (D-06) — 2026-11-16 —
  Block 4.
- **D-24 (del agente).** `QrArca` repite códigos de `CodigosWsfe`; una prueba compara ambas fuentes (WARN 8, ADR-027).
  — agente (D-06) — 2026-11-16 — Block 4.
- **D-25 (del agente).** `GeneradorPdfFactura` relanza fallas con texto fijo; la configuración de QuestPDF se
  centraliza en `ConfiguracionQuestPdf`, que también usa `GeneradorPdfPresupuesto` (WARN 11). — agente (D-06) —
  2026-11-16 — Block 4.
- **D-26 (del agente).** Motivo del 409 `sin_datos_emisor` (como `sin_tope`, `sin_punto_venta`) desde un helper de
  conjunto cerrado (INFO 22). — agente (D-06) — 2026-11-16 — Block 5.
- **D-27 (del agente).** Búsqueda del número sobre los dígitos del texto visible (`D4` + `D8`) con la función SQLite
  registrada `digitos_comprobante` (WARN 9). — agente (D-06) — 2026-11-16 — Block 6.
- **D-28 (del agente).** La búsqueda va en `FacturacionController` (`POST /api/facturacion/busqueda`); la ruta web
  `/facturas/:numero` usa el número del presupuesto (WARN 13). — agente (D-06) — 2026-11-16 — Blocks 6 y 7.
- Otros hallazgos aplicados sin decisión nueva: transacción en `GrabarDatosEmisor` (WARN 5), consecuencia del emisor
  vigente en ADR-028 (WARN 6), condición previa de producción para el CUIT (WARN 7, D-17), `PrintMembers => false` y
  casos de uso sin registrador (WARN 10), prueba de migración con todos los triggers (WARN 14), decisiones y ADR
  escritos en PLAN (WARN 15), reglas del comprobante impreso en el dominio (INFO 16), recorrido completo aceptado como
  en ADR-011 (INFO 18), comentario de `RUTAS_ARCA` (INFO 21), excepción propia del filtro y validación en el borde
  (INFO 23), origen del DNI escrito (INFO 24). INFO 17: sin cambios (lecturas entre módulos aceptables).

## CODE
- **D-29 (del agente, Block 1).** `DatosEmisor.ParaGrabar` recibe la fecha de inicio como texto `yyyy-MM-dd` y la
  interpreta en el dominio: una fecha mal escrita es una infracción del campo y no una excepción de Application. La
  spec nombra `DatosEmisor.Crear` en Error handling; lo implementado es `ParaGrabar`, que es lo que dice Files. —
  agente (D-06), aceptado por el verificador y el auditor — 2026-11-16 — Block 1.
- **D-30 (del agente, Block 1).** CHECK extra `CK_DatosEmisor_InicioActividades` con GLOB `yyyy-MM-dd`, como `Fecha`
  en `Presupuesto` y `Factura`. — agente (D-06) — 2026-11-16 — Block 1.
- **D-31 (del agente, Block 1).** `OpcionesArca.EsCuitValido` se conserva como método que delega en
  `Cuit.EsValidoCanonico` (lo usan `ClienteWsfe.cs` y `AislamientoArcaTests.cs`); el algoritmo y `PesosCuit` se
  eliminaron, así que hay una sola implementación del módulo 11. — agente (D-06) — 2026-11-16 — Block 1.
- **D-32 (del agente, Block 1).** `FacturasTriggersTests.SqlExistentes` excluye la tabla `DatosEmisor`, porque revertir
  hasta `ConfiguracionPuntoVenta` también la borra (igual que hizo FEAT-008 con sus tablas). — agente (D-06) —
  2026-11-16 — Block 1.
- **D-33 (del agente, Block 1, datos personales).** El CUIT 20-12345678-6 del PRD tiene dígito verificador válido y
  podría ser de una persona real: queda solo en las pruebas de AC-03 y AC-04; el resto usa 20-00000000-1. Nunca se usa
  en bases de demostración ni contra ARCA. — agente (D-06) — 2026-11-16 — Block 1 y siguientes.
- **D-34 (del agente, Block 2).** `GrabarDatosEmisor` valida con `DatosEmisor.ParaGrabar` antes de abrir la
  transacción (la validación no depende de la base, como en `GrabarConfiguracionConRecalculo`, y un pedido inválido no
  toma el lock de escritura); la lectura de los vigentes, el reemplazo y la confirmación van dentro. La spec decía
  "dentro: lee, valida, reemplaza". — agente (D-06), aceptado por el verificador y el auditor — 2026-11-16 — Block 2.
- **D-35 (del agente, Block 3).** El botón de la sección se llama "Grabar datos del emisor" (dos "Grabar" en la misma
  pantalla rompían las pruebas que lo buscan por nombre); los e2e existentes que buscan "Grabar" en la configuración
  (`configuracion.spec.ts`, `configuracion-recalculo.spec.ts`, `articulos-edicion.spec.ts`,
  `presupuestos-edicion.spec.ts`) pasan a `exact: true`, fuera de la lista de archivos del Block 3 (FAIL 1 de la
  auditoría). La sección se separa en `SeccionDatosEmisor.tsx`. — agente (D-06) — 2026-11-16 — Block 3.
- **D-36 (del agente, Block 3).** La web envía los textos recortados y el CUIT canónico, toma "hoy" del navegador
  (decide el servidor cerca de la medianoche) y no usa `maxLength` en los campos del emisor, para que un texto largo
  muestre el error del largo en lugar de cortarse. — agente (D-06) — 2026-11-16 — Block 3.
- **D-37 (del agente, Block 3).** `web/src/api/fechas.ts` unifica `FORMATO_FECHA` y `esFecha` (con
  `setUTCFullYear`, años 1 a 99 aceptados y año 0 rechazado, como `DateOnly`); el cambio alcanza también los lectores
  de fechas de presupuestos y facturas de `cliente.ts` (FEAT-003 a FEAT-008), que antes leían los años 0 a 99 como 19xx.
  La Api nunca envía esas fechas; ninguna prueba cambió de resultado. — agente (D-06), revisado por el auditor —
  2026-11-16 — Block 3.
- **D-38 (del agente, Block 4).** Con un logo que no se puede decodificar, el PDF de la factura sale sin logo (lo que
  pide el Error handling del Block 4 y su prueba requerida). La spec decía "como en `GeneradorPdfPresupuesto`", pero ese
  generador relanza la falla: los dos quedan con criterios distintos y alinearlos va en otro ticket (sería un refactor
  ajeno a este). — agente (D-06), señalado por el verificador y el auditor — 2026-11-16 — Block 4.
- **D-39 (del agente, Block 4).** QR de ARCA: base64 estándar sin codificar para URL en `?p=`, como la especificación
  y el ejemplo oficial de ARCA (con este JSON solo puede aparecer `=`); `cuit`, `nroDocRec` y `codAut` como números e
  `importe` decimal con 2 decimales; corrección M. El escaneo con teléfono sigue manual (D-16). — agente (D-06) —
  2026-11-16 — Block 4.
- **D-40 (del agente, Block 4).** Rótulos del PDF elegidos por el implementador ("Razón social:", "Punto de venta:
  0003", "Número: 00000125", "Fecha de emisión:", "CAE:", "Vencimiento del CAE:", "IVA contenido: $ 315,00"), CUIT del
  emisor mostrado con guiones, título del documento solo con el comprobante y sin autor ni asunto; la falla interna se
  prueba con una URL demasiado larga para QRCoder. — agente (D-06) — 2026-11-16 — Block 4.
- **D-41 (del agente, Block 5).** Un solo título de 404 ("No se encontró la factura.") para presupuesto inexistente o
  sin factura, en el detalle y en el PDF, sin revelar si el presupuesto existe; "faltan datos del emisor" es "no hay
  fila" (la entidad no admite filas incompletas, D-19), y una fila corrupta da 500; los importes del detalle van en
  pesos decimales como `FacturaEmitida`. — agente (D-06), aceptado por el verificador y el auditor — 2026-11-16 —
  Block 5.
- **D-42 (del agente, Block 6).** El rango Desde > Hasta de la búsqueda de facturas lo valida solo el dominio
  (`FiltroFacturas.Crear`) y `BuscarFacturas` lo traduce al error junto a `desde`; el controlador valida en el borde
  textos, fechas y página y corta antes. Si llega un rango invertido junto con otro error de borde, el 400 trae solo el
  del borde y el de `desde` aparece al corregir (FEAT-006 los acumula). Se prefirió no duplicar la regla en dos capas;
  queda fijado con una prueba y la web valida el rango antes de pedir (Block 7). `OpticaDbContext.cs` se modificó fuera
  de la lista del Block 6 para mapear `digitos_comprobante` con `HasDbFunction`. — agente (D-06), señalado por el
  verificador y el auditor — 2026-11-16 — Block 6.
- **D-43 (del agente, Block 7).** `BuscarFacturas.tsx` es casi una copia de `BuscarPresupuestos.tsx` (estado, pedido,
  paginado, reparto de errores con mensaje general fijo) y `DetalleFactura.tsx` repite la carga por número de ruta de
  `usePresupuestoDeLaRuta`: se acepta la duplicación en este ticket (extraer un hook de búsqueda paginada o de recurso
  por número tocaría pantallas de otros tickets) y queda como deuda para un ticket posterior. `guardarPdf` sí se extrae
  a `web/src/componentes/guardarPdf.ts`, fuera de la lista de archivos del Block 7. El nombre del PDF se arma en la web
  con la misma regla que la Api (`GenerarPdfFactura`), en lugar de leer `Content-Disposition`. — agente (D-06), señalado
  por el verificador y el auditor — 2026-11-16 — Block 7.
- **D-44 (desvío de proceso, Block 7).** En la ronda 1, el implementador escribió `web/src/App.test.tsx` con un script
  por shell en lugar de Edit, lo que saltea el control previo de escritura. Lo autoinformó; el verificador y el auditor
  revisaron el diff (85 líneas agregadas, 0 borradas, solo lo del bloque) y confirmaron que no debilita
  `Pantallas_NingunaMuestraDatosArca`. En la ronda 2 se pidió no repetirlo. — agente — 2026-11-16 — Block 7.

- **D-45 (del agente, Block 8).** Cobertura fuera del e2e: AC-16 (producción sin leyenda) queda en .NET
  (`Pdf_Produccion_SinLeyenda`, `ReglasComprobanteImpreso_CondicionYValidezFiscal`) porque el harness fuerza
  `Arca:Entorno = Simulado` y AGENTS.md prohíbe emitir contra producción; la fecha fija 30/10/2026 de AC-08 y los
  rangos de marzo de 2026 de AC-18 y AC-22 se prueban en e2e con la fecha del día (una factura con CAE no se modifica) y
  con fechas fijas en .NET (`Pdf_ComprobanteYFecha_B000300000125`, `Repositorio_ApellidoYDesde_SoloGonzalez20`,
  `Filtro_DesdePosteriorAHasta_Rechazado`); el logo de AC-06 queda en `Pdf_LogoYColores` y el escaneo del QR de AC-12
  sigue manual (D-16). `ARCA_POR_ARCHIVO` suma `facturas-busqueda` (B-3 en 124 y C-3 en 34560, para tener en la misma
  base la Factura B 0003-00000125 y la C 0003-00034561) además de `facturas-pdf`; la validación de contadores se agregó
  al harness y la de claves inexistentes es una prueba de `npm run test:unidad`. `e2e/tests/articulos.spec.ts:220` también pasa a
  `exact: true` (faltaba en la lista de D-35 y rompía el harness). — agente (D-06) — 2026-11-16 —
  Block 8.

## Pendientes pedidos por el usuario
- **P-01 (del usuario, 2026-11-16).** Aplicar la marca real de la óptica: la carpeta `Marca/` en la raíz del
  repositorio (ignorada por git, no se versiona) trae `branding.json` con el color primario #0903A0 sobre fondo #FFFFFF,
  `logo.png` (recorte del logo) y `Marca.jpg` (original). Se aplica en la instalación configurando
  `Optica:Marca:Directorio` con la ruta de esa carpeta (FEAT-001a; `docs/instalacion.md` §1); el formato ya es el que
  lee `ProveedorMarcaArchivo` (el campo `logo` usa el último segmento, `logo.png`). Sin la clave, la app y el PDF usan
  la marca de ejemplo. Es dato de la empresa: no se sube al repositorio.
