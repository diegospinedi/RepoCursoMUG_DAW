# Spec FEAT-001a: Ingreso, sesión y marca

| Field | Value |
|-------|-------|
| Ticket | FEAT-001a |
| PRD | docs/ddw/prd/prd-FEAT-001a.md |
| Tier | FEATURE |
| Date | 2026-10-30 |
| Spec loops | 3 |
| Loops since last human decision | 0 |

## Summary
Primer código del proyecto: una solución .NET 10 en capas (Domain, Application, Infrastructure,
Api) con SQLite vía EF Core 10, y una SPA React 19 + Vite que la Api sirve en producción. El
acceso usa una única contraseña guardada como hash PBKDF2-SHA256 con sal, y la sesión es una
cookie de autenticación de ASP.NET Core (`HttpOnly`, `Secure`, `SameSite=Strict`) que vence tras
60 minutos sin pedidos. Toda la Api exige sesión salvo una lista blanca anónima de cinco rutas. La
marca se lee de una carpeta configurada fuera del repositorio; si no hay una válida, se usa la
marca de ejemplo versionada en `marca-ejemplo/`. Las mitigaciones R-01 a R-11 de
`docs/ddw/security/threat-FEAT-001a.md` están incorporadas en los bloques; R-02 además en el
procedimiento de instalación de Block 6.

Criterio de ubicación de contratos: los contratos de persistencia (repositorios, registro de
eventos, unidad de trabajo de dominio) van en Domain; los contratos de servicios técnicos que usa un
caso de uso (hash de contraseñas, proveedor de marca, unidad de trabajo) van en Application. Los
controllers solo invocan casos de uso de Application.

## Coverage: PRD → blocks
| Requirement | Covered by |
|---|---|
| FR-01 | Block 3, Block 5 |
| FR-02 | Block 2, Block 3, Block 5 |
| FR-03 | Block 2, Block 3, Block 5 |
| FR-04 | Block 3, Block 5 |
| FR-05 | Block 1, Block 2 |
| FR-06 | Block 4, Block 5 |
| FR-07 | Block 1, Block 4 |
| FR-08 | Block 3, Block 5 |
| NFR-01 | Block 2 (PoliticaContrasena, mínimo 8) |
| NFR-02 | Block 3 (expiración de 60 minutos renovada en cada pedido) |
| NFR-03 | Strategy: Block 6 corre la suite e2e en los canales `chrome` y `msedge` de Playwright (versión estable instalada); la versión inmediata anterior de cada navegador se verifica a mano en VERIFY con la misma suite, documentando las versiones probadas |
| NFR-04 | Block 3 (política por defecto con sesión y prueba que enumera todos los endpoints) |

## Dependencies between blocks
Block 1 → Block 2 → Block 3 → Block 4 → Block 5 → Block 6. Block 4 solo necesita Block 1 y la Api
de Block 3; Block 5 consume las Api de Block 3 y Block 4; Block 6 necesita todo lo anterior.

## Block 1 — Esqueleto de la solución y persistencia

**Files**
- `Optica.sln` (new) — solución con los cuatro proyectos y los cuatro de pruebas
- `src/Optica.Domain/Optica.Domain.csproj` (new) — net10.0, sin dependencias
- `src/Optica.Application/Optica.Application.csproj` (new) — net10.0, referencia a Domain
- `src/Optica.Infrastructure/Optica.Infrastructure.csproj` (new) — net10.0, referencias a Domain y
  Application; paquetes `Microsoft.EntityFrameworkCore.Sqlite` y `Microsoft.EntityFrameworkCore.Design` 10.x
- `src/Optica.Api/Optica.Api.csproj` (new) — net10.0 web, referencias a Application e
  Infrastructure
- `src/Optica.Infrastructure/Persistencia/OpticaDbContext.cs` (new) — DbContext con las tablas
  `CredencialAcceso` y `EventoAcceso`
- `src/Optica.Infrastructure/Persistencia/FabricaConexion.cs` (new) — arma la cadena de conexión
  desde `Optica:Datos:Directorio`, activa `PRAGMA foreign_keys = ON` y `journal_mode = WAL`
- `src/Optica.Infrastructure/Persistencia/Migraciones/` (new) — migración versionada `Inicial`
- `src/Optica.Infrastructure/ConfiguracionInfraestructura.cs` (new) — extensión de DI
  `AgregarInfraestructura` y aplicación de migraciones al iniciar
