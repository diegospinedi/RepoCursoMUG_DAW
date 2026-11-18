# Suite de punta a punta (e2e)

`npm run test:harness` corre la suite en los canales estables de Chrome y Edge instalados en el sistema (NFR-03).
`servidor.mjs` compila la web y la Api y levanta **una Api por navegador y por archivo de pruebas**, cada una con su
base y su marca de prueba en directorios temporales. `npm run test:unidad` corre las pruebas unitarias de
`compilacion.mjs` (sin navegador ni Api) y las de `ci.mjs`, que fijan las reglas de la integración continua (por
ejemplo, que cada proyecto de .NET tenga su `packages.lock.json` y que la CI restaure en modo bloqueado), y la de
`tests/xlsx-minimo.ts` (`tests/xlsx-minimo.test.mjs`): el generador de planillas .xlsx de `actualizar-costos.spec.ts`
(FEAT-011, D-18), que arma el ZIP con `node:zlib` sin paquetes npm y cuyo CRC y tamaños se verifican con un lector
propio de la prueba. Como no termina en `.spec.ts`, el generador no es un proyecto de Playwright.

## Proyectos, Api y bases

Cada archivo de `tests/` deja su base en un estado que otro no espera (`acceso.spec.ts` la termina con otra
contraseña y bloqueada 5 minutos), así que `playwright.config.ts` arma un proyecto por navegador y por archivo, cada
uno con su Api, su puerto y su base nueva:

