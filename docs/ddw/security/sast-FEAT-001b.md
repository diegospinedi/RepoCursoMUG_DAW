# SAST FEAT-001b

| Field | Value |
|-------|-------|
| Ticket | FEAT-001b |
| Tools | revisión del código por el orquestador (git grep de patrones por categoría sobre los archivos de `src/` y `web/src/` que cambiaron en la rama, sin pruebas ni migraciones generadas); `npm audit` en `web/` y `e2e/`; `dotnet list Optica.sln package --vulnerable --include-transitive`; revisiones independientes por bloque (ddw-module-verifier y ddw-arch-auditor, con mutaciones) |
| Date | 2026-11-03 (re-cierre después del loop correctivo de VERIFY) |
| Alcance | rama `feat/FEAT-001b-bloqueo-gestion-contrasena` completa, desde `master` hasta `04506ca`. Respecto del primer cierre (`e5a035b`), en producción solo cambió `src/Optica.Application/Comun/IUnidadDeTrabajo.cs`: `ConflictoAlConfirmarException` pasa a tener un único constructor `(string mensaje)`, revisado línea por línea. La rama completa abarca los Blocks 1 a 7: bloqueo por intentos, cambio de contraseña (`POST /api/acceso/cambiar`), comando `restablecer-contrasena`, candado de instancia, pantallas web de bloqueo y de cambio, y pruebas de ausencia de datos de ARCA. Sin dependencias nuevas. |

| Rule | Verdict | Notes |
|---|---|---|
| F-SAST-01 | ✅ | sin secretos agregados: ninguna asignación literal de contraseña, token ni cadena de conexión en el diff; las contraseñas de las pruebas y las e2e son constantes sintéticas; `.env`, `*.db`, `*.pfx` y similares siguen en `.gitignore` |
| F-SAST-02 | ✅ | acceso a datos por EF Core con LINQ parametrizado; las dos consultas nuevas en `DatosLocales.cs:65` (`$tabla` como parámetro) y `DatosLocales.cs:76` (interpola la constante privada `TablaHistorial = "__EFMigrationsHistory"`, sin entrada del usuario) |
| F-SAST-03 | ✅ | la Api no lanza procesos; el comando `restablecer-contrasena` lee `args[0]` solo para elegir la rama, y con argumentos extra responde código 2 sin ejecutar nada |
| F-SAST-04 | ✅ | sin deserialización insegura: el pedido de cambio se lee con `System.Text.Json` sobre `SolicitudCambioContrasena` y `LectorContrasenaJson` |
| F-SAST-05 | ✅ | sin entrada del usuario en rutas de archivos: `optica.instancia`, `optica.db` y la carpeta de claves se arman con `Path.Combine` sobre el directorio de datos de la configuración y constantes (`CandadoInstanciaArchivo.cs:36`, `DatosLocales.cs:29,34,40`); `RenovarClaves` solo borra el contenido de esa carpeta de claves |
| F-SAST-06 | ✅ | React escapa toda salida; sin `dangerouslySetInnerHTML`, `innerHTML` ni `eval` en `web/src`; el aviso de `Inicio` toma del estado de la navegación solo un `string` y lo muestra como texto; el 423 muestra un texto fijo, no el `detail` del servidor |
| F-SAST-07 | ✅ | sin pedidos salientes nuevos; ARCA no se usa en este ticket |
| F-SAST-08 | ✅ | el cambio de contraseña reutiliza PBKDF2-HMAC-SHA256 con 600.000 iteraciones y sal nueva (ADR-002); el reemplazo usa el hash como token de concurrencia; sin MD5, SHA1, DES ni ECB |
| F-SAST-09 | ✅ | sin `UseDeveloperExceptionPage`, `EnableSensitiveDataLogging` ni `EnableDetailedErrors`; el host del comando filtra los logs de EF Core a Warning |
| F-SAST-10 | ✅ | ninguna contraseña ni hash llega al log: `ConflictoAlConfirmarException` ya no admite causa (`IUnidadDeTrabajo.cs`, constructor `(string mensaje)`), así que no se le pueden adjuntar los `Entries` de EF con el hash, y una prueba de regresión revisa los constructores públicos y no públicos; `SolicitudCambioContrasena.ToString()` oculta las dos y hay pruebas de log en todos los niveles; los `LogCritical` de `ConfiguracionInfraestructura.cs:77,86,99` y la salida del comando usan solo `RutaVisible`, la clave de configuración, el tipo de la excepción y el código de SQLite, sin adjuntar la excepción; `optica.instancia` queda vacío; en la web, pruebas con mutación confirman que no quedan contraseñas en la consola, el almacenamiento, la URL ni el estado de la navegación |
| F-SAST-11 | ✅ | sin superficie de subida de archivos |
| F-SAST-12 | ✅ | `POST /api/acceso/cambiar` exige sesión (cookie `HttpOnly`, `Secure`, `SameSite=Strict`) y `Content-Type: application/json` (415 si no), sin CORS; el restablecimiento no tiene endpoint (`Restablecimiento_NoExisteEnLaApi_NingunEndpointLoOfrece`) |
| F-SAST-13 | ✅ | `npm audit` en `web/` y `e2e/`: 0 vulnerabilidades; `dotnet list package --vulnerable --include-transitive`: ninguno de los 8 proyectos tiene paquetes vulnerables |
| F-SAST-14 | ✅ | entrada validada en el borde: las dos contraseñas de 8 a 128 unidades NFC con rechazo de Unicode inválido, cuerpo hasta 4 KB (413), JSON obligatorio (415), cuerpo ilegible 400 en el campo correspondiente; el comando compara la confirmación con `Trim()` y comparación ordinal; bloqueo a los 5 rechazos durante 5 minutos (423) con limitador de concurrencia compartido (429) |
| F-SAST-15 | ✅ | los errores devuelven ProblemDetails con textos fijos (400, 409, 423) sin detalles internos; el comando muestra solo el tipo de la excepción y el código de SQLite, sin texto ni traza (`Comando_ErrorDeSqlite_ErrorSinRutaNiExcepcion`) |
| F-SAST-16 | ✅ | sin CVE de severidad media en las dependencias (mismas auditorías que F-SAST-13) y sin paquetes nuevos |
| F-SAST-17 | ✅ | sin `eval`, `new Function` ni ejecución dinámica de código |

