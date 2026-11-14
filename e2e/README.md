# Suite de punta a punta (e2e)

`npm run test:harness` corre la suite en los canales estables de Chrome y Edge instalados en el sistema (NFR-03).
`servidor.mjs` compila la web y la Api y levanta **una Api por navegador y por archivo de pruebas**, cada una con su
base y su marca de prueba en directorios temporales. `npm run test:unidad` corre las pruebas unitarias de
`compilacion.mjs` (sin navegador ni Api) y las de `ci.mjs`, que fijan las reglas de la integración continua (por
ejemplo, que cada proyecto de .NET tenga su `packages.lock.json` y que la CI restaure en modo bloqueado).

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
| `chrome-facturacion` | `chrome` | `facturacion.spec.ts` | 5548 |
| `chrome-facturacion-rechazo` | `chrome` | `facturacion-rechazo.spec.ts` | 5549 |
| `chrome-facturacion-sin-respuesta` | `chrome` | `facturacion-sin-respuesta.spec.ts` | 5550 |
| `chrome-presupuestos-alta` | `chrome` | `presupuestos-alta.spec.ts` | 5551 |
| `chrome-presupuestos-busqueda` | `chrome` | `presupuestos-busqueda.spec.ts` | 5552 |
| `chrome-presupuestos-calculos` | `chrome` | `presupuestos-calculos.spec.ts` | 5553 |
| `chrome-presupuestos-concurrencia` | `chrome` | `presupuestos-concurrencia.spec.ts` | 5554 |
| `chrome-presupuestos-edicion` | `chrome` | `presupuestos-edicion.spec.ts` | 5555 |
| `chrome-presupuestos-pdf` | `chrome` | `presupuestos-pdf.spec.ts` | 5556 |
| `msedge-acceso` | `msedge` | `acceso.spec.ts` | 5557 |
| `msedge-articulos` | `msedge` | `articulos.spec.ts` | 5558 |
| `msedge-articulos-edicion` | `msedge` | `articulos-edicion.spec.ts` | 5559 |
| `msedge-configuracion` | `msedge` | `configuracion.spec.ts` | 5560 |
| `msedge-configuracion-recalculo` | `msedge` | `configuracion-recalculo.spec.ts` | 5561 |
| `msedge-facturacion` | `msedge` | `facturacion.spec.ts` | 5562 |
| `msedge-facturacion-rechazo` | `msedge` | `facturacion-rechazo.spec.ts` | 5563 |
| `msedge-facturacion-sin-respuesta` | `msedge` | `facturacion-sin-respuesta.spec.ts` | 5564 |
| `msedge-presupuestos-alta` | `msedge` | `presupuestos-alta.spec.ts` | 5565 |
| `msedge-presupuestos-busqueda` | `msedge` | `presupuestos-busqueda.spec.ts` | 5566 |
| `msedge-presupuestos-calculos` | `msedge` | `presupuestos-calculos.spec.ts` | 5567 |
| `msedge-presupuestos-concurrencia` | `msedge` | `presupuestos-concurrencia.spec.ts` | 5568 |
| `msedge-presupuestos-edicion` | `msedge` | `presupuestos-edicion.spec.ts` | 5569 |
| `msedge-presupuestos-pdf` | `msedge` | `presupuestos-pdf.spec.ts` | 5570 |