| Proyecto | Navegador (canal) | Archivo | Puerto (con `OPTICA_E2E_PUERTO` por defecto) |
|---|---|---|---|
| `chrome-acceso` | `chrome` | `acceso.spec.ts` | 5543 |
| `chrome-actualizar-costos` | `chrome` | `actualizar-costos.spec.ts` | 5544 |
| `chrome-articulos` | `chrome` | `articulos.spec.ts` | 5545 |
| `chrome-articulos-edicion` | `chrome` | `articulos-edicion.spec.ts` | 5546 |
| `chrome-backup-aviso` | `chrome` | `backup-aviso.spec.ts` | 5547 |
| `chrome-busqueda-en-vivo` | `chrome` | `busqueda-en-vivo.spec.ts` | 5548 |
| `chrome-configuracion` | `chrome` | `configuracion.spec.ts` | 5549 |
| `chrome-configuracion-backup` | `chrome` | `configuracion-backup.spec.ts` | 5550 |
| `chrome-configuracion-emisor` | `chrome` | `configuracion-emisor.spec.ts` | 5551 |
| `chrome-configuracion-recalculo` | `chrome` | `configuracion-recalculo.spec.ts` | 5552 |
| `chrome-estilo-uniforme` | `chrome` | `estilo-uniforme.spec.ts` | 5553 |
| `chrome-facturacion` | `chrome` | `facturacion.spec.ts` | 5554 |
| `chrome-facturacion-rechazo` | `chrome` | `facturacion-rechazo.spec.ts` | 5555 |
| `chrome-facturacion-sin-respuesta` | `chrome` | `facturacion-sin-respuesta.spec.ts` | 5556 |
| `chrome-facturas-busqueda` | `chrome` | `facturas-busqueda.spec.ts` | 5557 |
| `chrome-facturas-pdf` | `chrome` | `facturas-pdf.spec.ts` | 5558 |
| `chrome-inicio-menu` | `chrome` | `inicio-menu.spec.ts` | 5559 |
| `chrome-presupuestos-alta` | `chrome` | `presupuestos-alta.spec.ts` | 5560 |
| `chrome-presupuestos-busqueda` | `chrome` | `presupuestos-busqueda.spec.ts` | 5561 |
| `chrome-presupuestos-calculos` | `chrome` | `presupuestos-calculos.spec.ts` | 5562 |
| `chrome-presupuestos-concurrencia` | `chrome` | `presupuestos-concurrencia.spec.ts` | 5563 |
| `chrome-presupuestos-edicion` | `chrome` | `presupuestos-edicion.spec.ts` | 5564 |
| `chrome-presupuestos-pdf` | `chrome` | `presupuestos-pdf.spec.ts` | 5565 |
| `chrome-verificacion-consulta-sin-respuesta` | `chrome` | `verificacion-consulta-sin-respuesta.spec.ts` | 5566 |
| `chrome-verificacion-emite` | `chrome` | `verificacion-emite.spec.ts` | 5567 |
| `chrome-verificacion-importe-distinto` | `chrome` | `verificacion-importe-distinto.spec.ts` | 5568 |
| `chrome-verificacion-recupera` | `chrome` | `verificacion-recupera.spec.ts` | 5569 |
| `chrome-verificacion-sin-respuesta` | `chrome` | `verificacion-sin-respuesta.spec.ts` | 5570 |
| `msedge-acceso` | `msedge` | `acceso.spec.ts` | 5571 |
| `msedge-actualizar-costos` | `msedge` | `actualizar-costos.spec.ts` | 5572 |
| `msedge-articulos` | `msedge` | `articulos.spec.ts` | 5573 |
| `msedge-articulos-edicion` | `msedge` | `articulos-edicion.spec.ts` | 5574 |
| `msedge-backup-aviso` | `msedge` | `backup-aviso.spec.ts` | 5575 |
| `msedge-busqueda-en-vivo` | `msedge` | `busqueda-en-vivo.spec.ts` | 5576 |
| `msedge-configuracion` | `msedge` | `configuracion.spec.ts` | 5577 |
| `msedge-configuracion-backup` | `msedge` | `configuracion-backup.spec.ts` | 5578 |
| `msedge-configuracion-emisor` | `msedge` | `configuracion-emisor.spec.ts` | 5579 |
| `msedge-configuracion-recalculo` | `msedge` | `configuracion-recalculo.spec.ts` | 5580 |
| `msedge-estilo-uniforme` | `msedge` | `estilo-uniforme.spec.ts` | 5581 |
| `msedge-facturacion` | `msedge` | `facturacion.spec.ts` | 5582 |
| `msedge-facturacion-rechazo` | `msedge` | `facturacion-rechazo.spec.ts` | 5583 |
| `msedge-facturacion-sin-respuesta` | `msedge` | `facturacion-sin-respuesta.spec.ts` | 5584 |
| `msedge-facturas-busqueda` | `msedge` | `facturas-busqueda.spec.ts` | 5585 |
| `msedge-facturas-pdf` | `msedge` | `facturas-pdf.spec.ts` | 5586 |
| `msedge-inicio-menu` | `msedge` | `inicio-menu.spec.ts` | 5587 |
| `msedge-presupuestos-alta` | `msedge` | `presupuestos-alta.spec.ts` | 5588 |
| `msedge-presupuestos-busqueda` | `msedge` | `presupuestos-busqueda.spec.ts` | 5589 |
| `msedge-presupuestos-calculos` | `msedge` | `presupuestos-calculos.spec.ts` | 5590 |
| `msedge-presupuestos-concurrencia` | `msedge` | `presupuestos-concurrencia.spec.ts` | 5591 |
| `msedge-presupuestos-edicion` | `msedge` | `presupuestos-edicion.spec.ts` | 5592 |
| `msedge-presupuestos-pdf` | `msedge` | `presupuestos-pdf.spec.ts` | 5593 |
| `msedge-verificacion-consulta-sin-respuesta` | `msedge` | `verificacion-consulta-sin-respuesta.spec.ts` | 5594 |
| `msedge-verificacion-emite` | `msedge` | `verificacion-emite.spec.ts` | 5595 |
| `msedge-verificacion-importe-distinto` | `msedge` | `verificacion-importe-distinto.spec.ts` | 5596 |
| `msedge-verificacion-recupera` | `msedge` | `verificacion-recupera.spec.ts` | 5597 |
| `msedge-verificacion-sin-respuesta` | `msedge` | `verificacion-sin-respuesta.spec.ts` | 5598 |

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
`chrome-presupuestos-*` a 5551–5556 y los de Edge a 5557–5561 y 5565–5570, con los suyos en 5562–5564; y los cinco
`verificacion-*.spec.ts` (FEAT-008) ocupan 5557–5561 en Chrome, después de todos los demás, mueven los de Edge a
5562–5575 y tienen los suyos en 5576–5580; y `configuracion-emisor.spec.ts`, `facturas-busqueda.spec.ts` y
`facturas-pdf.spec.ts` (FEAT-009) ocupan 5547, 5552 y 5553 en Chrome, mueven `chrome-configuracion-recalculo` y los
`chrome-facturacion*` a 5548–5551 y los `chrome-presupuestos-*` y `chrome-verificacion-*` a 5554–5564, y los de Edge a
5565–5568, 5570–5573 y 5576–5586, con los suyos en 5569, 5574 y 5575; y `backup-aviso.spec.ts` y
`configuracion-backup.spec.ts` (FEAT-010) ocupan 5546 y 5548 en Chrome, mueven `chrome-configuracion` a 5547 y los que
siguen en Chrome a 5549–5566, y los de Edge a 5567–5569, 5571 y 5573–5590, con los suyos en 5570 y 5572; y
`actualizar-costos.spec.ts` (FEAT-011) ocupa 5544 en Chrome, entre `acceso` y `articulos`, mueve los que siguen en
Chrome a 5545–5567 y los de Edge a 5568 y 5570–5592, con el suyo en 5569; y `estilo-uniforme.spec.ts` e
`inicio-menu.spec.ts` (FEAT-012) ocupan 5552 y 5558 en Chrome, mueven `chrome-facturacion*` y `chrome-facturas-*` a
5553–5557 y los `chrome-presupuestos-*` y `chrome-verificacion-*` a 5559–5569, y los de Edge a 5570–5578, 5580–5584 y
5586–5596, con los suyos en 5579 y 5585; y `busqueda-en-vivo.spec.ts` (FEAT-013) ocupa 5548 en Chrome, entre
`backup-aviso` y `configuracion`, mueve los que siguen en Chrome a 5549–5570 y los de Edge a 5571–5575 y 5577–5598,
con el suyo en 5576. Son 56 proyectos, de 5543 a 5598. Si
tenés algo fijado a un puerto, buscá el actual en esta tabla o en la línea `[e2e] Listo: …` que escribe `servidor.mjs`
al arrancar.

