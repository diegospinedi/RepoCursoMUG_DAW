# SAST FEAT-009

| Field | Value |
|-------|-------|
| Ticket | FEAT-009 |
| Tools | revisión del diff contra la base de la rama `5c47270` (62 archivos de producción de `src/`, `web/src/` y `e2e/`) con grep de patrones (secretos, SQL crudo, exec/eval, HTML, cripto, rutas, log, consola y storage) y lectura de los endpoints nuevos, el PDF, el QR y la búsqueda; `dotnet list Optica.sln package --vulnerable --include-transitive`; `npm audit` en `web/` y en `e2e/`; revisiones de seguridad por bloque de ddw-module-verifier y ddw-arch-auditor; threat model `docs/ddw/security/threat-FEAT-009.md` |
| Date | 2026-11-16 |

| Rule | Verdict | Notes |
|---|---|---|
| F-SAST-01 | ✅ | sin secretos embebidos en el diff; los datos del emisor se cargan desde la pantalla y quedan en la base, no en la configuración; la carpeta `Marca/` con el logo real está en `.gitignore` y no se versiona |
| F-SAST-02 | ✅ | sin SQL armado: `DatosEmisorRepository` y `FacturaRepository.BuscarAsync` usan EF Core parametrizado; la búsqueda usa `instr(plegar(...))` y `instr(digitos_comprobante(...))` con funciones registradas (ADR-011); la migración `DatosEmisor` es `CREATE TABLE` estático |
| F-SAST-03 | ✅ | nada de entrada del usuario llega a exec/spawn; el harness e2e no cambió su `spawn` |
| F-SAST-04 | ✅ | sin `eval`, `new Function` ni deserialización insegura; el JSON del QR se arma con `Utf8JsonWriter` (`src/Optica.Application/Facturacion/QrArca.cs`) |
| F-SAST-05 | ✅ | sin rutas armadas con entrada del usuario; el logo del PDF viene de `ProveedorMarcaArchivo`, que exige que quede dentro de la carpeta de marca; el nombre del PDF descargado sale del comprobante (`factura-B-0003-00000125.pdf`) |
| F-SAST-06 | ✅ | sin `innerHTML` ni `dangerouslySetInnerHTML` en `web/src`; los datos de la factura, el emisor y la búsqueda se muestran como texto de React y se leen con lectores estrictos en `web/src/api/cliente.ts` |
| F-SAST-07 | ✅ | sin llamadas salientes nuevas: el QR solo contiene la URL constante `https://www.afip.gob.ar/fe/qr/` (host y ruta fijos, R-04) y no se consulta desde el sistema |
| F-SAST-08 | ✅ | sin cripto nueva; sin MD5, SHA1, DES, ECB ni `new Random()` en el diff |
| F-SAST-09 | ✅ | sin modo debug nuevo; el entorno por defecto sigue siendo Simulado y el PDF lleva "COMPROBANTE SIMULADO — SIN VALIDEZ FISCAL" en cada página fuera de producción |
| F-SAST-10 | ✅ | el único log nuevo (`GrabarDatosEmisor`) registra la fecha y hora UTC y los nombres de los campos cambiados, nunca sus valores (R-06); `ObtenerFactura`, `GenerarPdfFactura` y `BuscarFacturas` no reciben registrador; los filtros de la búsqueda no van al log (prueba con valores testigo); los tipos con datos tienen `PrintMembers => false` o no sobrescriben `ToString`; el generador de PDF relanza con texto fijo y sin la causa |
| F-SAST-11 | ✅ | sin superficie de carga de archivos |
| F-SAST-12 | ✅ | las rutas nuevas (`GET/POST /api/configuracion/emisor`, `GET /api/presupuestos/{numero}/factura`, `GET /api/presupuestos/{numero}/factura/pdf`, `POST /api/facturacion/busqueda`) usan la sesión con cookie y la política por defecto (fuera de `RutasAnonimas`), exigen JSON en los POST y tienen tope de cuerpo (2 KB y 4 KB); `no-store` en todo `/api` |
| F-SAST-13 | ✅ | `dotnet list package --vulnerable --include-transitive`: los 8 proyectos sin paquetes vulnerables; `npm audit` en `web/` y `e2e/`: 0 vulnerabilidades |
| F-SAST-14 | ✅ | validación en el borde y en el dominio: datos del emisor con CUIT módulo 11, largos y fecha no futura (`DatosEmisor`, `Cuit`); búsqueda con textos de hasta 100 caracteres recortados como el molde y sin UTF-16 inválido, fechas `yyyy-MM-dd` estrictas, página acotada y Desde ≤ Hasta en el dominio; `{numero:long}` en las rutas del detalle y del PDF |
| F-SAST-15 | ✅ | 404 con título fijo sin revelar si el presupuesto existe, 409 `sin_datos_emisor` con título y detalle fijos por helper de conjunto cerrado, 500 genérico sin el texto de la excepción (probado) |
| F-SAST-16 | ✅ | dependencia nueva `QRCoder` 1.6.0 (MIT, ADR-027) solo en Infrastructure, sin `System.Drawing` en los lockfiles, sin avisos conocidos; restauración en modo bloqueado; la Api no la referencia (`Api_NoReferenciaQrCoder`) |
| F-SAST-17 | ✅ | sin rutas de código dinámico |

## Suppressions
None.

## Notas de datos personales y de la empresa
- La búsqueda y el detalle de facturas muestran el Apellido, el Nombre y el DNI del cliente del presupuesto también en
  facturas a Consumidor Final (FR-25, decisión del usuario D-03), con sesión, `no-store` y sin log.
- El PDF lleva el DNI, el Apellido y el Nombre solo si la factura se emitió con DNI; el QR lleva el DNI solo en
  producción (D-23), como exige ARCA; el PDF descargado queda en la PC de la operadora (advertido en
  `docs/instalacion.md`).
- Los datos del emisor pueden ser de una persona física: se guardan en la base sin cifrar como el resto (R-13, ya
  aceptado) y nunca van al log.
- El CUIT de ejemplo del PRD (20-12345678-6) tiene dígito verificador válido y podría ser de una persona real: queda
  solo en las pruebas de AC-03 y AC-04 (D-33).
- Pruebas existentes ajenas a este ticket usan el DNI 30111222, que podría ser de una persona real
  (`SinDatosArcaTests`, `PresupuestosApiTests`): pendiente de reemplazo por uno ficticio en otro ticket.

Total: 17 clean, 0 vulnerabilities (0 critical, 0 high)
Result: PASSED
