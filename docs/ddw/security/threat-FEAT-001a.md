# Threat model FEAT-001a: Ingreso, sesión y marca

| Field | Value |
|-------|-------|
| Ticket | FEAT-001a |
| Spec | docs/ddw/specs/spec-FEAT-001a.md |
| Tier | FEATURE |
| Date | 2026-10-30 |

## Components
| Component | Source in the spec |
|---|---|
| `src/Optica.Infrastructure/Persistencia/OpticaDbContext.cs` | Block 1 |
| `src/Optica.Application/Acceso/DefinirContrasena.cs` | Block 2 |
| `src/Optica.Infrastructure/Seguridad/HasherContrasenaPbkdf2.cs` | Block 2 |
| `src/Optica.Api/Program.cs` | Block 3 |
| `src/Optica.Api/Controllers/AccesoController.cs` | Block 3 |
| `src/Optica.Infrastructure/Marca/ProveedorMarcaArchivo.cs` | Block 4 |
| `web/src/api/cliente.ts` | Block 5 |

## Trust boundaries
- Navegador de la operadora (PC del local o PC de la red local) → API ASP.NET Core
  (`/api/acceso/*`, `/api/sesion`, `/api/marca`, `/api/marca/logo`): cruza la contraseña en texto
  claro dentro del cuerpo de `POST /api/acceso/definir` y `POST /api/acceso/ingresar`, y la cookie
  de sesión en cada pedido.
- API → archivo SQLite `optica.db` en `Optica:Datos:Directorio`: cruza el hash de la contraseña y
  su sal.
- API → carpeta de marca en `Optica:Marca:Directorio` (fuera del repositorio, escrita por quien
  instala): cruza `branding.json` y el archivo de logo, que la API devuelve sin sesión.
- Repositorio público en GitHub → código y marca de ejemplo: lo que se versiona es visible para
  cualquiera; la marca real y la base nunca cruzan esta frontera.

## STRIDE analysis
### `src/Optica.Infrastructure/Persistencia/OpticaDbContext.cs`
- **Spoofing:** no autentica a nadie; solo lo usa la API dentro del mismo proceso, no expone
  conexión de red (SQLite es un archivo local).
- **Tampering:** quien tenga acceso de escritura al archivo `optica.db` puede reemplazar el hash y
  entrar; mitigado porque la base vive en `Optica:Datos:Directorio`, fuera del árbol del código y
  del repo, en una carpeta del perfil de Windows de la PC del local (R-07). Las escrituras usan
  consultas parametrizadas de EF Core, sin SQL armado a mano.
- **Repudiation:** no aplica a este componente; los intentos de ingreso los registran en la tabla
  `EventoAcceso` los casos de uso de Application (`DefinirContrasena`, `IngresarConContrasena`,
  `CerrarSesion`), y `AccesoController` solo delega (R-06).
- **Information Disclosure:** la base guarda solo el hash PBKDF2 y la sal, nunca la contraseña
  (AC-06 del PRD). En FEAT-001a no existen todavía datos de clientes.
- **Denial of Service:** SQLite en modo WAL con una sola fila de credencial; la carga es mínima.
  Una base corrupta impide ingresar: lo cubre el backup del PRD 09 de DISC-001.
- **Elevation of Privilege:** la base no tiene usuarios ni roles; no hay nada que escalar.

### `src/Optica.Application/Acceso/DefinirContrasena.cs`
- **Spoofing:** el endpoint de definición es anónimo por necesidad (todavía no hay contraseña).
  Un tercero en la red local podría definirla antes que las dueñas en una instalación nueva;
  mitigado porque el caso de uso solo define si no existe ninguna credencial, dentro de una
  transacción con restricción de fila única, y el procedimiento de instalación define la
  contraseña en el mismo acto de instalar (R-02).
- **Tampering:** una vez definida, el caso de uso rechaza toda nueva definición; cambiarla es
  FEAT-001b y exige la contraseña actual.
- **Repudiation:** la definición queda registrada en la tabla `EventoAcceso` con fecha y hora,
  sin la contraseña ni la IP (R-06).