**Carpetas de backup (FEAT-010, D-16).** La ubicación del backup tiene que estar en otra unidad que la base (FR-02).
Para probarlo, `servidor.mjs` crea por cada Api, antes de levantarla, una raíz `optica-e2e-backup-<puerto>` en la
carpeta temporal (el mismo punto de montaje que los datos de la Api) y otra en un segundo punto de montaje, `/dev/shm`
en Linux, si está en otro dispositivo que la carpeta temporal (`carpetasDeBackup` y `baseDeOtroMontaje` en
`compilacion.mjs`). El nombre lleva el puerto, que ya es único entre las Api que corren a la vez: así la prueba encuentra
sus carpetas con el puerto de su `baseURL`. Adentro están la carpeta de la misma unidad (`misma-unidad`, AC-02), la
de otra unidad en la que se puede escribir (`destino`, AC-01) y una sin permiso de escritura (`sin-escritura`, 555,
AC-03); la carpeta inexistente de AC-03 (`no-existe`) nunca se crea. Sin un segundo punto de montaje (Windows), el caso
positivo de AC-01 y la carpeta sin escritura se saltan con su motivo (los cubren las pruebas de .NET); la carpeta sin
escritura también se salta como root. La regla de la otra unidad nunca se desactiva con configuración.

Antes de crear o borrar una raíz, `revisarCarpetaDeBackup` comprueba que sea una temporal propia: exactamente
`<carpeta temporal o /dev/shm>/optica-e2e-backup-<puerto>`, absoluta y sin `..`. Cualquier otra ruta (por ejemplo, una
carpeta de backup real) se rechaza sin crear ni borrar nada y sin repetir la ruta en el mensaje. Una vez que
`configuracion-backup.spec.ts` graba el destino, el programador de esa Api puede hacer una copia ahí dentro de los 5
minutos siguientes: `servidor.mjs` borra las raíces después de detener las Api, y al arrancar borra las de corridas
anteriores que ya terminaron. Las raíces y `destino` se crean con permisos 700 (solo el usuario que corre la suite).
Antes de borrar, a `sin-escritura` se le devuelve el permiso de escritura solo si es una carpeta real (`lstat`): si es
un enlace simbólico no se le cambian los permisos (cambiarían los de lo apuntado) y se borra solo el enlace.

