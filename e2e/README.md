# Suite de punta a punta (e2e)

`npm run test:harness` corre la suite en los canales estables de Chrome y Edge instalados en el sistema (NFR-03).
`servidor.mjs` compila la web y la Api y levanta **una Api por navegador y por archivo de pruebas**, cada una con su
base y su marca de prueba en directorios temporales. `npm run test:unidad` corre las pruebas unitarias de
`compilacion.mjs` (sin navegador ni Api).

## Proyectos, Api y bases

Cada archivo de `tests/` deja su base en un estado que otro no espera (`acceso.spec.ts` la termina con otra
contraseña y bloqueada 5 minutos), así que `playwright.config.ts` arma un proyecto por navegador y por archivo, cada
uno con su Api, su puerto y su base nueva:

| Proyecto | Navegador (canal) | Archivo | Puerto (con `OPTICA_E2E_PUERTO` por defecto) |
|---|---|---|---|
| `chrome-acceso` | `chrome` | `acceso.spec.ts` | 5543 |
| `chrome-configuracion` | `chrome` | `configuracion.spec.ts` | 5544 |
| `msedge-acceso` | `msedge` | `acceso.spec.ts` | 5545 |
| `msedge-configuracion` | `msedge` | `configuracion.spec.ts` | 5546 |

Dentro de un proyecto las pruebas corren en orden y de a una, porque dependen de lo que dejó la anterior en su base.
Para correr un solo navegador: `npx playwright test --project="chrome-*"`; un solo archivo en un navegador:
`npx playwright test --project=msedge-configuracion`.

La lista de archivos sale de `tests/`: un `*.spec.ts` nuevo tiene sus proyectos (`chrome-<archivo>` y
`msedge-<archivo>`), con su Api y su base, sin tocar la configuración. Su nombre tiene que ser palabras en minúsculas
unidas por guiones (por ejemplo, `presupuestos-borrador.spec.ts`); si no, `playwright.config.ts` falla con un error
que nombra el archivo, en lugar de dejarlo sin correr.

Si una Api termina o no responde al arrancar, o si `OPTICA_E2E_INSTANCIAS` tiene un nombre mal formado o repetido o un
puerto fuera de 1 a 65535, `servidor.mjs` termina con código 1 y Playwright no corre ninguna prueba.

Se puede correr por dos vías: desde WSL (Linux), con Chrome y Edge instalados ahí, o desde Windows. Las dos cuentan
para NFR-03; lo que no se puede es usar el mismo árbol desde las dos (ver "Correr la suite desde Windows").

## Requisitos comunes

- **Node 22.22.2 o posterior dentro de la línea 22** (y npm 10.9.8 o posterior). `web/` y `e2e/` tienen un `.npmrc`
  con `engine-strict=true`: con otra versión (por ejemplo, Node 24), `npm ci` falla en lugar de solo avisar. Si tenés
  otra versión, instalá la 22 (instalador de nodejs.org o un administrador de versiones como `nvm`, `nvm-windows` o
  `fnm`) y comprobá con `node --version`.
- **El SDK de .NET 10** en el `PATH` y el certificado de desarrollo de .NET (`dotnet dev-certs https`); el navegador
  de la suite ignora que no sea de confianza.
- **Dependencias**, en cada carpeta: `npm ci` en `web/` y en `e2e/`.

## Correr la suite desde WSL

1. **Navegadores.** Chrome y Edge se instalan en WSL con Playwright, en una terminal con sudo (instala también las
   dependencias del sistema):

   ```bash
   cd e2e
   npx playwright install --with-deps chrome msedge
   ```

2. **Correr:** en `e2e/`, `npm run test:harness`.

## Correr la suite desde Windows

1. **Un clon aparte, fuera de WSL** (por ejemplo `C:\Repos\optica`), o una copia del árbol a una carpeta de Windows.
   No uses el mismo árbol desde WSL y desde Windows: `node_modules` tiene binarios nativos de la plataforma que lo
   instaló, y `servidor.mjs` se detiene con un error claro si `web/node_modules` es de otra plataforma.
2. **Dependencias**, en cada carpeta:

   ```powershell
   cd web; npm ci; cd ..
   cd e2e; npm ci
   ```

3. **Navegadores.** Si Chrome o Edge no están instalados, o Playwright no los encuentra:

   ```powershell
   npx playwright install msedge chrome
   ```

4. **Correr:** en `e2e\`, `npm run test:harness`.

Las rutas con espacios (por ejemplo, una carpeta "Modulo 6") funcionan: `dotnet` se lanza sin intérprete de comandos.

## Pruebas de .NET rápidas

`dotnet test Optica.sln` incluye `PublicacionSinRutasTests`, que hace un `dotnet publish` completo en Release y tarda
bastante más que el resto. Para las corridas rápidas durante el desarrollo se puede excluir:

```bash
dotnet test Optica.sln --filter "Categoria!=Publicacion"
```

Antes de dar por terminado un cambio, corré la suite completa, sin filtro.

## Diagnóstico sin Chrome ni Edge

`OPTICA_E2E_DIAGNOSTICO_CHROMIUM=1 npx playwright test --project="chromium-*"` usa el Chromium que descarga Playwright
(proyectos `chromium-acceso` y `chromium-configuracion`).
Es solo un diagnóstico: no reemplaza a `chrome` y `msedge` ni cuenta para NFR-03.