- `src/Optica.Api/appsettings.json` (new) — sin secretos ni rutas de la PC del local; directorios
  vacíos, que toman los valores por defecto
- `tests/Optica.Domain.Tests/`, `tests/Optica.Application.Tests/`,
  `tests/Optica.Infrastructure.Tests/`, `tests/Optica.Api.Tests/` (new) — proyectos xUnit
- `tests/Optica.Infrastructure.Tests/BaseTemporal.cs` (new) — base SQLite en un directorio
  temporal por prueba, borrada al terminar
- `.gitignore` (modified) — agrega `/Marca/` (con barra inicial, para no ignorar las carpetas de
  código llamadas `Marca`), `bin/`, `obj/`, `TestResults/`, `coverage/`, `node_modules/`,
  `web/dist/`, `src/Optica.Api/wwwroot/`, `e2e/test-results/`, `e2e/playwright-report/`

**Logic**
Crea la estructura en capas de `AGENTS.md`: Domain sin dependencias, Application depende de
Domain, Infrastructure implementa los contratos y Api es la raíz de composición. La base se guarda
en `Optica:Datos:Directorio`; si está vacío, en
`Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)/OpticaSistema`, que
resuelve a una ruta absoluta fuera del árbol del código tanto en Windows como en Linux/WSL. Al
iniciar, la Api aplica las migraciones pendientes. Implementa el almacenamiento del hash de FR-05 y
deja fuera del repositorio la marca real (FR-07, AC-08).

El cambio de `.gitignore` es el primer commit de CODE, y hasta entonces todo `git add` se hace por
ruta explícita: la carpeta local `Marca/` con la marca real todavía no está ignorada.

Rollback: la migración `Inicial` tiene su método `Down`, que elimina `EventoAcceso` y
`CredencialAcceso`. Como es la primera migración y en esta parte no hay datos de clientes,
revertirla equivale a volver a una base vacía; en una PC con datos, antes de revertir se hace el
backup con `VACUUM INTO` (nunca una copia del archivo, por el modo WAL).

**Data model**
- `CredencialAcceso`: `Id` INTEGER PK con `CHECK (Id = 1)` (a lo sumo una fila), `Hash` TEXT NOT
  NULL (formato `PBKDF2-SHA256$<iteraciones>$<sal base64>$<hash base64>`), `DefinidaEn` TEXT NOT
  NULL (fecha y hora UTC ISO 8601).
- `EventoAcceso`: `Id` INTEGER PK autoincremental, `FechaHora` TEXT NOT NULL (UTC), `Tipo` TEXT NOT
  NULL (`definicion`, `ingreso`, `salida`), `Resultado` TEXT NOT NULL (`exito`, `rechazo`);
  índice sobre `FechaHora`. No guarda contraseñas ni la IP de origen ni ningún otro dato personal,
  y no se purga (decisión de las dueñas, 2026-10-30).

**Input validation**
- `Optica:Datos:Directorio`: ruta absoluta o vacía; si no existe, se crea; si no se puede crear o
  escribir, la Api no arranca.

**Error handling**
- Directorio de datos inválido o sin permiso de escritura — la Api no arranca y registra un error
  que nombra la ruta y la clave de configuración a corregir.
- Segunda fila en `CredencialAcceso` — la base la rechaza por la restricción `CHECK (Id = 1)` / PK
  duplicada; el repositorio lo traduce a conflicto (lo usa Block 2).

**Required tests**
- [ ] `Migracion_CreaTablasConRestricciones` — la base temporal queda con ambas tablas, FK activas y
      modo WAL — validates AC-06
- [ ] `DirectorioDatos_Invalido_ImpideArrancar` — una ruta sin permiso de escritura produce error al
      iniciar (sad path)
- [ ] `CredencialAcceso_SegundaFila_RechazadaComoDuplicado` — insertar `Id = 2` o repetir `Id = 1`
      es rechazado por la base (sad path)
- [ ] `DirectorioDatos_PorDefecto_RutaAbsolutaFueraDelRepositorio` — sin configuración, la ruta
      resuelta es absoluta y no está dentro del directorio del repositorio

**Completion criterion**
`dotnet build Optica.sln` compila sin advertencias tratadas como error y las cuatro pruebas pasan.

## Block 2 — Dominio y casos de uso de la contraseña