**Corridas anteriores.** Cada archivo `pid` (el del directorio temporal y el de cada raíz de backup) guarda el pid de
`servidor.mjs` y, en Linux, la hora de inicio de ese proceso (campo 22 de `/proc/<pid>/stat`). Una corrida sigue viva si
el proceso existe y su hora de inicio coincide: así un pid que otro proceso reutilizó no la hace pasar por viva. En
Windows no hay una forma portable de leer la hora de inicio y decide solo el pid. La limpieza ignora las carpetas de otro
usuario de la PC (`lstat().uid` distinto de `process.getuid()`, cuando existe). Si una raíz de backup es de una corrida
viva, `servidor.mjs` se detiene con un mensaje que dice cómo resolverlo: si no hay otra corrida, borrar las carpetas
`optica-e2e-backup-*` de la carpeta temporal y de `/dev/shm`, o correr con otro `OPTICA_E2E_PUERTO`.

**"Otra unidad" en el harness y en la Api.** La Api decide la unidad por el punto de montaje de prefijo más largo de
`DriveInfo.GetDrives`, que en Linux lee `/proc/self/mountinfo`. `baseDeOtroMontaje` usa el mismo criterio: lee
mountinfo y acepta `/dev/shm` solo si su punto de montaje es otro que el de la carpeta temporal. Si mountinfo no se
puede leer, compara `stat().dev`, y ese criterio puede no coincidir con el de la Api con btrfs (subvolúmenes con otro
`dev` en el mismo montaje), overlay o montajes bind (el mismo `dev` en otro montaje): en ese caso el harness puede creer
que hay otra unidad cuando la Api ve la misma, y AC-01 falla con el mensaje de la misma unidad (o se salta sin
necesidad). La solución es correr donde mountinfo se pueda leer.

`configuracion-backup.spec.ts` (AC-01 a AC-03) va en orden: primero los rechazos (misma unidad, inexistente y sin
escritura), cada uno con el mensaje junto al campo y sin ubicación grabada al volver a abrir la configuración, y al final
el destino de otra unidad, que se graba y se ve al volver a abrir y al recargar. `backup-aviso.spec.ts` (AC-10 y AC-15)
no elige ninguna ubicación: con la base nueva sin backups, el inicio muestra "Falta el backup de ayer. No hay ningún
backup disponible. No hay una ubicación de backup elegida." (`role="status"`, sin enlaces, D-13 y D-26), también al
volver a ingresar, y ni el inicio ni ninguna pantalla de su menú tiene un texto de restaurar o restablecer, un control
que combine backup o copia con cargar, importar, subir o volver, ni un campo para subir un archivo. La lista de
pantallas sale de los enlaces del inicio y se compara con la esperada, así una pantalla nueva del menú hace fallar la
prueba hasta que se suma. También recorre "Artículos → Actualizar costos con planilla" (FEAT-011, D-27), fuera del
menú, donde admite un único campo de archivo: `accept=".xlsx"` con la etiqueta "Planilla de costos (.xlsx)", que carga
costos y no restaura backups. Las pantallas de detalle (presupuesto, factura, editar artículo) no se recorren porque
la base está vacía: las cubren `ProteccionTests` en la Api y, en la web, una búsqueda de texto en el código
(`restaur` no aparece en `web/src` fuera de las pruebas); esa búsqueda no es una prueba automática.
Como todas las bases nuevas de la suite muestran ese aviso en el inicio, una prueba que busque `getByRole('status')`
tiene que filtrarlo por su texto (como `acceso.spec.ts`). Desde FEAT-012 la lista esperada sigue el orden del menú en
tarjetas (D-11): Nuevo presupuesto, Buscar presupuestos, Facturas, Artículos, Nuevo artículo, Configuración y Cambiar
la contraseña.

