# Threat model FEAT-005: Integración continua para los pull requests

| Field | Value |
|-------|-------|
| Ticket | FEAT-005 |
| Spec | docs/ddw/specs/spec-FEAT-005.md |
| Tier | FEATURE |
| Date | 2026-11-07 |

## Components
| Component | Source in the spec |
|---|---|
| `.github/workflows/ci.yml` | Block 2 |
| `e2e/ci.mjs` | Block 1, Block 2, Block 3 |
| `e2e/playwright.config.ts` | Block 3 |
| `Directory.Build.props` | Block 1 |

## Trust boundaries
- Autor del pull request (incluido un fork) → runner de GitHub Actions: el código del pull request se compila y se
  ejecuta en el runner (`dotnet test`, `npm ci`, scripts de `package.json`).
- Runner → registros públicos (npm, NuGet, apt de Ubuntu, CDN de Chrome y Edge): las dependencias y los navegadores
  entran al runner.
- Runner → GitHub: el resultado de la corrida y los registros de cada paso se publican en el pull request.
- Navegadores de la suite e2e → Api local en `https://localhost:<puerto>`: la única red que pueden usar las pruebas.

## STRIDE analysis
### `.github/workflows/ci.yml`
- **Spoofing:** un pull request desde un fork podría hacerse pasar por un cambio confiable para obtener secretos;
  el workflow usa el disparador `pull_request`, que en forks corre sin secretos; `pull_request_target` y
  `workflow_run` quedan rechazados (regla `disparador-inseguro`) y no se referencia ningún secreto ni
  `github.token` (FR-08, regla `secretos`).
- **Tampering:** una acción de terceros cuya etiqueta se mueve podría inyectar código en el runner; las tres
  acciones son oficiales de `actions/`, son las únicas permitidas y se fijan por SHA de 40 caracteres (D-02, regla
  `acciones`) (R-01). Las dependencias npm entran con `npm ci` y la integridad `sha512` del lockfile (regla
  `instalacion`), y las de NuGet con `packages.lock.json` en modo bloqueado (D-04) (R-02).
- **Repudiation:** cada corrida queda en la pestaña Actions con el commit, el autor del pull request y los registros
  de cada paso con nombre (regla `pasos-con-nombre`).
- **Information Disclosure:** los registros de la corrida son visibles para quien ve el repositorio; las pruebas
  usan datos ficticios (DNI y emails de prueba) y la corrida no publica bases, respaldos ni artefactos: la regla
  `acciones` rechaza `upload-artifact` y `cache` (R-04). El `GITHUB_TOKEN` queda con `contents: read` (FR-09) y el
  checkout no lo guarda en `.git/config` (`persist-credentials: false`, regla `credenciales`).
- **Denial of Service:** una corrida colgada o muchos pushes seguidos consumen minutos del runner; `timeout-minutes:
  45` y `concurrency` con `cancel-in-progress` los acotan (NFR-01, NFR-02) (R-03).
- **Elevation of Privilege:** código de un pull request malicioso corre con `sudo` en el runner efímero (necesario
  para instalar navegadores), pero el `GITHUB_TOKEN` solo puede leer y la máquina se descarta al terminar; no puede
  escribir en el repositorio ni en `master` (regla `permisos`).

### `e2e/ci.mjs`
- **Spoofing:** no maneja identidades; lee archivos del propio repositorio.
- **Tampering:** solo lee texto (`ci.yml`, `AGENTS.md`, archivos de prueba) y no escribe nada; un workflow alterado
  para debilitarlo (permiso de escritura, secretos, acción sin SHA) queda rechazado por `revisarWorkflow` en la
  propia corrida.
- **Repudiation:** cada problema nombra la regla y el archivo; queda en la salida de `npm run test:unidad`.
- **Information Disclosure:** no lee secretos ni datos de clientes; solo informa nombres de reglas, archivos y hosts.
- **Denial of Service:** recorre una cantidad acotada de archivos del repositorio con expresiones sin retroceso
  catastrófico; corre en segundos.
- **Elevation of Privilege:** no ejecuta comandos ni evalúa el texto que lee.

### `Directory.Build.props`
- **Spoofing:** no maneja identidades; configura la restauración de NuGet.
- **Tampering:** un paquete reemplazado en el registro o una transitiva distinta no coincide con el hash de
  `packages.lock.json` y la restauración falla con NU1403 (hash del contenido); un proyecto que no coincide con su
  archivo de bloqueo falla con NU1004 en modo bloqueado (R-02).