**Files**
- `src/Optica.Domain/Acceso/CredencialAcceso.cs` (new) — entidad con el hash y la fecha de
  definición
- `src/Optica.Domain/Acceso/PoliticaContrasena.cs` (new) — invariante de longitud: mínimo 8,
  máximo 128
- `src/Optica.Domain/Acceso/ICredencialAccesoRepository.cs` (new) — contrato de persistencia
- `src/Optica.Domain/Acceso/IRegistroEventosAcceso.cs` (new) — contrato del registro de eventos
- `src/Optica.Application/Acceso/IHasherContrasena.cs` (new) — contrato de hash y verificación
- `src/Optica.Application/Comun/IUnidadDeTrabajo.cs` (new) — confirma en una sola transacción lo
  que registraron los repositorios
- `src/Optica.Application/Comun/ErrorValidacion.cs` (new) — campo afectado y acción correctiva
- `src/Optica.Application/Comun/Resultado.cs` (new) — éxito o errores de validación/conflicto
- `src/Optica.Application/Acceso/ConsultarEstadoAcceso.cs` (new) — informa si hay contraseña
- `src/Optica.Application/Acceso/DefinirContrasena.cs` (new) — define la contraseña si no existe
  y registra el evento
- `src/Optica.Application/Acceso/IngresarConContrasena.cs` (new) — verifica la contraseña,
  registra el evento y devuelve `Exito`, `SinCredencial` o `Rechazo`
- `src/Optica.Application/Acceso/CerrarSesion.cs` (new) — registra el evento de salida
- `src/Optica.Infrastructure/Seguridad/HasherContrasenaPbkdf2.cs` (new) — PBKDF2-HMAC-SHA256,
  600.000 iteraciones, sal aleatoria de 16 bytes, comparación en tiempo constante
- `src/Optica.Infrastructure/Persistencia/CredencialAccesoRepository.cs` (new) — implementación EF
- `src/Optica.Infrastructure/Persistencia/RegistroEventosAccesoEf.cs` (new) — agrega filas a
  `EventoAcceso`
- `src/Optica.Infrastructure/Persistencia/UnidadDeTrabajoEf.cs` (new) — un único `SaveChanges`
  del `OpticaDbContext` compartido, dentro de una transacción

**Logic**
Implementa FR-02, FR-03 y FR-05, y toda la lógica de acceso que la Api solo delega.
`PoliticaContrasena` hace cumplir en el dominio el mínimo de 8 y el máximo de 128 caracteres.
`DefinirContrasena` valida la política (FR-03); si ya existe una credencial devuelve conflicto sin
calcular el hash; si no, agrega la credencial y el evento `definicion` y los confirma juntos con
`IUnidadDeTrabajo`, de modo que no queda una credencial sin evento ni al revés.
`IngresarConContrasena` devuelve `SinCredencial` si no hay contraseña, `Rechazo` si no coincide
(un hash mal formado cuenta como no coincidente) y `Exito` si coincide, y en los dos últimos casos
registra el evento `ingreso` con su resultado. `CerrarSesion` registra el evento `salida`. Los
casos de uso dependen solo de contratos de Domain y Application.

**Data model**
- Reuses `CredencialAcceso` and `EventoAcceso` from Block 1 — no schema change; constraints as
  declared there: `CHECK (Id = 1)`, `Hash` NOT NULL, `DefinidaEn` NOT NULL, índice sobre
  `FechaHora`.

**Input validation**
- contraseña: texto, mínimo 8 caracteres, máximo 128; se cuenta en caracteres, sin recortar
  espacios.

**Error handling**
- Contraseña de menos de 8 caracteres — `ErrorValidacion` con campo `contrasena` y acción
  "Ingresá una contraseña de 8 caracteres o más"; no se guarda nada.
- Contraseña de más de 128 caracteres — `ErrorValidacion` con campo `contrasena` y acción
  "Ingresá una contraseña de 128 caracteres o menos"; no se guarda nada.
- Ya existe una contraseña — resultado de conflicto; no se calcula el hash ni se modifica la
  credencial.
- Hash guardado mal formado — la verificación devuelve falso; nunca acierto.
- Ingreso sin contraseña definida — resultado `SinCredencial`; no se registra evento.
- Contraseña incorrecta al ingresar — resultado `Rechazo` y evento `ingreso` con resultado
  `rechazo`.
- Falla al confirmar la unidad de trabajo — no queda guardada ni la credencial ni el evento.