**Menú de Inicio y estilo uniforme (FEAT-012).** Las dos pruebas miden con la marca del harness (primario `#0903A0`
sobre fondo `#FFFFFF`, la real de NFR-01) y solo con estilos computados (`getComputedStyle` y las cajas del
navegador): no guardan capturas. Las medidas que comparten (el contraste WCAG desde los colores computados, la espera
de la marca y de las transiciones, y el ancho de la página) están en `tests/medidas.ts`, que como no termina en
`.spec.ts` no es un proyecto. Las dos fallan ante cualquier violación de la política de seguridad de contenido
(R-02). `inicio-menu.spec.ts` (AC-01, AC-02 y AC-04 a AC-07) no graba datos: verifica las secciones Ventas, Catálogo
y Sistema con sus opciones y destinos, el clic en la esquina de la tarjeta "Facturas" (fuera del nombre, de la
descripción y del ícono) que abre la búsqueda de facturas, el recorrido con Tab en el orden del menú con el contorno
computado en la tarjeta (3px del primario, separado 2px) y sin contorno propio del enlace, una sola columna sin
desplazamiento horizontal con 360 px y dos tarjetas o más en una fila con 1280 px, el ícono en el primario y el
contraste del texto del menú (4,5:1 o más) y del foco (3:1 o más), y que el recorrido no deje errores en la consola.
`estilo-uniforme.spec.ts` (AC-11 a AC-16, D-20) siembra por la Api, en cada prueba, un artículo, un presupuesto en
Final con su factura (ARCA simulada) y otro en Borrador para la pantalla de modificación, con códigos `EU-<puerto>-<n>`
y un cliente ficticio. Recorre las 15 pantallas por su ruta con 360 px y 1280 px y verifica: la tarjeta de cada
pantalla con el fondo, el borde, el radio y la sombra computados de una opción del menú (Inicio sin tarjeta exterior,
D-15); la página sin desplazamiento horizontal y las tablas con `overflow-x: auto` en su contenedor; el mismo alto,
radio y colores de los botones principales y secundarios, también los de las filas; el campo inválido al enviar vacío
(cinco formularios) con el borde de error, el mensaje junto al campo y el contorno del primario con el foco; el mismo
bloque de error (borde lateral, fondo y prefijo "✕") en cuatro pantallas de detalle con un número inexistente y en la
definición de la contraseña rechazada; y el contraste de 4,5:1 o más de cada texto visible, campo y botón habilitado,
calculado desde los colores computados sobre su fondo efectivo.

**ARCA en la suite (FEAT-007).** Todas las Api de la suite arrancan con ARCA simulada (`Arca__Entorno=Simulado`), con
un tiempo máximo de 2 s (`Arca__TiempoMaximoSegundos=2`) y sin CUIT ni certificado (`Arca__Cuit`,
`Arca__Certificado__Ruta` y `Arca__Certificado__Clave` vacíos). Antes, `entornoDeLaApi` (`compilacion.mjs`) quita
del entorno de quien corre la suite toda clave de ARCA, en cualquier mayúscula, con `__` o `:` y también con los
prefijos `ASPNETCORE_` y `DOTNET_` (por ejemplo, `ARCA__ENTORNO=Produccion`). Así ninguna prueba llega a ARCA, ni a
homologación ni a producción, y sus comprobantes no tienen validez fiscal. La revisión de hosts externos de
`npm run test:unidad` admite solo las direcciones de homologación de WSAA y WSFEv1 y los espacios de nombres SOAP que
escriben las pruebas de .NET del cliente de ARCA, cada uno con su ruta exacta; producción nunca.

