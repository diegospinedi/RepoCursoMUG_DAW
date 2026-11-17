# SAST FEAT-010

| Field | Value |
|-------|-------|
| Ticket | FEAT-010 |
| Tools | Revisión estática del diff `7330d05..HEAD` por ddw-sec-auditor (lectura del código contra §4 del catálogo, el modelo de amenazas R-01 a R-10 y las decisiones D-01 a D-38); `npm audit` en `web/` y `e2e/`; `dotnet list package --vulnerable --include-transitive` |
| Date | 2026-11-17 |

Alcance: el código de producción del ticket en `src/` (dominio, casos de uso, infraestructura y Api del backup y la
restauración, migración `Backup`), `web/src` (sección "Backup" de la configuración y aviso en Inicio) y el harness
`e2e/*.mjs` (crea y borra carpetas de backup temporales). El ticket no agrega ni cambia paquetes (`*.csproj`,
`package.json` y lockfiles sin cambios en el diff).

| Rule | Verdict | Notes |
|---|---|---|
| F-SAST-01 | ✅ | Sin credenciales en el diff; `src/Optica.Api/appsettings.json:11` solo suma el nivel de log de `Optica.Application.Backup` |
| F-SAST-02 | ✅ | Solo se interpolan constantes del código (`RestauradorSqlite.cs`, `TablaFactura`/`TablaHistorial`); valores parametrizados (`$ultima`, `$tabla`); PRAGMA literales en `VerificacionSqlite.cs`; `HistorialBackupRepository.cs` con LINQ; SQL estático en la migración `20261116200000_Backup.cs` |
| F-SAST-03 | ✅ | El código de producción no lanza procesos; el harness lanza `dotnet` con argumentos fijos, como antes |
| F-SAST-04 | ✅ | Cuerpo del POST: record de un solo `string?` con `JsonNumberHandling.Strict` (`ConfiguracionController.cs:96`); `info.json` de Dropbox con `JsonDocument`, tope de 64 KB y tipos validados; la copia de SQLite se abre en solo lectura con `trusted_schema = OFF` |
| F-SAST-05 | ✅ | Ruta del POST: rechazo de UNC, `\\?\`, `\\.\`, `//`, red y tipo desconocido antes y después de `GetFullPath` (`InspectorUbicaciones.cs:74-124`), se graba normalizada; nombres de copia armados desde `DateOnly` (`CopiaBackup.cs`); retención y `.parcial` solo en el primer nivel y con el patrón exacto (`AlmacenCopiasArchivo.cs:88-128`); argumento de `restaurar-backup` en solo lectura, con nombre exacto y sin rutas UNC ni de dispositivo (`RestaurarBackup.cs`, `FormaRuta.cs`, H-02 corregido); borrado del harness solo de `optica-e2e-backup-<puerto>` bajo bases propias, sin seguir enlaces y del mismo usuario (`e2e/compilacion.mjs`, `e2e/servidor.mjs`) |
| F-SAST-06 | ✅ | `SeccionBackup.tsx` e `Inicio.tsx` muestran todo por JSX, sin `dangerouslySetInnerHTML`; la ruta no va a la URL, al storage ni a la consola |
| F-SAST-07 | ✅ | Sin pedidos HTTP salientes nuevos |
| F-SAST-08 | ✅ | Sin criptografía nueva; la restauración reutiliza `IDatosLocales.RenovarClaves()` (`RestaurarBackup.cs:203`) |
| F-SAST-09 | ✅ | Sin `UseDeveloperExceptionPage`, `EnableSensitiveDataLogging` ni `EnableDetailedErrors`; nivel por defecto `Information` |
| F-SAST-10 | ✅ | Logs con `[LoggerMessage]`: motivo, paso, tipo de excepción y fecha UTC, nunca la ruta ni `ex.Message` (`HacerBackup.cs:308-330`, `GrabarUbicacionBackup.cs:212-215`, `ProgramadorBackup.cs:193-212`); `LogCritical` del arranque con texto fijo; `SolicitudUbicacionBackup` con `PrintMembers => false` (`ConfiguracionController.cs:96-99`); sin datos de clientes |
| F-SAST-11 | ✅ | Sin subida de archivos: el POST recibe un texto y la copia a restaurar se indica por consola (FR-14, AC-15) |
| F-SAST-12 | ✅ | `POST /api/configuracion/backup` exige sesión con cookie `HttpOnly`, `Secure` y `SameSite=Strict` (`Program.cs:267-269`); `[FromBody]` responde 415 a todo lo que no sea JSON |
| F-SAST-13 | ✅ | `npm audit` en `web/` y `e2e/`: 0 vulnerabilidades; `dotnet list package --vulnerable --include-transitive`: sin paquetes vulnerables; el ticket no cambia paquetes |
| F-SAST-14 | ✅ | Cuerpo de hasta 2 KB (`ConfiguracionController.cs:256`); ruta recortada, sin caracteres de control, UTF-16 válido, hasta 260 caracteres y absoluta (`UbicacionBackup.cs:410-415`), revalidada sobre la forma normalizada (`GrabarUbicacionBackup.cs:147`); cuerpo ilegible → 400 con texto fijo; la web valida lo mismo |
| F-SAST-15 | ✅ | Respuestas 400 con textos fijos sin la ruta (`GrabarUbicacionBackup.cs:17-32`); 500 por `ManejadorErroresNoControlados`; la consola de `restaurar-backup` muestra solo el tipo de la excepción, el nombre con el patrón fijo y `anterior\…` relativa (`ComandoRestaurarBackup.cs:85-160`) |
| F-SAST-16 | ✅ | Sin CVE Medium en las auditorías de dependencias |
| F-SAST-17 | ⚠️ | Low, corregido: los triggers de protección de una copia se verificaban solo por nombre (`RestauradorSqlite.cs:200-214`); ahora se compara también su texto normalizado (`TriggersDeProteccion.cs`, H-01, D-38). Riesgo residual informativo: triggers o vistas de más en una copia armada a mano y `trusted_schema = OFF` fuera de las conexiones de la copia (`FabricaConexion.cs:186`), que exigen acceso a la consola de la PC |

