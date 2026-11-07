# Threat model FEAT-004: Configurar un linter en la web (oxlint)

| Field | Value |
|-------|-------|
| Ticket | FEAT-004 |
| Spec | docs/ddw/specs/spec-FEAT-004.md |
| Tier | FEATURE |
| Date | 2026-11-07 |

## Components
| Component | Source in the spec |
|---|---|
| `web/package.json` y `web/package-lock.json` (dependencia `oxlint` 1.92.0 y script `lint`) | Block 1 |
| `web/lint.test.mjs` | Block 1 |
| `web/src/paginas/EditarArticulo.tsx` (con `Presupuesto.tsx`, `NuevoArticulo.tsx`, `MarcaProvider.tsx`, `contextoMarca.ts`, `servidorFalso.tsx` y `sondas.tsx`) | Block 2 |

## Trust boundaries
- Registro público de npm → PC de desarrollo: `npm ci` descarga `oxlint` y su binario nativo de la plataforma
  (`@oxlint/binding-*`), que se ejecuta con los permisos de quien corre `npm run lint` o las pruebas.
- Prueba `node:test` → sistema de archivos: `lint.test.mjs` escribe archivos de ejemplo en una carpeta temporal del
  sistema operativo y lanza el binario de oxlint como proceso hijo.
- La web publicada (`web/dist`) no cruza ninguna frontera nueva: oxlint es solo de desarrollo y no entra en la salida.

## STRIDE analysis
### `web/package.json` y `web/package-lock.json` (dependencia `oxlint` 1.92.0 y script `lint`)
- **Spoofing:** un paquete con nombre parecido (typosquatting) en lugar de `oxlint`; se instala el nombre exacto del
  repositorio `oxc-project/oxc` y el `package-lock.json` fija la integridad `sha512` de cada paquete (R-01).
- **Tampering:** una versión nueva comprometida que llegue sola; la versión exacta `1.92.0` sin `^` y `npm ci` con el
  lockfile impiden la actualización silenciosa (R-01).
- **Repudiation:** el cambio de dependencias queda en el historial de git con el lockfile completo.
- **Information Disclosure:** oxlint lee solo el código de `web/src`, que no contiene secretos ni datos personales
  reales; no hace pedidos de red al analizar.
- **Denial of Service:** un binario que no exista para la plataforma rompe `npm run lint`, no la aplicación; las
  plataformas usadas (Windows x64 y Linux x64 en WSL) tienen binario publicado.
- **Elevation of Privilege:** el binario corre con los permisos del desarrollador; no hay scripts `postinstall` propios
  (R-02).

### `web/lint.test.mjs`
- **Spoofing:** sin identidades; la prueba ejecuta el JavaScript del paquete `oxlint` del proyecto, resuelto con `createRequire`, con el mismo Node (`process.execPath`), no un programa del `PATH`.
- **Tampering:** escribe solo dentro de su carpeta temporal única (`mkdtemp`) y la borra al terminar; no modifica
  archivos del repositorio (R-03).
- **Repudiation:** sin registro; el resultado queda en la salida de `node --test`.
- **Information Disclosure:** los archivos de ejemplo son código ficticio sin datos.
- **Denial of Service:** la ejecución de oxlint tiene un límite de tiempo en la prueba (60 s, NFR-01).
- **Elevation of Privilege:** el proceso hijo se lanza con `execFile(process.execPath, ...)` y argumentos fijos, sin intérprete de comandos,
  así ninguna ruta se interpreta como comando (R-03).

### `web/src/paginas/EditarArticulo.tsx` (con `Presupuesto.tsx`, `NuevoArticulo.tsx`, `MarcaProvider.tsx`, `contextoMarca.ts`, `servidorFalso.tsx` y `sondas.tsx`)
- **Spoofing:** sin cambios en la sesión ni en la autenticación; las pantallas siguen exigiendo sesión como hoy.
- **Tampering:** las correcciones cambian la forma del código, no los datos que se envían; las pruebas existentes no
  cambian sus aserciones (R-04).
- **Repudiation:** sin cambios.
- **Information Disclosure:** `Presupuesto.tsx` muestra datos personales del cliente; la corrección no puede agregar
  `console`, almacenamiento del navegador ni datos en la URL (la prueba `NuevoPresupuesto_NoGuardaDatosEnElAlmacenamiento`
  y las de `Presupuesto.test.tsx` siguen vigentes) (R-04).
- **Denial of Service:** corregir `set-state-in-effect` reduce renders en cascada; no agrega pedidos.
- **Elevation of Privilege:** sin cambios de autorización.

## Data classification
| Data | Class | At rest | In transit |
|---|---|---|---|
| Código fuente de `web/src` analizado por oxlint | public (código del proyecto, sin secretos) | repositorio git; sin cifrado adicional | no sale de la PC; oxlint no usa red |
| Datos del cliente mostrados por `Presupuesto.tsx` | PII (Ley 25.326) | sin cambios: base sin cifrar, riesgo aceptado R-01 de FEAT-003a | sin cambios: TLS y `no-store` (FEAT-003a) |

## Risks and mitigations
| ID | Risk | STRIDE | Likelihood | Impact | Mitigation |
|---|---|---|---|---|---|
| R-01 | Paquete de npm comprometido o con nombre parecido (cadena de suministro) | T | L | H | nombre exacto `oxlint` del repositorio `oxc-project/oxc`, versión exacta 1.92.0, integridad `sha512` en `package-lock.json`, `npm ci` en lugar de `npm install`, `npm audit` sin vulnerabilidades |
| R-02 | El binario nativo ejecuta código con los permisos del desarrollador | E | L | M | solo dependencia de desarrollo, no se publica; sin `postinstall` propio; mismo nivel de confianza que Vite y TypeScript, ya instalados |
| R-03 | La prueba deja archivos o ejecuta rutas como comandos | T | L | L | carpeta temporal única con `mkdtemp`, borrada al terminar; `execFile(process.execPath, ...)` sin shell y argumentos fijos |
| R-04 | Una corrección cambia el comportamiento o expone datos del cliente | I | L | M | ninguna aserción existente cambia; las pruebas de datos personales de FEAT-003a siguen; revisión del verificador y del auditor por bloque |

## Supply chain
Dependencia nueva: `oxlint` 1.92.0 (proyecto `oxc-project/oxc`, licencia MIT) con sus binarios opcionales por
plataforma `@oxlint/binding-*` de la misma versión. Solo de desarrollo, fijada por versión e integridad en el lockfile,
auditada con `npm audit` en el cierre de CODE.

## Availability
Sin superficie nueva en la aplicación publicada. Si el binario de oxlint no está disponible, falla el análisis de
desarrollo, no la web ni la Api.