Los puertos son consecutivos desde `OPTICA_E2E_PUERTO`, en el orden de la tabla: primero los navegadores y, dentro de
cada uno, los archivos en el orden de `sort()` de JavaScript sobre el nombre sin `.spec.ts` (por eso `articulos` va
antes que `articulos-edicion` y `configuracion` antes que `configuracion-recalculo`: un nombre que es el comienzo de
otro ordena primero). Un archivo nuevo corre los puertos de los que vienen después: `articulos.spec.ts` (FEAT-002a)
movió `chrome-configuracion` de 5544 a 5545 y los de Edge de 5545–5546 a 5546–5548; `articulos-edicion.spec.ts`
(FEAT-002b) movió `chrome-configuracion` a 5546 y los de Edge a 5547–5550; y `configuracion-recalculo.spec.ts`
(FEAT-002c) ocupa 5547 en Chrome y mueve los de Edge a 5548–5551, con el suyo en 5552; y `presupuestos-alta.spec.ts`
(FEAT-003a) ocupa 5548 en Chrome y mueve los de Edge a 5549–5553, con el suyo en 5554; y `presupuestos-calculos.spec.ts`
(FEAT-003b) ocupa 5549 en Chrome y mueve los de Edge a 5550–5555, con el suyo en 5556; y `presupuestos-edicion.spec.ts`
(FEAT-003c) ocupa 5550 en Chrome y mueve los de Edge a 5551–5557, con el suyo en 5558; y
`presupuestos-concurrencia.spec.ts` (FEAT-003d) ocupa 5550 en Chrome, mueve `chrome-presupuestos-edicion` a 5551 y los
de Edge a 5552–5558 y 5560, con el suyo en 5559; y `presupuestos-busqueda.spec.ts` y `presupuestos-pdf.spec.ts`
(FEAT-006) ocupan 5549 y 5553 en Chrome, mueven `chrome-presupuestos-calculos`, `-concurrencia` y `-edicion` a
5550–5552 y los de Edge a 5554–5559 y 5561–5563, con los suyos en 5560 y 5564; y `facturacion.spec.ts`,
`facturacion-rechazo.spec.ts` y `facturacion-sin-respuesta.spec.ts` (FEAT-007) ocupan 5548–5550 en Chrome, mueven los
`chrome-presupuestos-*` a 5551–5556 y los de Edge a 5557–5561 y 5565–5570, con los suyos en 5562–5564. Son 28
proyectos, de 5543 a 5570. Si tenés algo fijado a un puerto, buscá el actual en esta tabla o en la línea
`[e2e] Listo: …` que escribe `servidor.mjs` al arrancar.

**ARCA en la suite (FEAT-007).** Todas las Api de la suite arrancan con ARCA simulada (`Arca__Entorno=Simulado`), con
un tiempo máximo de 2 s (`Arca__TiempoMaximoSegundos=2`) y sin CUIT ni certificado (`Arca__Cuit`,
`Arca__Certificado__Ruta` y `Arca__Certificado__Clave` vacíos). Antes, `entornoDeLaApi` (`compilacion.mjs`) quita
del entorno de quien corre la suite toda clave de ARCA, en cualquier mayúscula, con `__` o `:` y también con los
prefijos `ASPNETCORE_` y `DOTNET_` (por ejemplo, `ARCA__ENTORNO=Produccion`). Así ninguna prueba llega a ARCA, ni a
homologación ni a producción, y sus comprobantes no tienen validez fiscal. La revisión de hosts externos de
`npm run test:unidad` admite solo las direcciones de homologación de WSAA y WSFEv1 y los espacios de nombres SOAP que
escriben las pruebas de .NET del cliente de ARCA, cada uno con su ruta exacta; producción nunca. La
respuesta del simulador (`Arca__Simulado__Respuesta`) sale del archivo de pruebas, que `servidor.mjs` conoce por el
nombre del proyecto (`variablesDeArca` en `compilacion.mjs`): `rechazar` para `facturacion-rechazo.spec.ts`,
`no_responder` para `facturacion-sin-respuesta.spec.ts` y `autorizar` para todos los demás. Solo la Api de
`facturacion.spec.ts` arranca con el contador del simulador (`arca-simulado.json` en su directorio de datos) en 124
para la Factura B del punto de venta 3, así su primera factura autorizada es la 0003-00000125 de AC-19 y AC-30.