- **Information Disclosure:** el error de longitud mínima no revela nada más que la regla pública
  de 8 caracteres.
- **Denial of Service:** el hash se calcula una sola vez por definición; los pedidos posteriores
  se rechazan antes de calcular nada porque la credencial ya existe.
- **Elevation of Privilege:** definir la contraseña no inicia sesión por sí solo con privilegios
  extra; todas las sesiones tienen el mismo nivel.

### `src/Optica.Infrastructure/Seguridad/HasherContrasenaPbkdf2.cs`
- **Spoofing:** la verificación compara en tiempo constante (`CryptographicOperations.FixedTimeEquals`),
  así que el tiempo de respuesta no ayuda a adivinar la contraseña.
- **Tampering:** el formato guardado incluye algoritmo, iteraciones, sal y hash; un valor
  manipulado o mal formado se rechaza como contraseña incorrecta, nunca como acierto.
- **Repudiation:** no aplica a este componente.
- **Information Disclosure:** sal aleatoria de 16 bytes por contraseña y PBKDF2-HMAC-SHA256 con
  600.000 iteraciones: una base robada no permite tablas precalculadas y encarece la fuerza bruta
  fuera de línea (R-03).
- **Denial of Service:** cada verificación cuesta del orden de cientos de milisegundos de CPU; un
  ataque de pedidos masivos a `ingresar` podría saturar la PC del local; mitigado con el limitador
  de concurrencia del endpoint (R-05).
- **Elevation of Privilege:** no aplica; no emite credenciales ni roles.

### `src/Optica.Api/Program.cs`
- **Spoofing:** la sesión es una cookie de autenticación de ASP.NET Core firmada y cifrada con Data
  Protection; las claves de Data Protection se guardan en `Optica:Datos:Directorio`, fuera del
  repo. Una cookie fabricada no pasa la verificación.
- **Tampering:** cookie `HttpOnly`, `Secure`, `SameSite=Strict`; la API no habilita CORS y los
  endpoints que modifican estado exigen `Content-Type: application/json`, así que un formulario de
  otro sitio no puede enviar pedidos con la sesión de la operadora (R-04).
- **Repudiation:** los eventos de autenticación se guardan en la tabla `EventoAcceso` de la base y
  el log de consola registra errores y advertencias, nunca contraseñas ni cookies.
- **Information Disclosure:** la política de autorización por defecto exige sesión en todo endpoint
  salvo la lista blanca anónima; un endpoint nuevo queda protegido sin que nadie se acuerde de
  marcarlo (R-01). Sin sesión, la API responde 401 sin cuerpo con datos, en lugar de redirigir.
  Fuera de desarrollo no se muestra la página de excepciones: los errores salen como
  ProblemDetails sin traza. La app escucha solo por HTTPS (R-08).
- **Denial of Service:** la expiración deslizante de 60 minutos limita la vida de una cookie
  robada; el limitador de concurrencia se registra acá (R-05).
- **Elevation of Privilege:** hay un solo nivel de acceso; la lista blanca anónima es explícita y
  está cubierta por una prueba que falla si aparece un endpoint anónimo nuevo (R-01).

### `src/Optica.Api/Controllers/AccesoController.cs`
- **Spoofing:** adivinar la contraseña por fuerza bruta contra `POST /api/acceso/ingresar`. En
  FEAT-001a no hay todavía datos de clientes detrás del ingreso y el bloqueo por 5 intentos llega
  en FEAT-001b, que debe estar implementado antes de cargar datos reales (R-03).
- **Tampering:** el cuerpo se valida como JSON con un único campo `contrasena`; cualquier otro
  formato se rechaza con 400 antes de llegar al caso de uso.
- **Repudiation:** cada intento de ingreso y cada definición se registran con tipo, resultado,
  fecha y hora, nunca con la contraseña ni la IP (R-06).
- **Information Disclosure:** `GET /api/acceso/estado` informa sin sesión si ya hay contraseña
  definida, necesario para mostrar la pantalla correcta (AC-03); no revela nada más. El mensaje de
  ingreso fallido es único, sin pistas sobre la contraseña.