**Required tests**
- [ ] `DefinirContrasena_De8Caracteres_GuardaSoloHash` — el valor guardado no contiene la
      contraseña y verifica correctamente — validates AC-06
- [ ] `DefinirContrasena_De7Caracteres_RechazadaPorInvalida` — devuelve error con campo y acción —
      validates AC-04 (sad path)
- [ ] `DefinirContrasena_De129Caracteres_RechazadaPorInvalida` — devuelve error con campo y acción
      (sad path)
- [ ] `DefinirContrasena_YaDefinida_ConflictoSinModificar` — el segundo intento es rechazado y el
      hash queda igual (sad path)
- [ ] `Hasher_HashMalFormado_InvalidoDevuelveFalso` — un hash manipulado nunca verifica (sad
      path)
- [ ] `IngresarConContrasena_SinCredencial_RechazadoSinEvento` — (sad path)
- [ ] `IngresarConContrasena_Incorrecta_RechazadaYEventoRegistrado` — (sad path)
- [ ] `DefinirContrasena_ErrorAlConfirmar_NoGuardaCredencialNiEvento` — con la unidad de trabajo
      forzada a fallar, la base queda sin credencial y sin evento (sad path)
- [ ] `IngresarConContrasena_Correcta_ExitoYEventoRegistrado`
- [ ] `PoliticaContrasena_Limites8y128` — 8 y 128 válidos; 7 y 129 inválidos en el dominio
- [ ] `Hasher_MismaContrasena_SalesDistintas` — dos hashes de la misma contraseña difieren

**Completion criterion**
Las once pruebas pasan con repositorios controlados (Application), sin base (Domain) y con base
temporal (Infrastructure).

## Block 3 — Api de acceso, sesión y protección

**Files**
- `src/Optica.Api/Program.cs` (new) — raíz de composición: autenticación por cookie, política de
  autorización por defecto con sesión, limitador de concurrencia, ProblemDetails, HTTPS, Data
  Protection con claves en `Optica:Datos:Directorio/claves`, protegidas con
  `ProtectKeysWithDpapi()` cuando el sistema operativo es Windows (en Linux/WSL solo se usa para
  pruebas, con claves en un directorio temporal). El certificado HTTPS se toma del almacén de
  certificados de Windows (`CurrentUser\My`, sujeto `CN=OpticaSistema`) mediante
  `Kestrel:Certificates:Default:Store` y `:Subject`; ni el certificado ni ninguna contraseña van en
  `appsettings.json` ni en el repositorio. En desarrollo se usa el certificado de desarrollo de .NET
- `src/Optica.Api/Acceso/RenovacionSesion.cs` (new) — renueva la cookie en cada pedido autenticado
  para que venza a los 60 minutos del último pedido
- `src/Optica.Api/Acceso/RutasAnonimas.cs` (new) — la lista blanca de rutas anónimas
- `src/Optica.Api/Controllers/AccesoController.cs` (new) — estado, definir, ingresar, salir
- `src/Optica.Api/Controllers/SesionController.cs` (new) — sonda protegida de sesión
- `src/Optica.Api/Errores/RespuestasValidacion.cs` (new) — traduce `ErrorValidacion` a
  ValidationProblemDetails (campo y acción)
- `tests/Optica.Api.Tests/FabricaApi.cs` (new) — WebApplicationFactory con base temporal y reloj
  controlable (`TimeProvider`)

**Logic**
Implementa FR-01, FR-04 y FR-08 en el servidor. La cookie se emite al definir la contraseña y al
ingresar, con `ExpireTimeSpan` de 60 minutos renovada en cada pedido autenticado (FR-04, NFR-02);
un pedido sin cookie válida recibe 401 sin cuerpo con datos, nunca una redirección (FR-01, NFR-04).
Las únicas rutas anónimas son `GET /api/acceso/estado`, `POST /api/acceso/definir`,
`POST /api/acceso/ingresar`, `GET /api/marca` y `GET /api/marca/logo` (R-01). `ingresar` y
`definir` tienen un limitador de concurrencia de 2 con cola de 10 (R-05). `AccesoController` solo
delega en `ConsultarEstadoAcceso`, `DefinirContrasena`, `IngresarConContrasena` y `CerrarSesion`
(Block 2), que registran cada evento en `EventoAcceso` (R-06), y traduce sus resultados a respuestas HTTP y a la emisión o el borrado de la cookie. Los POST exigen
`Content-Type: application/json` y no hay CORS (R-04).