`facturacion.spec.ts` (FEAT-007, AC-01, AC-02, AC-03, AC-13, AC-18, AC-19, AC-30 y AC-32) emite la factura desde la
pantalla del presupuesto. Sus pruebas van en orden: primero la configuración incompleta (la base nueva no tiene tope
ni punto de venta, y el punto de venta no se puede borrar una vez cargado), que no consume ningún número; después la
emisión de la Factura B 0003-00000125 de un Final de 3 líneas y $3.000,00, que el presupuesto muestra también al
volver a abrirlo; y al final un Borrador y un presupuesto ya facturado, con el botón deshabilitado y la Api rechazando
el pedido directo (409 `borrador` y `facturado`). `facturacion-rechazo.spec.ts` (AC-20) muestra "ARCA rechazó la
factura: 10016 – …" con Facturar habilitado y vuelve a emitir; `facturacion-sin-respuesta.spec.ts` (AC-21 y AC-22)
espera los 2 s del tiempo máximo, muestra "ARCA no respondió. La emisión quedó pendiente de verificación.", deja
Facturar deshabilitado también al volver a abrir el presupuesto y comprueba que un pedido directo recibe 409
`pendiente` sin esperar a ARCA. Las tres cargan la configuración por la Api antes de emitir, crean sus artículos con
códigos propios (`FA-…`, `FR-…`, `FS-…`) y graban sus presupuestos en Borrador y después en Final. Los clientes son
ficticios, con el DNI 00.718.294 (un DNI que empieza con 00 no se emite, D-50 de FEAT-007) y emails en `example.com`.

`articulos.spec.ts` cambia la configuración de su propia base desde la pantalla de configuración: pasa el múltiplo a 50
y lo vuelve a 0,01 (AC-10), y al final deja Monotributo (AC-09). Por eso sus pruebas también van en ese orden.

`articulos-edicion.spec.ts` (FEAT-002b, AC-01 a AC-06) es uno de los archivos cuyas pruebas no dependen
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
clientes son ficticios (D-17 y D-36 de FEAT-003a). Desde FEAT-003b la Api exige Cantidad y Porcentaje de Descuento en
cada línea (las altas por la Api envían 1 y 0) y la tabla de líneas tiene el total en `tfoot`: las filas se cuentan y
se leen solo en `tbody`.

`presupuestos-calculos.spec.ts` (FEAT-003b, AC-01, AC-02, AC-03, AC-07, AC-08 y AC-09) carga la Cantidad y el
Descuento desde la pantalla de alta y verifica los precios de la línea y el total recalculados, los mensajes junto al
campo (sin grabar nada) y el total del presupuesto grabado. Sus pruebas no dependen del orden: la primera que corre
define la contraseña y las demás ingresan con ella; cada una deja por la Api la configuración de partida (Responsable
Inscripto, 21 y 0,01) y crea sus artículos con códigos en el proveedor propios (`PC-…`). El cliente es ficticio, con
DNI 99.999.999.

`presupuestos-edicion.spec.ts` (FEAT-003c, AC-01 a AC-10) modifica presupuestos en Borrador desde la pantalla, los
pasa a Final con el diálogo de confirmación (y verifica en el navegador real que el diálogo es modal), cancela el paso a
Final, intenta modificar un Final por la pantalla y con pedidos directos a la Api hechos desde el navegador con la
sesión, y comprueba que las líneas conservan su Precio Unitario después de recalcular el catálogo (múltiplo 50, desde la
pantalla de configuración) y de desactivar el artículo. Sus pruebas no dependen del orden: la primera que corre define
la contraseña y las demás ingresan con ella; cada una deja por la Api la configuración de partida (Responsable
Inscripto, 21 y 0,01), crea sus artículos con códigos en el proveedor propios (`PE-…`) y graba sus presupuestos por la
Api en Borrador. Un presupuesto en Final se prepara como en la app: se graba en Borrador y después pasa a Final. AC-01 se
verifica como fecha intacta después de modificar (la fecha de otro día se prueba en .NET con reloj falso). El cliente
es ficticio, con DNI 99.999.999 y un email en `example.com`.

