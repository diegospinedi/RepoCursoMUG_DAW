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
| `chrome-articulos` | `chrome` | `articulos.spec.ts` | 5544 |
| `chrome-articulos-edicion` | `chrome` | `articulos-edicion.spec.ts` | 5545 |
| `chrome-configuracion` | `chrome` | `configuracion.spec.ts` | 5546 |
| `chrome-configuracion-recalculo` | `chrome` | `configuracion-recalculo.spec.ts` | 5547 |
| `chrome-presupuestos-alta` | `chrome` | `presupuestos-alta.spec.ts` | 5548 |
| `msedge-acceso` | `msedge` | `acceso.spec.ts` | 5549 |
| `msedge-articulos` | `msedge` | `articulos.spec.ts` | 5550 |
| `msedge-articulos-edicion` | `msedge` | `articulos-edicion.spec.ts` | 5551 |
| `msedge-configuracion` | `msedge` | `configuracion.spec.ts` | 5552 |
| `msedge-configuracion-recalculo` | `msedge` | `configuracion-recalculo.spec.ts` | 5553 |
| `msedge-presupuestos-alta` | `msedge` | `presupuestos-alta.spec.ts` | 5554 |

Los puertos son consecutivos desde `OPTICA_E2E_PUERTO`, en el orden de la tabla: primero los navegadores y, dentro de
cada uno, los archivos en el orden de `sort()` de JavaScript sobre el nombre sin `.spec.ts` (por eso `articulos` va
antes que `articulos-edicion` y `configuracion` antes que `configuracion-recalculo`: un nombre que es el comienzo de
otro ordena primero). Un archivo nuevo corre los puertos de los que vienen después: `articulos.spec.ts` (FEAT-002a)
movió `chrome-configuracion` de 5544 a 5545 y los de Edge de 5545–5546 a 5546–5548; `articulos-edicion.spec.ts`
(FEAT-002b) movió `chrome-configuracion` a 5546 y los de Edge a 5547–5550; y `configuracion-recalculo.spec.ts`
(FEAT-002c) ocupa 5547 en Chrome y mueve los de Edge a 5548–5551, con el suyo en 5552; y `presupuestos-alta.spec.ts`
(FEAT-003a) ocupa 5548 en Chrome y mueve los de Edge a 5549–5553, con el suyo en 5554. Si tenés algo fijado a un
puerto, buscá el actual en esta tabla o en la línea `[e2e] Listo: …` que escribe `servidor.mjs` al arrancar.

`articulos.spec.ts` cambia la configuración de su propia base desde la pantalla de configuración: pasa el múltiplo a 50
y lo vuelve a 0,01 (AC-10), y al final deja Monotributo (AC-09). Por eso sus pruebas también van en ese orden.

`articulos-edicion.spec.ts` (FEAT-002b, AC-01 a AC-06) es la excepción al párrafo siguiente: sus pruebas no dependen
del orden. La que corre primero define la contraseña de la base y las demás ingresan con ella; cada prueba crea sus
artículos por la Api con la sesión de la página y con sus propios códigos en el proveedor (`ED-…`), y las que
verifican un precio (AC-01, AC-02, AC-03 y AC-05) dejan antes Responsable Inscripto, alícuota 21 y múltiplo 0,01
desde la pantalla de configuración. Solo la prueba de AC-04 graba descripciones con "armazón", porque esa búsqueda
tiene que listar todas las que lo contienen y ninguna más.

`configuracion-recalculo.spec.ts` (FEAT-002c, AC-01 a AC-04) cambia la configuración desde la pantalla y verifica el
recálculo del catálogo. Cada prueba fija antes, por la Api, la configuración de partida (21, Responsable Inscripto,
0,01 y sin tope; D-24) y crea sus artículos por la Api con códigos en el proveedor propios (`RC-…`). Los artículos sí
quedan de una prueba a la otra, así que la cantidad del aviso "Cambió el precio de venta de N artículos." se compara
con los precios de toda la base leídos antes y después de grabar. La prueba de AC-04 tiene que ser la última: deja un
artículo de costo `ImporteMaximo` que haría rechazar cualquier recálculo posterior con múltiplo 50. Ese artículo solo
existe en la base de este proyecto, que no comparte ningún otro archivo.

`presupuestos-alta.spec.ts` (FEAT-003a, AC-01 a AC-19) graba presupuestos desde la pantalla y los vuelve a abrir. La
primera prueba define la contraseña y verifica AC-10 con la base vacía (el primer presupuesto recibe el número 1); las
demás ingresan con ella. Cada prueba deja por la Api Responsable Inscripto, alícuota 21 y múltiplo 0,01, y crea sus
artículos por la Api con códigos en el proveedor propios (`PR-…`); solo la prueba de AC-13 graba descripciones con
"Armazón" o "armazon", activas e inactivas. Ninguna prueba, salvo AC-10 y AC-09, espera un número fijo: lo leen de la
respuesta del alta. La prueba de AC-09 graba por la Api hasta que el último número es 154 y graba el 155 desde la
pantalla; la de AC-19 compara la fecha con la del día de la PC que corre la Api y la suite (la misma). Los datos de
clientes son ficticios (D-17 y D-36 de FEAT-003a).

Dentro de un proyecto las pruebas corren en orden y de a una, porque dependen de lo que dejó la anterior en su base.
Para correr un solo navegador: `npx playwright test --project="chrome-*"`; un solo archivo en un navegador:
`npx playwright test --project=msedge-configuracion`.

La lista de archivos sale de `tests/`: un `*.spec.ts` nuevo tiene sus proyectos (`chrome-<archivo>` y
`msedge-<archivo>`), con su Api y su base, sin tocar la configuración. Su nombre tiene que ser palabras en minúsculas
unidas por guiones (por ejemplo, `presupuestos-borrador.spec.ts`); si no, `playwright.config.ts` falla con un error
que nombra el archivo, en lugar de dejarlo sin correr.

**Edge sin red externa.** Con un perfil nuevo, Edge inicia sesión solo con la cuenta del equipo y consulta servicios
de Microsoft (Graph, Office, Bing); mientras lo hace, el navegador deja de atender pedidos por varios segundos y las
pruebas fallaban de forma intermitente en "Cargando…" (W-VER-03 de FEAT-001a y FEAT-001b). Los proyectos `msedge-*`
arrancan con `--host-resolver-rules=MAP * ~NOTFOUND, EXCLUDE localhost`: solo se resuelve `localhost`, así esos pedidos
fallan en el acto y no sale ningún dato de la cuenta (FEAT-002b, D-30). No subas `expect.timeout` para taparlo.

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
(proyectos `chromium-acceso`, `chromium-articulos`, `chromium-articulos-edicion`, `chromium-configuracion`,
`chromium-configuracion-recalculo` y `chromium-presupuestos-alta`).
Es solo un diagnóstico: no reemplaza a `chrome` y `msedge` ni cuenta para NFR-03.