Desde FEAT-008 las Api arrancan además con una espera corta antes de poder verificar una emisión pendiente
(`Arca__EsperaVerificacionSegundos`, D-20; 3 s salvo los dos archivos de la tabla de abajo) en lugar del minuto de la
óptica: la Api exige que la espera sea al menos el tiempo máximo más el margen del envío (el menor entre 10 s y un
tercio del tiempo máximo; D-31), y 3 s alcanza para 2 + 0,67 s. La respuesta del simulador (`Arca__Simulado__Respuesta`), su consulta por número
(`Arca__Simulado__Consulta`) y el contenido inicial de su archivo (`arca-simulado.json` en el directorio de datos de la
Api) salen del archivo de pruebas, que `servidor.mjs` conoce por el nombre del proyecto (`ARCA_POR_ARCHIVO`,
`variablesDeArca` y `contadorSimulado` en `compilacion.mjs`):

| Archivo | Respuesta | Consulta | Espera | Archivo del simulador |
|---|---|---|---|---|
| `facturacion.spec.ts` | `autorizar` | `responder` | 3 s | contador en 124 (\*) |
| `facturacion-rechazo.spec.ts` | `rechazar` | `responder` | 3 s | sin archivo |
| `facturacion-sin-respuesta.spec.ts` | `no_responder` | `responder` | 3 s | sin archivo |
| `facturas-pdf.spec.ts` | `autorizar` | `responder` | 3 s | Factura B en 124 (\*\*) |
| `facturas-busqueda.spec.ts` | `autorizar` | `responder` | 3 s | Factura B en 124 y Factura C en 34560 (\*\*) |
| `verificacion-recupera.spec.ts` | `autorizar_sin_responder` | `responder` | 3 s | sin archivo |
| `verificacion-emite.spec.ts` | `no_responder_una_vez` | `responder` | 8 s | sin archivo |
| `verificacion-sin-respuesta.spec.ts` | `no_responder` | `responder` | 6 s | sin archivo |
| `verificacion-consulta-sin-respuesta.spec.ts` | `no_responder` | `no_responder` | 3 s | sin archivo |
| `verificacion-importe-distinto.spec.ts` | `no_responder_una_vez` | `responder` | 3 s | sin archivo |
| Todos los demás | `autorizar` | `responder` | 3 s | sin archivo |

(\*) La Factura B del punto de venta 3 arranca en 124: la primera autorizada es la 0003-00000125 (AC-19 y AC-30 de
FEAT-007).

(\*\*) FEAT-009: en los dos archivos la Factura B del punto de venta 3 arranca en 124 (`'B-3': 124`), así la primera
es la 0003-00000125 (AC-08 y AC-23); en `facturas-busqueda.spec.ts` la Factura C del punto de venta 3 arranca además
en 34560 (`'C-3': 34560`), así la primera es la 0003-00034561 (AC-19). El harness revisa los contadores como los lee el
simulador (claves `B-<pv>` o `C-<pv>`, enteros de 0 a 99999999) y se detiene antes de compilar si uno no sirve.

La espera (`Arca__EsperaVerificacionSegundos`) es de 3 s salvo en dos archivos que tienen que hacer un pedido antes de
que pase: `verificacion-emite.spec.ts` (8 s: la otra pestaña pide verificar durante la emisión, AC-17) y
`verificacion-sin-respuesta.spec.ts` (6 s: el pedido que comprueba la pendiente nueva, AC-12). Así ese pedido tiene
varios segundos de margen en lugar de menos de uno.

`servidor.mjs` revisa ese mapa antes de compilar (`revisarArcaPorArchivo`): una propiedad desconocida, un valor que la
Api no admite (comparado exacto, en minúsculas) o una espera que no sea un entero de 3 a 3600 (D-31 con 2 s de tiempo
máximo) lo detiene con código 1 y un mensaje que nombra el archivo de pruebas.