## Hallazgos

- **H-01 — Low (F-SAST-17), corregido.** `src/Optica.Infrastructure/Backup/RestauradorSqlite.cs:200` — una copia con
  `TR_Factura_Inmutable` con otro cuerpo pasaba la verificación. Se compara el SQL normalizado de los 17 triggers contra
  el texto canónico, atado a las migraciones por prueba; la copia o el temporal con otro cuerpo se rechaza como ajena.
- **H-02 — Info (F-SAST-05), corregido.** `src/Optica.Application/Backup/RestaurarBackup.cs:116` — el argumento de
  `restaurar-backup` aceptaba rutas UNC (posible autenticación SMB hacia un servidor ajeno). Se rechazan las rutas UNC y
  de dispositivo por la forma del texto, sin abrirlas.
- **H-03 — Info (F-SAST-05).** `src/Optica.Infrastructure/Backup/InspectorUbicaciones.cs:17` — una unión NTFS creada
  después de validar la carpeta podría desviar las escrituras; acotado a archivos con el patrón exacto y limitación
  documentada en D-17.
- **H-04 — Info (F-SAST-14).** `src/Optica.Api/Controllers/ConfiguracionController.cs:96` — campos JSON desconocidos
  se ignoran; sin impacto con un único campo; registrado en D-38.
- **H-05 — Info, alerta de datos personales.** `src/Optica.Infrastructure/Backup/RestauradorSqlite.cs:338` y
  `src/Optica.Application/Backup/HacerBackup.cs:272` — la base completa (DNI, domicilio, teléfono y email de clientes)
  queda sin cifrar en las copias (R-06, aceptado por las dueñas, D-04), en `anterior/` después de restaurar y en un
  `.parcial` que no se pudo borrar; la guía pide guardar `anterior/` bajo llave y el `.parcial` se borra en el backup
  siguiente.

## Suppressions
None.

Total: 16 clean, 1 warning (Low, corregido), 0 vulnerabilities (0 critical, 0 high)
Result: PASSED