**API contract**
- Method + path: `GET /api/acceso/estado`; `POST /api/acceso/definir`; `POST /api/acceso/ingresar`;
  `POST /api/acceso/salir`; `GET /api/sesion`
- Request: `definir` e `ingresar`: `{ "contrasena": string }`; `estado`, `salir` y `sesion`: sin
  cuerpo
- Response: `estado`: 200 `{ "contrasenaDefinida": boolean }`; `definir`: 201 sin cuerpo, con
  cookie de sesión; `ingresar`: 204 con cookie de sesión; `salir`: 204 y la cookie se borra;
  `sesion`: 204
- Error codes: 400 (ValidationProblemDetails con `errors.contrasena`), 401 (sin sesión o
  contraseña incorrecta en `ingresar`, mensaje único), 409 (`definir` con contraseña ya definida),
  415 (Content-Type distinto de JSON), 429 (limitador)
- Auth: `estado`, `definir` e `ingresar` anónimos; `salir` y `sesion` con sesión; todo endpoint no
  listado exige sesión por la política por defecto

**Input validation**
- `contrasena`: string JSON, requerida, 8 a 128 caracteres (Block 2).
- Cuerpo: `Content-Type: application/json`, máximo 4 KB.

**Error handling**
- Pedido sin sesión a un endpoint protegido — 401 sin cuerpo con datos.
- Contraseña incorrecta en `ingresar` — 401 con el mensaje único "La contraseña no es correcta.
  Volvé a ingresarla."; evento de rechazo registrado.
- `ingresar` sin contraseña definida — 409 con acción "Definí la contraseña primero".
- Contraseña faltante o inválida en `definir` o `ingresar` — 400 con campo y acción.
- `definir` con contraseña ya definida — 409; no se modifica nada.
- Cuerpo que no es JSON — 415.
- Limitador saturado — 429.

**Required tests**
- [ ] `EndpointsProtegidos_SinSesion_NoAutorizado401` — recorre todos los endpoints registrados y
      exige 401 en los que no están en `RutasAnonimas` — validates AC-02 (sad path)
- [ ] `RutasAnonimas_SonExactamenteCinco` — la lista blanca no crece sin que falle una prueba
- [ ] `Estado_SinContrasena_DevuelveNoDefinida` — validates AC-03
- [ ] `Definir_De7Caracteres_Invalido400ConCampoYAccion` — validates AC-04 (sad path)
- [ ] `Definir_YaDefinida_Conflicto409` — (sad path)
- [ ] `Ingresar_ContrasenaIncorrecta_Rechazado401YEventoRegistrado` — (sad path)
- [ ] `Ingresar_SinContrasenaDefinida_Conflicto409` — (sad path)
- [ ] `Ingresar_ContrasenaFaltante_Invalido400` — (sad path)
- [ ] `Post_ConTextoPlano_Rechazado415` — (sad path)
- [ ] `Ingresar_LimitadorSaturado_Rechazado429` — (sad path)
- [ ] `Sesion_60MinutosSinActividad_NoAutorizado` — con el reloj adelantado 60 minutos tras el
      último pedido, `GET /api/sesion` responde 401; a los 59 minutos responde 204 — validates AC-05
- [ ] `Sesion_PedidoIntermedio_RenuevaVencimiento` — un pedido a los 40 minutos extiende el
      vencimiento a 100 minutos desde el inicio
- [ ] `Cookie_EsHttpOnlySecureSameSiteStrict`

**Completion criterion**
Las trece pruebas pasan y ningún endpoint fuera de la lista blanca responde sin sesión.

## Block 4 — Marca instalada y marca de ejemplo

**Files**
- `src/Optica.Domain/Marca/IdentidadVisual.cs` (new) — colores primario y de fondo, logo y si es de
  ejemplo (el tipo no se llama `Marca` para no chocar con el namespace `Optica.Domain.Marca`)
- `src/Optica.Application/Marca/IProveedorMarca.cs` (new) — contrato
- `src/Optica.Application/Marca/ObtenerMarca.cs` (new) — caso de uso
- `src/Optica.Infrastructure/Marca/ProveedorMarcaArchivo.cs` (new) — lee `branding.json` y el logo
  de `Optica:Marca:Directorio`; si falta o es inválida, usa `marca-ejemplo/`
