# SAST FEAT-008

| Field | Value |
|-------|-------|
| Ticket | FEAT-008 |
| Tools | revisión del diff contra la base de la rama `6727d98` (47 archivos de producción de `src/`, `web/src/` y `e2e/`) con grep de patrones (secretos, SQL crudo, exec/eval, HTML, cripto, rutas, TLS) y lectura de los endpoints, los logs, las respuestas 409 y el archivo del simulador; `dotnet list Optica.sln package --vulnerable --include-transitive`; `npm audit` en `web/` y en `e2e/`; revisiones de seguridad por bloque de ddw-module-verifier y ddw-arch-auditor; threat model `docs/ddw/security/threat-FEAT-008.md` |
| Date | 2026-11-16 |

| Rule | Verdict | Notes |
|---|---|---|
| F-SAST-01 | ✅ | sin secretos embebidos en el diff; las opciones nuevas (`Arca:EsperaVerificacionSegundos` y las del simulador) no son secretos; `appsettings.json` no cambió de criterio (D-13 y D-26 de FEAT-007 siguen vigentes) |
| F-SAST-02 | ✅ | sin SQL armado: los repositorios nuevos (`EmisionPendienteRepository`, `ObservacionEmisionRepository`, `DescarteEmisionRepository`) usan EF Core parametrizado, sin `FromSqlRaw`, `ExecuteSqlRaw` ni `CommandText`; la migración `RecuperacionEmisiones` es SQL estático (tablas y triggers) sin datos de entrada |
| F-SAST-03 | ✅ | nada de entrada del usuario llega a exec/spawn: el único `spawn` sigue siendo el del harness (`e2e/servidor.mjs:104`), con `dotnet` y una ruta fija; los `.exec(` del diff son de expresiones regulares |
| F-SAST-04 | ✅ | sin `eval`, `new Function` ni deserialización insegura; el archivo del simulador se lee con `JsonDocument` con `MaxDepth = 4` (`src/Optica.Infrastructure/Arca/SimuladorArca.cs:442`) |
| F-SAST-05 | ✅ | sin rutas armadas con entrada del usuario: el archivo del simulador es el directorio de datos más un nombre fijo (`SimuladorArca.cs:87`); la escritura va a un temporal con `FileMode.CreateNew` y `File.Move` (`SimuladorArca.cs:640-646`) |
| F-SAST-06 | ✅ | sin `innerHTML` ni `dangerouslySetInnerHTML` en `web/src`; la observación, el comprobante, los importes y la descripción de un rechazo se muestran como texto de React y se validan con lectores estrictos en `web/src/api/cliente.ts` |
| F-SAST-07 | ✅ | la consulta `FECompConsultar` y `FECompUltimoAutorizado` usan las mismas URL constantes por entorno de FEAT-007 (`ServiciosArca.cs`); sin URL configurables desde la web, sin `ServerCertificateCustomValidationCallback` |
| F-SAST-08 | ✅ | sin cripto nueva; sin MD5, SHA1, DES, ECB ni `new Random()` en el diff |
| F-SAST-09 | ✅ | sin modo debug nuevo; el entorno por defecto sigue siendo Simulado |
| F-SAST-10 | ✅ | los logs nuevos llevan solo presupuesto, entorno, tipo, punto de venta, número, CAE, códigos de ARCA, nombre de archivo y el tipo de la falla; los `{Motivo}` son textos fijos o valores de enum (`EmitirFactura.cs:79`, `ProcesoEmision.cs:199`, `ProcesoEmision.cs:357`, `VerificarEmision.cs:85`, `VerificarEmision.cs:270`, `VerificarEmision.cs:355`); no se adjuntan excepciones (`ProcesoEmision.cs:44`, `ProcesoEmision.cs:275`); el simulador no guarda el DNI; `Program.cs` prohíbe HttpLogging de cuerpos (D-32) |
| F-SAST-11 | ✅ | sin superficie de carga de archivos nueva |
| F-SAST-12 | ✅ | las rutas nuevas (`POST /api/presupuestos/{numero}/factura/verificacion`, `POST /api/presupuestos/{numero}/factura/descarte`, `GET /api/facturacion/emisiones-pendientes`) usan la sesión con cookie y la política por defecto (fuera de `RutasAnonimas`), exigen `Content-Type: application/json` en los POST (`LimitesDelCuerpo`, D-33) y tope de cuerpo con `RequestSizeLimit`; `no-store` en todo `/api` |
| F-SAST-13 | ✅ | `dotnet list package --vulnerable --include-transitive`: los 8 proyectos sin paquetes vulnerables; `npm audit` en `web/` y `e2e/`: 0 vulnerabilidades |
| F-SAST-14 | ✅ | validación en el borde: número de presupuesto `long` por la ruta, POST sin cuerpo leído; espera de la verificación validada al arrancar contra el plazo de ARCA más el margen (D-31); respuestas de la consulta de ARCA validadas campo por campo (resultado, total, CAE, fechas, punto de venta y tipo); el archivo del simulador rechazado por encima de 256 KB (`SimuladorArca.cs:335`, `SimuladorArca.cs:436`) |
| F-SAST-15 | ✅ | los 409 de la verificación y del descarte llevan título fijo y solo los datos de su motivo (`PresupuestosController.cs`, `ConflictoDeLaVerificacion`); un resultado no previsto lanza y el manejador responde 500 genérico; la descripción de un rechazo de ARCA va con el DNI enmascarado (`ProcesoEmision.cs:314`, D-34) |
| F-SAST-16 | ✅ | sin dependencias nuevas ni cambios en `*.csproj`, `package.json` o los lockfiles en este ticket |
| F-SAST-17 | ✅ | sin rutas de código dinámico |

## Suppressions
None.

## Notas de datos personales y de la empresa
- La descripción de un rechazo de ARCA puede repetir datos de la solicitud: se devuelve en el 409 con el DNI del
  receptor y el del cliente enmascarados (D-34) y no va al log. Si ARCA devolviera otro dato personal en ese texto
  (nombre, domicilio), llegaría a la pantalla; hoy la solicitud de Factura B no manda otros datos personales.
- Siguen vigentes los riesgos ya aceptados de FEAT-007: DNI sin cifrar en `optica.db` y en los backups (R-13) y la
  clave del certificado como variable de entorno de usuario.

Total: 17 clean, 0 vulnerabilities (0 critical, 0 high)
Result: PASSED