- **Denial of Service:** pedidos masivos a `ingresar` fuerzan cálculos PBKDF2; limitador de
  concurrencia (R-05).
- **Elevation of Privilege:** `salir` invalida la cookie; un ingreso exitoso emite una cookie nueva,
  nunca reutiliza una anterior (sin fijación de sesión).

### `src/Optica.Infrastructure/Marca/ProveedorMarcaArchivo.cs`
- **Spoofing:** no aplica; lee archivos locales.
- **Tampering:** `branding.json` lo escribe quien instala; un campo `logo` manipulado como
  `../../optica.db` haría que `GET /api/marca/logo` sirviera sin sesión cualquier archivo de la PC.
  Mitigado: la ruta del logo se resuelve y debe quedar dentro de la carpeta de marca, con extensión
  `.png`, `.jpg` o `.svg`; si no, se usa la marca de ejemplo (R-09).
- **Repudiation:** no aplica.
- **Information Disclosure:** la marca es pública por naturaleza (se muestra en la pantalla de
  ingreso). La marca real de la óptica no se versiona: `Marca/` queda en `.gitignore` y el repo
  solo trae `marca-ejemplo/` (AC-08 del PRD).
- **Denial of Service:** un logo enorme se devolvería en cada carga; se limita a 1 MB y, si lo
  supera, se usa la marca de ejemplo.
- **Elevation of Privilege:** un logo SVG con scripts se sirve con `Content-Type: image/svg+xml` y
  cabecera `Content-Security-Policy: sandbox`, y la SPA lo muestra con `<img>`, donde los scripts
  no se ejecutan (R-10).

### `web/src/api/cliente.ts`
- **Spoofing:** el cliente nunca guarda la contraseña ni la sesión; la cookie `HttpOnly` no es
  accesible desde JavaScript.
- **Tampering:** todas las llamadas van al mismo origen con `credentials: 'same-origin'` y
  `Content-Type: application/json`.
- **Repudiation:** no aplica al cliente.
- **Information Disclosure:** ante un 401 el cliente descarta lo que tenga en memoria y navega a
  `/ingreso`, de modo que una sesión vencida no deja datos a la vista (AC-01 y AC-05 del PRD).
  React escapa el texto que renderiza; la API envía `Content-Security-Policy: default-src 'self'`
  para la SPA (R-11).
- **Denial of Service:** no aplica al cliente.
- **Elevation of Privilege:** la protección de rutas en la SPA es solo de experiencia de uso; la
  autorización real la hace la API en cada pedido (R-01).

## Data classification
| Data | Class | At rest | In transit |
|---|---|---|---|
| contraseña de acceso | credentials | nunca se guarda; solo hash PBKDF2-HMAC-SHA256, 600.000 iteraciones, sal aleatoria de 16 bytes | HTTPS (TLS 1.2 o superior) dentro de la red local |
| hash y sal de la contraseña | credentials | archivo SQLite en `Optica:Datos:Directorio`, fuera del repo, en el perfil de Windows de la PC del local | no sale de la PC |
| cookie de sesión | credentials | no se guarda en el servidor; cifrada y firmada con Data Protection, claves en `Optica:Datos:Directorio` | HTTPS; cookie `HttpOnly`, `Secure`, `SameSite=Strict` |
| claves de Data Protection | credentials | archivo en `Optica:Datos:Directorio`, fuera del repo; en Windows, protegidas con DPAPI | no sale de la PC |
| marca (logo y colores) | public | carpeta `Optica:Marca:Directorio` fuera del repo; la de ejemplo, versionada | HTTPS |
| eventos de acceso (fecha, hora, tipo, resultado) | public | tabla `EventoAcceso` en el archivo SQLite de `Optica:Datos:Directorio`; sin contraseñas, sin IP ni otros datos personales (decisión de las dueñas, 2026-10-30) | no sale de la PC |