- `src/Optica.Api/Controllers/MarcaController.cs` (new) — `GET /api/marca`, `GET /api/marca/logo`
- `marca-ejemplo/branding.json` (new) — marca genérica con el mismo esquema que la real
- `marca-ejemplo/logo.svg` (new) — logo genérico "Óptica" en gris, sin la marca real
- `src/Optica.Api/Optica.Api.csproj` (modified) — copia `marca-ejemplo/**` a la salida

**Logic**
Implementa FR-06 y FR-07. El esquema de `branding.json` es el de la marca real:
`{ "logo": string, "colores": { "primario": "#RRGGBB", "fondo": "#RRGGBB" } }`; los demás campos se
ignoran. Del campo `logo` se toma solo el nombre del archivo y se busca dentro de la carpeta de
marca, así `"Marca/logo.png"` funciona y una ruta con `..` no sale de la carpeta (R-09). Si la
carpeta configurada no existe, no tiene `branding.json`, tiene colores inválidos o el logo no
existe, supera 1 MB o tiene una extensión distinta de `.png`, `.jpg` o `.svg`, se usa la marca de
ejemplo completa y se registra una advertencia. Un logo `.svg` se sirve con
`Content-Security-Policy: sandbox` (R-10).

**API contract**
- Method + path: `GET /api/marca`; `GET /api/marca/logo`
- Request: sin parámetros ni cuerpo
- Response: `/api/marca`: 200 `{ "primario": "#RRGGBB", "fondo": "#RRGGBB", "esEjemplo": boolean }`;
  `/api/marca/logo`: 200 con el archivo y `Content-Type` `image/png`, `image/jpeg` o
  `image/svg+xml`
- Error codes: 500 solo si tampoco la marca de ejemplo se puede leer (instalación dañada)
- Auth: anónimos (la marca se muestra en la pantalla de ingreso; no contiene datos protegidos)

**Data model**
- Marca (archivo `branding.json` en la carpeta de marca; no se persiste en la base, sin cambios de
  esquema): `colores.primario` NOT NULL, formato `#RRGGBB`; `colores.fondo` NOT NULL, formato
  `#RRGGBB`; `logo` NOT NULL, extensión `.png`, `.jpg` o `.svg`, máximo 1 MB; default: la marca de
  ejemplo de `marca-ejemplo/`.

**Input validation**
- `colores.primario` y `colores.fondo`: `^#[0-9A-Fa-f]{6}$`.
- `logo`: nombre de archivo con extensión `.png`, `.jpg` o `.svg`, tamaño máximo 1 MB, dentro de
  la carpeta de marca.

**Error handling**
- Carpeta configurada inexistente o sin `branding.json` — marca de ejemplo y advertencia en el log.
- `branding.json` inválido o con colores con formato inválido — marca de ejemplo y advertencia.
- Logo faltante, de más de 1 MB o con extensión no permitida — marca de ejemplo y advertencia.
- Campo `logo` con `..` — solo se usa el nombre de archivo; nunca se lee fuera de la carpeta.
- Marca de ejemplo ilegible — 500 y error en el log.

**Required tests**
- [ ] `Marca_Instalada_DevuelveColoresYLogoDeLaOptica` — con una carpeta de prueba con primario
      #0903A0 y fondo #FFFFFF, `GET /api/marca` los devuelve y `esEjemplo` es falso — validates
      AC-07
- [ ] `Marca_CarpetaFaltante_UsaEjemplo` — sin carpeta configurada devuelve la marca de ejemplo —
      validates AC-08 (sad path)
- [ ] `Marca_ColoresInvalidos_UsaEjemplo` — `"primario": "azul"` usa la de ejemplo (sad path)
- [ ] `Marca_LogoFaltanteOGrande_UsaEjemplo` — logo inexistente o de 2 MB usa la de ejemplo (sad
      path)
- [ ] `Marca_LogoConPuntosPuntos_RechazadoFueraDeCarpeta` — `"logo": "../secreto.png"` nunca sirve
      un archivo fuera de la carpeta (sad path)
- [ ] `Marca_EjemploIlegible_Error500` — (sad path)
- [ ] `MarcaLogo_Svg_ConCspSandbox` — el SVG se sirve con `Content-Security-Policy: sandbox`
- [ ] `Marca_Anonima_SinSesionResponde200`