`facturacion.spec.ts` (FEAT-007, AC-01, AC-02, AC-03, AC-13, AC-18, AC-19, AC-30 y AC-32) emite la factura desde la
pantalla del presupuesto. Sus pruebas van en orden: primero la configuración incompleta (la base nueva no tiene tope
ni punto de venta, y el punto de venta no se puede borrar una vez cargado), que no consume ningún número; después la
emisión de la Factura B 0003-00000125 de un Final de 3 líneas y $3.000,00, que el presupuesto muestra también al
volver a abrirlo; y al final un Borrador y un presupuesto ya facturado, con el botón deshabilitado y la Api rechazando
el pedido directo (409 `borrador` y `facturado`). `facturacion-rechazo.spec.ts` (AC-20) muestra "ARCA rechazó la
factura: 10016 – …" con Facturar habilitado y vuelve a emitir; `facturacion-sin-respuesta.spec.ts` (AC-21 y AC-22)
espera los 2 s del tiempo máximo, muestra "ARCA no respondió. La emisión quedó pendiente de verificación.", muestra
"Verificar con ARCA" en lugar de Facturar (desde FEAT-008) también al volver a abrir el presupuesto y comprueba que un
pedido directo recibe 409 `pendiente` sin esperar a ARCA. Las tres cargan la configuración por la Api antes de emitir, crean sus artículos con
códigos propios (`FA-…`, `FR-…`, `FS-…`) y graban sus presupuestos en Borrador y después en Final. Los clientes son
ficticios, con el DNI 00.718.294 (un DNI que empieza con 00 no se emite, D-50 de FEAT-007) y emails en `example.com`.

Los cinco `verificacion-*.spec.ts` (FEAT-008) dejan una emisión pendiente desde la pantalla y la verifican con
"Verificar con ARCA" pasada la espera de su Api (la pendiente se graba antes de enviar a ARCA, así que contada desde la
respuesta de la emisión ya pasó). `verificacion-recupera.spec.ts` (AC-02 a AC-05) va en orden: primero ingresa en
la base nueva sin aviso de pendientes (AC-04); después deja dos pendientes, sale y vuelve a ingresar, ve el aviso con
los dos números y sus enlaces (AC-03), abre uno con "Verificar con ARCA" y sin Facturar (AC-02) y lo verifica: recupera
la 0003-00000001 que ARCA ya tenía, sin emitir otra (AC-05); resueltas las dos, el aviso desaparece.
`verificacion-emite.spec.ts` (AC-17 y AC-06) abre el presupuesto en otra pestaña antes de emitir; mientras la emisión
todavía espera a ARCA, esa pestaña vuelve a leer el presupuesto y pide verificar: recibe los segundos que faltan, sin consultar ni emitir; pasada la espera, la verificación
emite la 0003-00000001. `verificacion-sin-respuesta.spec.ts` (AC-12) verifica, la nueva emisión tampoco tiene
respuesta y la emisión sigue pendiente con su número nuevo (un pedido enseguida recibe otra vez la espera, porque la
pendiente es nueva). `verificacion-consulta-sin-respuesta.spec.ts` (AC-11) verifica con la consulta sin respuesta:
2 s, "ARCA no respondió. La emisión sigue pendiente de verificación." y un solo pedido.
`verificacion-importe-distinto.spec.ts` (AC-07 a AC-09) deja pendiente un presupuesto de $ 1.000,00 con la
0003-00000001, que el simulador no registra, factura otro de $ 2.000,00 (ARCA le autoriza ese mismo número) y verifica
el primero: aviso de revisar el punto de venta con el número y los dos importes; cancelar el descarte deja todo igual
y confirmarlo vuelve Facturar, que emite la 0003-00000002. No siembra el archivo del simulador: el simulador no admite
un comprobante con número mayor que su contador (D-27 de FEAT-008). AC-15 y AC-16 (pendientes sin número o de otro
entorno) no tienen e2e: la suite no puede sembrar la base (D-06 de FEAT-008). Los archivos con
`no_responder_una_vez` tienen una sola prueba, porque la Api cuenta "la primera solicitud" una vez por proceso. Los
artículos usan códigos propios (`VR-…`, `VE-…`, `VS-…`, `VC-…`, `VI-…`) y los clientes son ficticios, con el DNI
00.718.294 y emails en `example.com`.

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