`presupuestos-concurrencia.spec.ts` (FEAT-003d, AC-01 y, por correr en `chrome` y `msedge`, AC-02) abre dos sesiones
en contextos de navegador separados (`browser.newContext()`): la primera ingresa (en la base nueva define la
contraseña) y recién después la segunda, con la misma contraseña y su propia cookie. Las dos pulsan "Grabar" a la vez
en el alta y cada pantalla muestra "Presupuesto N.º {n} grabado. Estado: Borrador." con números distintos y
consecutivos, sin el mensaje de error general; al reabrir, cada presupuesto tiene sus datos. La otra prueba carga un
DNI inválido en una sesión mientras la otra graba: la inválida muestra el mensaje junto al DNI sin enviar el alta, la
otra recibe el número siguiente y, corregido el DNI, la primera recibe el que sigue, sin hueco. El encuentro de las dos
altas dentro de la base se fuerza solo en las pruebas de .NET (integración y Api); acá se prueba la respuesta de cada
pantalla. Sus pruebas no dependen del orden: cada una deja por la Api la configuración de partida (Responsable
Inscripto, 21 y 0,01), crea sus artículos con códigos en el proveedor propios (`PCC-…`), graba por la Api un
presupuesto testigo y compara los números con el de ese testigo, sin esperar un número fijo. Los clientes son
ficticios, con DNI 99.999.9xx y emails en `example.com`, y cada prueba cierra sus dos contextos al terminar.

`presupuestos-busqueda.spec.ts` (FEAT-006, AC-01 a AC-10 y la medición de NFR-01 en la pantalla) busca desde la
pantalla `/presupuestos` por Apellido, Nombre, DNI y fechas, pagina y abre un resultado. Ninguna fecha se modifica en la
base: todos los presupuestos tienen la fecha del día en que se graban, así que los filtros de fecha se prueban con el
día anterior, el mismo y el siguiente (límites inclusive, una sola fecha cargada y Desde posterior a Hasta, que no
envía la búsqueda); los rangos con fechas de marzo de 2026 se prueban en .NET con reloj falso. Solo la primera prueba
graba Apellidos con "González" y "Gómez", el Nombre "María José" y un DNI que contiene "23456" (99.923.456, ficticio), así esas
búsquedas tienen un resultado exacto. La prueba de AC-08 graba por la Api hasta que el último número es 120 (falla si la
base ya lo pasó) y verifica las páginas 1 (120 a 71) y 3 (20 a 1); la de NFR-01 mide desde la pulsación de Buscar hasta
la grilla, que tiene que aparecer en menos de 2 s (el volumen de 10.000 presupuestos de AC-16 se mide en las pruebas de
la Api, D-03). Cada búsqueda verifica que los filtros viajan en el cuerpo y no en la URL. Sus pruebas no dependen del
orden: cada una deja por la Api la configuración de partida y crea sus artículos con códigos propios (`PB-…`).

`presupuestos-pdf.spec.ts` (FEAT-006, AC-11 y AC-12) descarga el PDF de un presupuesto en Final con el botón
"Descargar PDF" (evento `download` de Playwright), comprueba el nombre `presupuesto-{n}.pdf`, que la descarga sale de
una URL de objeto con la Content-Security-Policy de la SPA activa y sin violaciones, y lee el archivo en Node con el build
`legacy` de `pdfjs-dist` (sin worker, sin fuentes del sistema y sin ninguna URL de fuentes, CMaps o wasm): tiene que
ser un PDF válido con el texto "Presupuesto N.º {n}". El contenido completo del PDF se verifica en .NET con PdfPig
(D-02). En Borrador el botón está deshabilitado y un pedido directo a la Api, con la sesión, recibe 409 `borrador` sin
PDF. Sus pruebas no dependen del orden; los artículos usan códigos propios (`PP-…`) y el cliente es ficticio.
`pdfjs-dist` trae como dependencia opcional `@napi-rs/canvas`, un binario nativo por plataforma que `npm ci` instala
solo para la plataforma donde corre (en Linux x64, las variantes glibc y musl; en Windows x64, la de MSVC).
`pdfjs-dist` lo carga al importarse para `DOMMatrix` y `Path2D`; la suite no dibuja páginas y, si el binario falta, solo
hay advertencias.