**Completion criterion**
Las ocho pruebas pasan; con la carpeta `Marca/` local configurada, la Api devuelve #0903A0 y el
logo PNG de la óptica.

## Block 5 — Interfaz web: definir contraseña, ingresar, sesión y marca

**Files**
- `web/package.json`, `web/package-lock.json` (new) — React 19, React Router, Vite, TypeScript,
  Vitest, Testing Library, jsdom; scripts `dev`, `build`, `test`, `test:coverage`
- `web/vite.config.ts` (new) — proxy de `/api` a la Api en desarrollo; configuración de Vitest
- `web/tsconfig.json`, `web/index.html` (new)
- `web/src/main.tsx`, `web/src/App.tsx` (new) — rutas `/definir-contrasena`, `/ingreso`, `/`
- `web/src/api/cliente.ts` (new) — `fetch` al mismo origen con JSON; ante 401 limpia el estado y
  navega a `/ingreso`
- `web/src/marca/MarcaProvider.tsx` (new) — pide `/api/marca` y fija `--color-primario` y
  `--color-fondo`
- `web/src/componentes/Encabezado.tsx` (new) — logo `<img src="/api/marca/logo">` en todas las
  pantallas
- `web/src/componentes/CampoContrasena.tsx` (new) — campo con mensaje de error junto al campo
- `web/src/paginas/DefinirContrasena.tsx`, `web/src/paginas/Ingreso.tsx`,
  `web/src/paginas/Inicio.tsx` (new)
- `web/src/rutas/Arranque.tsx` (new) — decide la pantalla inicial con `estado` y `sesion`
- `web/src/estilos.css` (new) — estilos con las variables de marca
- `web/src/**/*.test.tsx` (new) — pruebas de componentes

**Logic**
Implementa FR-01, FR-02, FR-03, FR-04, FR-06 y FR-08 en la interfaz. Al abrir, `Arranque` consulta
`/api/acceso/estado`: sin contraseña muestra `DefinirContrasena` (FR-02); con contraseña consulta
`/api/sesion` y muestra `Inicio` o `Ingreso` (FR-01). La validación de 8 caracteres se hace también
en el navegador y el mensaje del servidor se muestra junto al campo (FR-03, FR-08). Cualquier 401
lleva a `/ingreso` sin dejar datos en pantalla (FR-04). `Inicio` es una pantalla mínima con
"Sesión iniciada" y el botón Salir; las pantallas con datos llegan en los PRDs siguientes. No se usa
`dangerouslySetInnerHTML` (R-11).

**API contract**
- Method + path: consume `GET /api/acceso/estado`, `POST /api/acceso/definir`,
  `POST /api/acceso/ingresar`, `POST /api/acceso/salir`, `GET /api/sesion`, `GET /api/marca`,
  `GET /api/marca/logo` (Block 3 y Block 4); no crea endpoints
- Request: `{ "contrasena": string }` en definir e ingresar
- Response: según Block 3 y Block 4
- Error codes: maneja 400 (mensaje junto al campo), 401 (a `/ingreso`), 409, 429 (mensaje general)
- Auth: cookie de sesión enviada por el navegador; el cliente no la lee

**Input validation**
- contraseña: mínimo 8 caracteres antes de enviar; el servidor vuelve a validar.

**Error handling**
- Contraseña de menos de 8 caracteres — no se envía; mensaje junto al campo "La contraseña debe
  tener al menos 8 caracteres. Ingresá una más larga."
- Respuesta 400 — mensaje del servidor junto al campo indicado.
- Respuesta 401 en una pantalla protegida — navega a `/ingreso` y descarta el estado.
- Contraseña incorrecta al ingresar (401 en `ingresar`) — mensaje junto al campo.
- Error de red o 5xx — mensaje general "No se pudo conectar con el sistema. Reintentá."

**Required tests**
- [ ] `Arranque_SinContrasena_MuestraDefinir` — validates AC-03
- [ ] `Arranque_SinSesion_MuestraIngreso` — validates AC-01
- [ ] `DefinirContrasena_7Caracteres_RechazadaConMensajeJuntoAlCampo` — validates AC-04 (sad path)
- [ ] `DefinirContrasena_Error400_InvalidoMuestraMensajeDelServidor` — (sad path)
- [ ] `Cliente_401_NoAutorizadoNavegaAIngreso` — validates AC-05 (sad path)
- [ ] `Ingreso_ContrasenaIncorrecta_RechazadaConMensaje` — (sad path)
- [ ] `Cliente_ErrorDeRed_MuestraMensajeGeneral` — (sad path)
- [ ] `Encabezado_MuestraLogoYAplicaColoresDeMarca` — validates AC-07

