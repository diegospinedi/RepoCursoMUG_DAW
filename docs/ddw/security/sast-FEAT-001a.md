# SAST FEAT-001a

| Field | Value |
|-------|-------|
| Ticket | FEAT-001a |
| Tools | revisión del código por el orquestador (git grep de patrones por categoría sobre `src/`, `web/src/`, `e2e/`), `npm audit` (web y e2e), `dotnet list package --vulnerable --include-transitive`; revisiones independientes por bloque (ddw-arch-auditor y ddw-module-verifier, con mutaciones) |
| Date | 2026-11-02 |
| Alcance | rama `feat/FEAT-001a-ingreso-sesion-marca` completa: Blocks 1 a 6 (`src/`, `web/`, `e2e/`, `Directory.Build.props`, `appsettings.json`, `docs/instalacion.md`) |

| Rule | Verdict | Notes |
|---|---|---|
| F-SAST-01 | ✅ | sin secretos en el código: no hay contraseñas, claves ni cadenas de conexión con credenciales; el certificado HTTPS se toma del almacén de Windows y no está en `appsettings.json` (prueba `Appsettings_SinCertificadoNiContrasenasNiHostsAbiertos`); `.env`, `.env.*`, `appsettings.Production.json`, `*.db`, `*.cer`, `*.pfx`, `*.pem` y `/Marca/` están en `.gitignore`; ningún archivo de esos tipos está versionado |
| F-SAST-02 | ✅ | todo acceso a datos por EF Core con LINQ parametrizado; el único `CommandText` (`FabricaConexion.cs:179,187`) es la constante de PRAGMAs, sin entrada del usuario |
| F-SAST-03 | ✅ | la Api no lanza procesos; `spawn`/`spawnSync` solo en el servidor de pruebas e2e (`e2e/servidor.mjs:77,101`), con comandos y argumentos fijos, `dotnet` sin intérprete de comandos y `npm.cmd` con argumentos fijos (13+12 pruebas en `e2e/compilacion.test.mjs`) |
| F-SAST-04 | ✅ | sin deserialización insegura: solo `System.Text.Json` sobre tipos concretos (`LectorContrasenaJson`, `branding.json`), sin `TypeNameHandling` ni `BinaryFormatter`; XML del SVG con `DtdProcessing.Prohibit` y `XmlResolver = null` |
| F-SAST-05 | ✅ | rutas de archivos acotadas: el logo toma solo el nombre, rechaza `..`, caracteres prohibidos, de control y nombres reservados de Windows, y verifica que quede dentro de la carpeta de marca (`ProveedorMarcaArchivo.EstaEnLaCarpeta`); la SPA sirve con `PhysicalFileProvider` y una guarda que impide apuntar a la carpeta de la aplicación, la raíz del contenido o la carpeta de datos; pruebas de path traversal en `SpaTests` y `ProveedorMarcaArchivoTests` |
| F-SAST-06 | ✅ | React escapa toda salida; no hay `dangerouslySetInnerHTML`, `innerHTML` ni `eval` en `web/src`; colores de marca validados como `#RRGGBB` antes de aplicarlos por CSSOM; logo por `<img>` y SVG servido con CSP `default-src 'none'; style-src 'unsafe-inline'; sandbox` y `nosniff`; SPA con CSP `default-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'; object-src 'none'` |
| F-SAST-07 | ✅ | sin pedidos salientes guiados por el usuario; el proxy de Vite (`web/vite.config.ts`) es solo de desarrollo, con destino fijo por variable de entorno y TLS verificado fuera de localhost |
| F-SAST-08 | ✅ | contraseña con PBKDF2-HMAC-SHA256, 600.000 iteraciones, sal de 16 bytes y comparación en tiempo constante (`HasherContrasenaPbkdf2.cs:81`, ADR-002); sin MD5, SHA1, DES ni ECB |
| F-SAST-09 | ✅ | sin `UseDeveloperExceptionPage`; errores no controlados con ProblemDetails genérico; `EnableSensitiveDataLogging` y `EnableDetailedErrors` nunca activados; build Release sin sourcemaps en la web |
| F-SAST-10 | ✅ | la contraseña no llega al log en ningún nivel (`SolicitudContrasena.ToString()` sin el valor; pruebas `Log_*_NoContieneLaContrasena` con captura en Trace); el hash no sale (parámetros de EF enmascarados); las rutas con la carpeta del usuario se ocultan con `RutasPrivadas`; no se adjuntan excepciones crudas; eventos de error de EF en Debug; no se guarda la IP (R-06) |
| F-SAST-11 | ✅ | no hay superficie de subida de archivos: la marca se lee de una carpeta local configurada al instalar |
| F-SAST-12 | ✅ | cookie de sesión `HttpOnly`, `Secure` y `SameSite=Strict`; todo POST exige `Content-Type: application/json` (415 si no), sin CORS; un formulario de otro sitio no puede enviar JSON con la cookie |
| F-SAST-13 | ✅ | `npm audit` en `web/` y `e2e/`: 0 vulnerabilidades; `dotnet list package --vulnerable --include-transitive`: ningún proyecto con paquetes vulnerables |
| F-SAST-14 | ✅ | entrada validada en el borde: contraseña de 8 a 128 unidades NFC con rechazo de Unicode inválido (Application), cuerpo hasta 4 KB (413), `application/json` obligatorio (415), cuerpo ilegible 400; `branding.json` hasta 64 KB y JSON estricto; logo de hasta 1 MB con contenido que corresponde al formato |
| F-SAST-15 | ✅ | los errores devuelven ProblemDetails sin detalle interno (`Pedido_ErrorDeBase_Error500GenericoSinDetalle`); los mensajes de excepción del dominio son fijos y no repiten valores recibidos |
| F-SAST-16 | ✅ | sin CVE de severidad media en las dependencias (mismas auditorías que F-SAST-13) |
| F-SAST-17 | ✅ | sin `eval`, `new Function` ni ejecución dinámica de código |

## Hallazgos informativos (no bloquean; documentados)

- El manejador de errores no controlados registra el tipo, el mensaje y la traza de excepciones que no son de la base, con la carpeta del usuario oculta (`src/Optica.Api/Errores/ManejadorErroresNoControlados.cs:46`). Hoy ningún mensaje lleva datos personales; restricción registrada en `decisions-FEAT-001a.md` para los módulos con datos de clientes (no poner nombre, DNI ni CUIT en mensajes de excepción ni en rutas de la Api).
- `GET /api/marca/logo` es anónimo por diseño: en la carpeta de marca va solo la marca (documentado en `docs/instalacion.md`).
- Hasta que se defina la contraseña, cualquiera que llegue a la Api puede definirla (R-02): `docs/instalacion.md` exige definirla escuchando solo en `localhost` antes de abrir la red. No hay bloqueo por intentos fallidos hasta FEAT-001b: no se cargan datos reales antes.
- Fuera de Windows las claves de Data Protection se guardan sin cifrar (solo pruebas y e2e, en temporales); en la PC del local la Api corre en Windows con DPAPI.
- Las compilaciones Debug conservan rutas del desarrollador y Source Link; Release las elimina (`Directory.Build.props`, verificado por `PublicacionSinRutasTests`). No se distribuyen compilaciones Debug.

## Suppressions
None.

Total: 17 clean, 0 vulnerabilities (0 critical, 0 high)
Result: PASSED