Dentro de un proyecto las pruebas corren en orden y de a una porque así lo fija `playwright.config.ts`
(`workers: 1`, `fullyParallel: false`): comparten la base del proyecto. Solo algunos archivos dependen además de lo que
dejó la prueba anterior (`articulos.spec.ts`, `configuracion-recalculo.spec.ts`, `facturacion.spec.ts` y la primera
prueba de `presupuestos-alta.spec.ts`, por ejemplo); `articulos-edicion.spec.ts`, `presupuestos-busqueda.spec.ts`,
`presupuestos-calculos.spec.ts`, `presupuestos-concurrencia.spec.ts`, `presupuestos-edicion.spec.ts` y
`presupuestos-pdf.spec.ts` no dependen del orden, como se describe arriba.
Para correr un solo navegador: `npx playwright test --project="chrome-*"`; un solo archivo en un navegador:
`npx playwright test --project=msedge-configuracion`.

La lista de archivos sale de `tests/`: un `*.spec.ts` nuevo tiene sus proyectos (`chrome-<archivo>` y
`msedge-<archivo>`), con su Api y su base, sin tocar la configuración. Su nombre tiene que ser palabras en minúsculas
unidas por guiones (por ejemplo, `presupuestos-borrador.spec.ts`); si no, `playwright.config.ts` falla con un error
que nombra el archivo, en lugar de dejarlo sin correr.

**Navegadores sin red externa.** Todos los proyectos de la suite (`chrome-*`, `msedge-*` y los `chromium-*` del
diagnóstico) arrancan con `--host-resolver-rules=MAP * ~NOTFOUND, EXCLUDE localhost`: solo se resuelve `localhost`,
así un pedido a cualquier otro nombre falla en el acto y no sale de la máquina ningún dato de la prueba ni de la cuenta
del equipo (FEAT-005, FR-10 y D-05). En Edge empezó como arreglo (FEAT-002b, D-30): con un perfil nuevo, Edge inicia
sesión solo con la cuenta del equipo y consulta servicios de Microsoft (Graph, Office, Bing); mientras lo hace, el
navegador deja de atender pedidos por varios segundos y las pruebas fallaban de forma intermitente en "Cargando…"
(W-VER-03 de FEAT-001a y FEAT-001b). No subas `expect.timeout` para taparlo. `npm run test:unidad` verifica que cada
proyecto de `playwright.config.ts` tenga la regla y que ningún archivo de prueba versionado (de .NET, de la web o de
esta suite) escriba una URL a un host externo; los permitidos son `localhost`, `127.0.0.1`, `::1`, las direcciones
privadas en decimal (`10.*`, `192.168.*`, `172.16.*` a `172.31.*`), los dominios `.example` y `example.com`, y
`www.w3.org` solo con los espacios de nombres de SVG, XHTML y XLink y la DTD de SVG 1.1 (que las pruebas de la marca
usan como un SVG a rechazar, sin descargarla). Las IP literales no pasan por la regla de resolución: por eso la
revisión de texto también las rechaza.

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
`chromium-configuracion-recalculo`, `chromium-facturacion`, `chromium-facturacion-rechazo`,
`chromium-facturacion-sin-respuesta`, `chromium-presupuestos-alta`, `chromium-presupuestos-busqueda`,
`chromium-presupuestos-calculos`, `chromium-presupuestos-concurrencia`, `chromium-presupuestos-edicion` y
`chromium-presupuestos-pdf`).
Es solo un diagnóstico: no reemplaza a `chrome` y `msedge` ni cuenta para NFR-03.