## Risks and mitigations
| ID | Risk | STRIDE | Likelihood | Impact | Mitigation |
|---|---|---|---|---|---|
| R-01 | un endpoint nuevo queda accesible sin sesión porque nadie lo marcó | E | M | H | política de autorización por defecto que exige sesión; lista blanca anónima explícita de 5 rutas; prueba de integración que enumera los endpoints y falla si aparece uno anónimo fuera de la lista |
| R-02 | un tercero en la red local define la contraseña antes que las dueñas en una instalación nueva | S | L | H | la definición solo procede si no existe credencial (transacción y fila única); la instalación define la contraseña en el mismo acto |
| R-03 | fuerza bruta en línea contra `ingresar`, o fuera de línea sobre una base robada | S | M | M | PBKDF2 con 600.000 iteraciones y sal; en FEAT-001a no hay datos de clientes detrás del ingreso; el bloqueo de FEAT-001b es requisito antes de cargar datos reales |
| R-04 | falsificación de pedidos desde otro sitio (CSRF) con la cookie de la operadora | T | L | M | `SameSite=Strict`, sin CORS, `Content-Type: application/json` obligatorio en los POST |
| R-05 | saturación de CPU con pedidos masivos a `ingresar` | D | L | M | limitador de concurrencia de ASP.NET Core en `ingresar` y `definir`: 2 en paralelo, cola de 10, el resto 429 |
| R-06 | no se puede reconstruir quién intentó entrar | R | M | L | tabla `EventoAcceso` con tipo, resultado, fecha y hora, sin contraseñas ni IP; la contraseña es compartida por diseño (PRD-001) y, por decisión de las dueñas (2026-10-30), no se guarda la IP para no almacenar datos personales: el registro dice cuándo y con qué resultado, no desde qué PC |
| R-07 | copia o reemplazo del archivo de base desde la PC | T | L | M | base fuera del árbol del código y del repo, en el perfil de Windows; `.gitignore` excluye `*.db`; la protección física de la PC queda a cargo del negocio (PRD FEAT-001b) |
| R-08 | contraseña y cookie viajan sin cifrar por la red local | I | M | H | la app escucha solo por HTTPS con un certificado generado al instalar; en desarrollo, certificado de desarrollo de .NET; cookie `Secure` |
| R-09 | `branding.json` manipulado hace servir sin sesión un archivo arbitrario de la PC | I | L | H | la ruta del logo debe resolverse dentro de la carpeta de marca, con extensión permitida y menos de 1 MB; si no, marca de ejemplo |
| R-10 | logo SVG con scripts ejecutado en el navegador | E | L | M | servido como `image/svg+xml` con `Content-Security-Policy: sandbox` y mostrado con `<img>` |
| R-11 | inyección de scripts en la SPA | E | L | H | React escapa el contenido; `Content-Security-Policy: default-src 'self'` en las respuestas de la SPA; sin `dangerouslySetInnerHTML` |

## Supply chain
Dependencias nuevas, todas de fuentes oficiales y fijadas con lockfile: NuGet
`Microsoft.EntityFrameworkCore.Sqlite` y `Microsoft.EntityFrameworkCore.Design` 10.x (Microsoft),
`xunit`, `Microsoft.NET.Test.Sdk` y `Microsoft.AspNetCore.Mvc.Testing` para pruebas; npm `react`,
`react-dom`, `react-router`, `vite`, `typescript`, `vitest`, `@testing-library/react`, `jsdom` y
`@playwright/test` para pruebas e2e. El hash de contraseñas usa `Rfc2898DeriveBytes.Pbkdf2` de
.NET, sin paquetes de terceros. `package-lock.json` se versiona y se instala con `npm ci`; el paso
de SAST de CODE revisa las dependencias.

## Availability
El vector de DoS es el cálculo de PBKDF2 en `ingresar` y `definir`, cubierto por el limitador de
concurrencia (R-05). El sistema no se publica en internet: solo es accesible desde la PC o la red
local del negocio (PRD-001, Dependencias), así que un ataque volumétrico externo no lo alcanza.