## Hallazgos informativos (no bloquean; documentados)

- **Datos personales (R-14, riesgo aceptado por el usuario en PLAN):** los eventos de acceso de las operadoras, que ahora incluyen `bloqueo`, `cambio_contrasena` y `restablecimiento`, se guardan sin límite de tiempo en `optica.db` y en cada backup. `docs/instalacion.md` exige proteger los backups como la base. Hay que definir una política de retención antes de cargar datos reales de clientes.
- **Restablecimiento físico (R-08, aceptado):** quien tenga acceso a la PC con la cuenta de la Api puede restablecer la contraseña. El comando exige confirmación, se niega si la Api está en marcha, registra el evento y corta todas las sesiones renovando las claves.
- **Error en la contraseña actual al cambiarla (R-04, aceptado):** no cuenta para el bloqueo. Con una sesión robada se puede probar la contraseña actual sin bloquear la cuenta, con el límite del limitador de concurrencia.
- **Candado fuera de Windows:** `FileShare.None` es un candado advisory (`flock`) y no es confiable bajo `/mnt/c` en WSL. En la PC del local, con Windows nativo, el bloqueo es obligatorio. En VERIFY falta la corrida manual en Windows de `Comando_ApiEnMarcha_ErrorSinCambios` y `Api_SegundaInstanciaSobreElMismoDirectorio_RechazadaAlArrancar`.
- **`DbUpdateException` que no son conflicto:** `UnidadDeTrabajoEf.ConfirmarAsync` las propaga con sus `Entries`, que pueden traer el hash. Hoy no llegan al log: `ManejadorErroresNoControlados.Describir` escribe solo el tipo, el mensaje y la traza, y `EnableSensitiveDataLogging` y `EnableDetailedErrors` están prohibidos. Queda como restricción en `decisions-FEAT-001b.md`: nunca registrar la excepción completa con un serializador estructurado.
- **Datos de prueba fuera del repo:** las pruebas de la Api dejan en `~/.cache/optica-pruebas` bases con hashes de contraseñas sintéticas, que se limpian después de una hora.

## Suppressions
None.

Total: 17 clean, 0 vulnerabilities (0 critical, 0 high)
Result: PASSED