- **Repudiation:** cada cambio de dependencias queda en el diff del `packages.lock.json` del pull request.
- **Information Disclosure:** los archivos de bloqueo solo listan paquetes públicos y sus hashes.
- **Denial of Service:** un archivo de bloqueo desincronizado corta la corrida en el paso de compilación; se
  corrige restaurando en la PC y commiteando el archivo.
- **Elevation of Privilege:** no ejecuta código; solo define propiedades de MSBuild.

### `e2e/playwright.config.ts`
- **Spoofing:** con la regla de resolución, un nombre de host externo no se resuelve, así una página no puede
  hablar con un servidor que se haga pasar por un servicio (ARCA u otro) (R-05).
- **Tampering:** la Api de cada proyecto usa una base temporal propia que se borra al terminar; no toca datos reales.
- **Repudiation:** Playwright informa cada prueba con su proyecto y navegador.
- **Information Disclosure:** sin la regla, Chrome podría consultar servicios de Google con datos del perfil; con
  ella ningún pedido sale de la máquina (FR-10).
- **Denial of Service:** un pedido externo falla en el acto (`NOTFOUND`) en lugar de esperar un tiempo de espera.
- **Elevation of Privilege:** los navegadores corren sin privilegios especiales y con perfiles temporales.

## Data classification
| Data | Class | At rest | In transit |
|---|---|---|---|
| `GITHUB_TOKEN` de la corrida | credentials | lo crea GitHub por corrida, con `contents: read`, y vence al terminar; nunca se escribe en el repositorio | TLS 1.2+ hacia la API de GitHub |
| Datos ficticios de clientes de las pruebas (DNI, email) | PII | solo en bases SQLite temporales del runner, borradas al terminar; el disco del runner se descarta | solo `https://localhost` dentro del runner |
| Certificado de desarrollo de .NET | credentials | lo genera `dotnet dev-certs https` en cada corrida y se descarta con el runner | no sale del runner |
| Registros de la corrida | public | en GitHub Actions, visibles para quien ve el repositorio | TLS hacia GitHub |

## Risks and mitigations
| ID | Risk | STRIDE | Likelihood | Impact | Mitigation |
|---|---|---|---|---|---|
| R-01 | Acción de terceros comprometida o etiqueta movida | T | L | H | Solo acciones oficiales de `actions/` fijadas por SHA (D-02); la regla `acciones` rechaza un `uses:` sin SHA |
| R-02 | Dependencia npm o NuGet maliciosa o cambiada | T | L | H | `npm ci` con integridad `sha512` del lockfile; NuGet con `packages.lock.json` en modo bloqueado (D-04: NU1004 si el proyecto no coincide, NU1403 si cambia el hash de un paquete); `npx --no-install` usa el Playwright fijado; la regla `instalacion` rechaza `npm install` |
| R-03 | Corridas colgadas o repetidas consumen los minutos del repositorio | D | M | L | `timeout-minutes: 45` y `concurrency` con `cancel-in-progress: true` (reglas `tiempo-maximo` y `cancelacion`) |
| R-04 | Datos personales en los registros públicos de la corrida | I | L | M | Las pruebas usan solo datos ficticios; la regla `acciones` impide subir artefactos o cachés con bases o resultados; la Api de e2e corre con el log de EF Core en Warning (sin SQL con valores) |
| R-05 | Una prueba habla con ARCA u otro servicio externo | S | L | H | Regla de resolución en los tres navegadores (D-05) y la prueba `Pruebas_SinHostsExternos`, que también rechaza IP literales, sobre los archivos de prueba |
| R-06 | Un pull request de un fork intenta leer secretos o escribir en el repositorio | E | M | H | Disparador `pull_request` sin secretos, `permissions: contents: read`, checkout sin credenciales guardadas; reglas `disparador-inseguro`, `secretos`, `credenciales` y `permisos` |

## Supply chain
Dependencias nuevas de la corrida: `actions/checkout` v7.0.1, `actions/setup-dotnet` v6.1.0 y `actions/setup-node`
v7.1.0, fijadas por SHA (D-02). Chrome y Edge estables vienen en la imagen `ubuntu-24.04`; Playwright 1.64.1 (fijado
en `e2e/package-lock.json`) solo instala sus dependencias de sistema con apt (D-06) y la corrida imprime sus
versiones. No se agregan paquetes npm ni NuGet; los de NuGet pasan a estar bloqueados con `packages.lock.json` (D-04).

## Availability
El único vector es el consumo de minutos del runner: corridas colgadas o pushes seguidos. `timeout-minutes: 45` y la
cancelación por pull request lo acotan (R-03). La caída de GitHub o de un registro deja la corrida en rojo, sin efecto
sobre la aplicación.