**Completion criterion**
`npm run build` compila sin errores de tipos y `npm run test:coverage` pasa las ocho pruebas.

## Block 6 — Servir la SPA desde la Api y pruebas de punta a punta

**Files**
- `src/Optica.Api/Program.cs` (modified) — archivos estáticos desde `Optica:Web:Directorio` (por
  defecto `wwwroot`), fallback a `index.html` anónimo para rutas que no son `/api`, cabecera
  `Content-Security-Policy: default-src 'self'` en las respuestas de la SPA
- `e2e/package.json`, `e2e/package-lock.json` (new) — `@playwright/test`; script `test:harness`
- `e2e/playwright.config.ts` (new) — proyectos `chrome` y `msedge`; `webServer` que compila la web
  y levanta la Api con base y marca en directorios temporales
- `e2e/tests/acceso.spec.ts` (new) — recorrido completo
- `docs/instalacion.md` (new) — procedimiento de instalación en la PC del local: directorios de
  datos y de marca fuera del repo, copia de la marca real, certificado HTTPS en el almacén de
  Windows, y definición de la contraseña en el mismo acto de instalar, antes de conectar la PC a la
  red local (R-02)

**Logic**
Cubre FR-01 a FR-08 de punta a punta y la estrategia de NFR-03. La Api sirve la SPA compilada como
un solo desplegable. La suite corre contra una base nueva: muestra la pantalla de definición, rechaza
7 caracteres, define una de 8, entra a Inicio con el logo y los colores, sale, y vuelve a ingresar.
También comprueba que sin sesión una ruta protegida termina en `/ingreso`.

**API contract**
- Method + path: `GET /` y cualquier ruta que no empieza con `/api` → `index.html`
- Request: sin cuerpo
- Response: 200 `text/html` con `Content-Security-Policy: default-src 'self'`
- Error codes: 404 para rutas `/api/*` inexistentes con sesión; 401 sin sesión (Block 3)
- Auth: anónimo para los archivos de la SPA, que no contienen datos; los datos siguen protegidos
  por la Api

**Input validation**
- Sin entrada propia: sirve archivos estáticos; las entradas del usuario se validan en Block 3 y
  Block 5.

**Error handling**
- Directorio de la web inexistente — la Api arranca igual, registra una advertencia y las rutas de
  la SPA responden 404 (la Api sigue funcionando para las pruebas).
- Ruta `/api/*` inexistente — no cae en el fallback de `index.html`: 404 con sesión, 401 sin
  sesión.

**Required tests**
- [ ] `acceso.spec.ts: primera vez define contraseña e ingresa` — validates AC-03, AC-07
- [ ] `acceso.spec.ts: 7 caracteres rechazada con mensaje junto al campo` — validates AC-04 (sad
      path)
- [ ] `acceso.spec.ts: sin sesión una ruta protegida va a ingreso` — validates AC-01 (sad path)
- [ ] `Spa_DirectorioWebFaltante_ApiArrancaY404` — (sad path)
- [ ] `Spa_RutaApiInexistente_NoDevuelveIndexError404` — (sad path)
- [ ] `Spa_RespuestaConCsp`

**Completion criterion**
`npm run test:harness` pasa en los proyectos `chrome` y `msedge`, y las tres pruebas de la Api
pasan.

## Final verification
- `dotnet build Optica.sln` y `dotnet test Optica.sln` pasan.
- En `web/`: `npm ci`, `npm run build` y `npm run test:coverage` pasan.
- En `e2e/`: `npm ci` y `npm run test:harness` pasan en Chrome y Edge estables; la versión
  anterior de cada navegador se verifica a mano en VERIFY (NFR-03).
- `git ls-files Marca` no devuelve nada, `git check-ignore Marca/logo.png` lo ignora y
  `git check-ignore src/Optica.Domain/Marca/IdentidadVisual.cs` no lo ignora (AC-08). Es una
  verificación del repositorio, no de una capa: se corre en VERIFY, no como prueba de un proyecto.
- La suite e2e necesita Chrome y Edge instalados: en esta máquina se corre desde Windows, no desde
  WSL.