`busqueda-en-vivo.spec.ts` (FEAT-013, AC-01 a AC-14 y R-04) escribe en los cuatro buscadores (Artículos, Buscar
presupuestos, Facturas y el buscador de artículos del presupuesto) y en los dos campos con filtro de Configuración, y
verifica las sugerencias (hasta 8, con la tabla filtrada por la misma búsqueda), que elegir una abre el artículo, el
presupuesto o la factura o agrega la línea, el teclado (flechas, Enter, Escape y Tab, con `aria-activedescendant` y
`aria-selected`), el anuncio que dice solo la cantidad ("8 sugerencias"), que "Buscar" y Enter buscan como antes (con un
texto de 1 carácter, que en vivo no se pide), que una condición fiscal que no está en la lista no se graba, que una
búsqueda anterior no pisa la tabla ni las sugerencias de la última (AC-09: `page.route` retiene la respuesta vieja hasta
que la prueba la libera, sin tiempos fijos; en el navegador la búsqueda nueva cancela el pedido viejo, así que la e2e
cubre la cancelación, y la respuesta que llega tarde y se descarta por número de pedido la cubre
`useBusquedaEnVivo_RespuestaViejaDescartada` en Vitest), que una búsqueda que falla por un error de la Api (un 500) o de
la red (`route.abort('failed')`) oculta las sugerencias y muestra el mensaje de error de hoy (AC-10; el aviso de recurso
de cada falla provocada es el único error de consola que cada prueba descuenta), y que escribir "armazón" seguido hace
un solo pedido y muestra las sugerencias en menos de 1 s desde la última tecla, medido en el navegador con
`performance.now()`. El volumen de 10.000 artículos de AC-13 se mide en la Api (`Volumen_BuscarArmazon_P95MenorA700ms`,
p95 menor a 700 ms, D-17). Siembra por la Api artículos con una palabra propia por prueba, Borradores de relleno hasta
el 154 para que el de Pérez sea el 155 (AC-04; falla si la base ya lo pasó) y una factura de ARCA simulada; los
presupuestos se graban en Borrador y después pasan a Final, y los clientes son ficticios, con DNI que empiezan con 00.
Las sugerencias de presupuestos y facturas no muestran el DNI (R-02). Una descripción con HTML (`<img … onerror=…>`)
tiene que verse como texto. Cualquier violación de la CSP, o cualquier error en la consola después de iniciar la sesión,
hace fallar la prueba. Las pruebas corren en el orden del archivo y la primera necesita la base vacía (sus artículos son
el 1 al 10, así el 7 de AC-03 es uno de ellos); las demás no dependen de lo que dejó otra.

Desde FEAT-013 la alícuota y la condición fiscal de Configuración, "Texto a buscar" y los filtros de texto de las dos
búsquedas son campos con sugerencias (`role="combobox"`), y su etiqueta nombra también la lista de sugerencias
(`aria-labelledby`): `getByLabel(etiqueta, { exact: true })` encuentra dos elementos, así que los specs los buscan con
`getByRole('combobox', { name })`. Para elegir una alícuota o una condición escriben parte de la opción y la tocan en
la lista (o escriben la etiqueta entera, que vale como elegirla); el campo muestra la etiqueta ("10,5", "Responsable
Inscripto"), no el código. Las búsquedas de presupuestos y facturas esperan la respuesta y no el pedido: un pedido en
vivo que "Buscar" cancela no tiene respuesta.

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
dejó la prueba anterior (`articulos.spec.ts`, `configuracion-backup.spec.ts`, `configuracion-recalculo.spec.ts`, `facturacion.spec.ts`,
`verificacion-recupera.spec.ts` y la primera prueba de `presupuestos-alta.spec.ts`, por ejemplo); `articulos-edicion.spec.ts`, `presupuestos-busqueda.spec.ts`,
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
(un proyecto `chromium-<archivo>` por cada archivo de la tabla).
Es solo un diagnóstico: no reemplaza a `chrome` y `msedge` ni cuenta para NFR-03.
