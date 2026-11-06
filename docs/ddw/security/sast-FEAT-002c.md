# SAST FEAT-002c

| Field | Value |
|-------|-------|
| Ticket | FEAT-002c |
| Alcance | `git diff master...42a4451`: 17 archivos de `src/` (uno retirado), `web/src` (4), `e2e/tests` (1), pruebas .NET |
| Tools | revisión manual del diff con búsquedas de patrones (grep), `npm audit` (web y e2e), `dotnet list Optica.sln package --vulnerable --include-transitive` |
| Date | 2026-11-06 |

| Rule | Verdict | Notes |
|---|---|---|
| F-SAST-01 | ✅ | sin secretos embebidos en el diff; la única contraseña es la de prueba del e2e (`e2e-config-recalculo-1`), válida solo en la base temporal del harness |
| F-SAST-02 | ✅ | consultas de EF Core parametrizadas; sin `FromSqlRaw`/`ExecuteSqlRaw`; la siembra de pruebas (`tests/Compartido/SiembraCatalogo.cs:96`) usa un comando preparado con parámetros tipados |
| F-SAST-03 | ✅ | nada llega a `Process.Start` ni a otro ejecutor de comandos |
| F-SAST-04 | ✅ | sin `eval`, `new Function` ni deserialización insegura (System.Text.Json con tipos cerrados) |
| F-SAST-05 | ✅ | ninguna entrada del usuario forma rutas de archivo |
| F-SAST-06 | ✅ | React escapa el texto de los mensajes (D-06, D-01); sin `innerHTML` ni `dangerouslySetInnerHTML` |
| F-SAST-07 | ✅ | sin pedidos salientes guiados por la entrada del usuario |
| F-SAST-08 | ✅ | el ticket no toca criptografía ni contraseñas |
| F-SAST-09 | ✅ | sin cambios de modo de depuración; `EnableSensitiveDataLogging` sigue desactivado |
| F-SAST-10 | ✅ | el registro de la grabación (`GrabarConfiguracionConRecalculo.cs:172`) lleva solo los parámetros de configuración, la cantidad y la fecha UTC, sin códigos ni importes de artículos (D-11); `SolicitudConfiguracion`, `ConfiguracionVigente` y `ConfiguracionGrabada` no imprimen sus miembros (D-28); `BaseTemporal` ya no escribe la ruta temporal (D-30) |
| F-SAST-11 | ✅ | no hay superficie de subida de archivos |
| F-SAST-12 | ✅ | `POST /api/configuracion` sigue detrás de la sesión con cookie `SameSite=Strict`, sin CORS y con JSON obligatorio (FEAT-001c) |
| F-SAST-13 | ✅ | `npm audit` en web/ y e2e/: 0 vulnerabilidades; sin dependencias nuevas |
| F-SAST-14 | ✅ | `[JsonNumberHandling(Strict)]` y `[RequestSizeLimit]` en el POST; validación de la configuración antes de leer; el lector web exige un entero seguro ≥ 0 en `preciosCambiados` |
| F-SAST-15 | ✅ | el 400 lleva textos fijos con códigos autonuméricos (D-12); fallas inesperadas y anidamientos terminan en el 500 genérico sin traza; `ConflictoAlConfirmarException` sigue sin causa |
| F-SAST-16 | ✅ | `dotnet list package --vulnerable --include-transitive`: ningún proyecto tiene paquetes vulnerables |
| F-SAST-17 | ✅ | sin rutas de código dinámicas |

## Suppressions
None.

## Notas de datos (informativas, no son vulnerabilidades del diff)
- El registro de la grabación de la configuración llega al Registro de eventos de Windows con la alícuota, la
  condición fiscal y el tope (información comercial confidencial de la empresa, R-04 de FEAT-001c, aceptado):
  conviene restringir el acceso a ese registro en la instalación.
- Fuera del alcance de este ticket: el harness e2e mostró una vez el ingreso con la contraseña correcta durante el
  bloqueo de 5 minutos (FEAT-001b, causa probable: retroceso del reloj del sistema); se trata en un ticket FIX
  aparte por decisión del usuario.

Total: 17 clean, 0 vulnerabilities (0 critical, 0 high)
Result: PASSED
