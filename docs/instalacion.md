# Instalación en la PC del local — Óptica Sistema

Procedimiento para instalar la aplicación en la PC de la óptica (Windows 10 u 11). Corresponde a FEAT-001a
(ingreso, sesión y marca) y FEAT-001b (bloqueo por intentos fallidos, cambio y restablecimiento de la contraseña).
Seguí los pasos en orden: el paso 6 (definir la contraseña) se hace **antes** de conectar la aplicación a la red local.
El backup automático (FEAT-010) se configura en la sección 9, y la restauración de un backup está en la sección 10.
Si se olvida la contraseña, el restablecimiento en la PC está en la sección 11. La facturación electrónica con ARCA
(FEAT-007) se configura en la sección 12: **sin esa configuración la aplicación factura en un entorno simulado, sin
validez fiscal**.

> **Bloqueo por intentos fallidos.** Desde FEAT-001b, después de 5 intentos de ingreso fallidos seguidos el ingreso
> queda bloqueado 5 minutos, también si se reinicia la Api (riesgo R-03 del modelo de amenazas, ya mitigado). Si la
> hora de la PC retrocede durante el bloqueo o poco después de que termine (por ejemplo, al sincronizarse sola), el
> bloqueo puede extenderse hasta 10 minutos desde ese momento (FIX-001); si no se puede esperar, la contraseña se
> restablece desde la PC (sección 11).
> Antes de cargar datos reales de clientes, revisá la sección 8.

## 1. Qué se instala y dónde

Nada de lo que se instala vive dentro del repositorio. Usá estas carpetas (podés elegir otras, siempre con rutas
absolutas y fuera de la copia del código):

| Carpeta | Contenido | Clave de configuración |
|---|---|---|
| `C:\OpticaSistema\App` | La Api publicada (`Optica.Api.exe` y sus archivos) | — |
| `C:\OpticaSistema\App\wwwroot` | La interfaz web compilada (`index.html` y `assets\`) | `Optica:Web:Directorio` |
| `C:\OpticaSistema\Marca` | La marca real: `branding.json` y el logo | `Optica:Marca:Directorio` |
| `%LOCALAPPDATA%\OpticaSistema` de la cuenta que ejecuta la Api | La base `optica.db`, las claves de sesión (`claves\`), el candado de instancia (`optica.instancia`) y los archivos de ARCA: `arca-simulado.json` (contador y comprobantes del simulador) y `arca-ta-<entorno>.json` (ticket de acceso a ARCA, cifrado; sección 12). Después de una restauración, también la carpeta `anterior\` (sección 10) | `Optica:Datos:Directorio` (vacía = esta ubicación) |
| Fuera del repositorio y de las carpetas anteriores, con acceso solo para la cuenta de la Api | El certificado de ARCA (`.pfx`), solo si se factura con ARCA (sección 12) | `Arca:Certificado:Ruta` |

`optica.instancia` es un archivo vacío que la Api mantiene abierto en exclusiva mientras corre: una segunda Api sobre la
misma carpeta de datos no arranca (registra un error crítico y termina), y la restauración de la sección 10 y el
restablecimiento de la sección 11 se niegan mientras la Api está en marcha. No lo borres con la Api en marcha; con la Api detenida no hace falta tocarlo.

> **`Optica:Web:Directorio` nunca apunta a la carpeta de la aplicación** (`C:\OpticaSistema\App`) ni a una carpeta que
> la contenga (`C:\OpticaSistema`, `C:\`): todo lo que hay en esa carpeta se serviría sin sesión, incluidos
> `appsettings.Production.json`, los ensamblados y los PDB. **Tampoco apunta nunca a la carpeta de datos**
> (`Optica:Datos:Directorio`) ni a una que la contenga: ahí están la base y las claves de sesión (`claves\`). Va siempre
> a la subcarpeta `wwwroot`. Si se configura mal, la Api no sirve ninguna pantalla (responde 404) y registra una
> advertencia que nombra la clave.

La Api se ejecuta con una cuenta de Windows de la PC (en este documento, "la cuenta de la Api"). Conviene que sea una
cuenta estándar, sin permisos de administración. Quien administra la PC usa otra cuenta.

## 2. Preparar la publicación (en la PC de desarrollo)

1. En `web\`: `npm ci` y `npm run build`. El resultado queda en `web\dist\`.
2. En la raíz del repositorio:

   ```powershell
   dotnet publish src\Optica.Api\Optica.Api.csproj -c Release -r win-x64 --self-contained false -o C:\Publicacion\OpticaSistema
   ```

   Publicá **siempre con `-r win-x64`**. El generador del PDF de los presupuestos (QuestPDF, ADR-018) trae binarios
   nativos para 8 plataformas: sin `-r` se copian todos (unos 85 MB); con `-r win-x64`, solo los de Windows (unos
   12 MB), que son los únicos que usa la PC del local (D-34 de FEAT-006, ADR-020). No pongas `RuntimeIdentifier` en el
   `.csproj`: la CI corre en Linux y necesita los binarios de Linux.

   Publicá **siempre en Release**. En Release, `Directory.Build.props` reemplaza la ruta del repositorio por `/_/` en
   los ensamblados y en los PDB, y desactiva Source Link: así ni las trazas de error ni los PDB llevan la carpeta de
   usuario (con su nombre) de quien compiló ni la dirección del repositorio en GitHub (con el usuario de GitHub). La
   prueba `PublicacionSinRutasTests` (categoría `Publicacion`) lo comprueba sobre una publicación real. Una compilación
   Debug sí lleva esas rutas y Source Link: **no la instales**. Si preferís no distribuir los PDB,
   agregá `-p:DebugType=none` al comando; las trazas pierden los números de línea. Con `-r win-x64` se obtiene
   `Optica.Api.exe` también al publicar desde Linux o WSL.
3. Copiá el contenido de `web\dist\` a `C:\Publicacion\OpticaSistema\wwwroot\`.
4. Llevá la carpeta `C:\Publicacion\OpticaSistema` a la PC del local como `C:\OpticaSistema\App`.

No uses `npm run dev` (el servidor de desarrollo de Vite) contra una Api con datos reales: es una herramienta de
desarrollo, y fuera de `localhost` necesita un certificado de confianza.

## 3. Copiar la marca real

La marca real de la óptica no está en el repositorio (es público). Copiala desde su ubicación privada a
`C:\OpticaSistema\Marca`:

- `branding.json`, con el esquema `{ "logo": "logo.png", "colores": { "primario": "#RRGGBB", "fondo": "#RRGGBB" } }`;
- el archivo del logo que nombra `branding.json` (`.png`, `.jpg` o `.svg`, de hasta 1 MB).

En esa carpeta va **solo la marca**: el logo se sirve sin sesión a cualquier equipo de la red local. Si la carpeta
falta o la marca es inválida, la aplicación muestra la marca de ejemplo y registra una advertencia.

**Cambiar la marca exige reiniciar la Api:** se lee una vez al arrancar y queda en memoria.

## 4. Permisos NTFS

Ejecutá en una consola de PowerShell como administrador, reemplazando `PC\CuentaApi` por la cuenta de la Api. Los
grupos van por SID para que funcionen en Windows en cualquier idioma (`*S-1-5-32-544` = Administradores,
`*S-1-5-18` = SYSTEM).

Carpeta de marca: lectura para la cuenta de la Api; escritura solo para quien administra.

```powershell
icacls C:\OpticaSistema\Marca /inheritance:r /grant:r "*S-1-5-32-544:(OI)(CI)F" "*S-1-5-18:(OI)(CI)F" "PC\CuentaApi:(OI)(CI)RX"
```

Carpeta de la aplicación: lo mismo (la Api no necesita escribir en ella).

```powershell
icacls C:\OpticaSistema\App /inheritance:r /grant:r "*S-1-5-32-544:(OI)(CI)F" "*S-1-5-18:(OI)(CI)F" "PC\CuentaApi:(OI)(CI)RX"
```

La carpeta de datos (`%LOCALAPPDATA%\OpticaSistema` de la cuenta de la Api) ya queda, por defecto, accesible solo para
esa cuenta, SYSTEM y Administradores. No la compartas en red.

Carpeta de backups (sección 9): la elige quien administra en la configuración, y las copias las escribe la Api con su
cuenta. Control total para Administradores y SYSTEM, y modificación para la cuenta de la Api: la necesita para escribir
cada copia, renombrarla después de verificarla y borrar las que ya no se conservan. Ninguna otra cuenta la lee: los
backups contienen el hash de la contraseña, los eventos de acceso (fecha y hora de cada ingreso, rechazo, bloqueo,
cambio y restablecimiento: datos personales de las operadoras, riesgo R-14) y los datos personales de los clientes de
cada presupuesto y de cada factura (apellido, nombre, DNI, domicilio, email y teléfono). Creá la carpeta y aplicale los
permisos **antes** de elegirla en la configuración (el comando está al final de esta sección).

- **Disco externo o pendrive NTFS:** aplicale los mismos permisos.
- **Pendrive o disco con FAT32 o exFAT:** esos formatos no guardan permisos, y cualquiera que lo conecte a una PC puede
  leer las copias. Mientras no esté conectado a la PC del local, guardalo bajo llave (sección 9).
- **Carpeta de OneDrive, Google Drive o Dropbox:** los permisos de la PC protegen la carpeta en este disco, pero no la
  copia en la nube. La cuenta de la nube se protege como se indica en la sección 9 (contraseña propia y verificación en
  dos pasos).

La carpeta `anterior\` que deja una restauración (sección 10) está dentro de la carpeta de datos y hereda sus permisos:
no la saques de ahí ni la compartas.

> **Datos personales de clientes sin cifrar (Ley 25.326).** La base (`optica.db` y `optica.db-wal`) y **cada copia**
> de backup guardan el DNI, el domicilio, el email y el teléfono de los clientes **en claro**: quien pueda leer el
> archivo puede leer esos datos, que están alcanzados por la Ley 25.326 de Protección de los Datos Personales. Lo único
> que los protege es la ubicación fuera del árbol de fuentes y los permisos NTFS de esta sección: la base, los de la
> carpeta de datos, y los backups, los de la carpeta de backups. El cifrado de la base no está implementado: es un
> riesgo aceptado mientras el sistema se usa como software de prueba, y se tiene que revisar antes de cargar datos
> reales de clientes, de pasar a producción o si la PC se comparte (R-01 del modelo de amenazas de FEAT-003a, D-23 y
> D-27). Las copias de backup también se guardan sin cifrar: lo decidieron las dueñas (FEAT-010, D-04, riesgo R-06
> aceptado), y las protegen el pendrive bajo llave, la cuenta de la nube con contraseña propia y verificación en dos
> pasos, y los permisos de esta sección (ver la sección 9). Esa decisión se revisa si se pierde un pendrive o la cuenta de la
> nube, si cambia la normativa de datos personales o antes de que un tercero administre las copias.

> **El PDF del presupuesto lleva los datos del cliente (Ley 25.326).** El PDF que se descarga desde un presupuesto en
> Final incluye el apellido, el nombre, el DNI, el domicilio, el email y el teléfono que el cliente haya dado, y el
> navegador lo guarda en la carpeta **Descargas** de la cuenta de Windows que lo descargó. Ahí ya no lo protege el
> sistema: borralo cuando lo hayas entregado o adjuntado, no lo dejes en una carpeta compartida y, si la PC la usan
> varias personas, usá una cuenta de Windows por operadora (riesgo R-08 aceptado por las dueñas, FEAT-006, D-38).
>
> **Licencia de QuestPDF.** La generación del PDF usa QuestPDF con la licencia Community, gratuita solo si la empresa
> factura menos de USD 1 millón por año (ADR-018). Si la óptica supera ese monto, hace falta comprar una licencia o
> reemplazar el generador.

```powershell
New-Item -ItemType Directory -Force D:\Backups\OpticaSistema
icacls D:\Backups\OpticaSistema /inheritance:r /grant:r "*S-1-5-32-544:(OI)(CI)F" "*S-1-5-18:(OI)(CI)F" "PC\CuentaApi:(OI)(CI)M"
```

## 5. Certificado HTTPS en el almacén de Windows

La aplicación solo escucha por HTTPS. El certificado se guarda en el almacén de certificados de Windows de la cuenta de
la Api (`CurrentUser\My`), con el sujeto `CN=OpticaSistema`. **Ni el certificado ni ninguna contraseña van en
`appsettings.json` ni en el repositorio.**

1. Iniciá sesión con la cuenta de la Api y, en PowerShell (sin elevar), creá el certificado. Reemplazá `NOMBRE-PC` por
   el nombre de la PC en la red local (comando `hostname`):

   ```powershell
   $cert = New-SelfSignedCertificate -Subject "CN=OpticaSistema" -DnsName "NOMBRE-PC","localhost" `
     -CertStoreLocation Cert:\CurrentUser\My -KeyExportPolicy NonExportable -NotAfter (Get-Date).AddYears(3) `
     -TextExtension @("2.5.29.19={critical}{text}ca=false")
   Export-Certificate -Cert $cert -FilePath C:\OpticaSistema\OpticaSistema.cer
   $cert.Thumbprint
   Import-Certificate -FilePath C:\OpticaSistema\OpticaSistema.cer -CertStoreLocation Cert:\CurrentUser\Root
   ```

   `-TextExtension` marca el certificado como **no** perteneciente a una autoridad de certificación (`ca=false`,
   restricción básica crítica): aunque quede entre las raíces de confianza, no sirve para firmar otros certificados.
   La clave privada no se puede exportar y no sale de la PC. El archivo `.cer` contiene solo la parte pública; no lo
   subas al repositorio (`*.cer` está en `.gitignore`).

   **Aviso de Windows al importar en `CurrentUser\Root`:** aparece la ventana "Advertencia de seguridad" ("Está a punto
   de instalar un certificado desde una entidad de certificación…", que afirma representar a `OpticaSistema`). Antes
   de tocar **Sí**, comprobá que la huella digital (*thumbprint*) que muestra es la que imprimió `$cert.Thumbprint`. Si
   no coincide, tocá **No**: es otro certificado. Si la ventana no aparece porque la consola no tiene escritorio (por
   ejemplo, una sesión remota sin interfaz), el comando falla: repetilo desde una sesión con escritorio.
2. **Las otras cuentas de Windows de la PC del local** que usen el navegador para abrir el sistema (por ejemplo, la
   cuenta de quien atiende, distinta de la cuenta de la Api) también tienen que confiar en el certificado: el almacén
   `CurrentUser\Root` es de cada cuenta. Iniciá sesión con cada una y, en PowerShell sin elevar:

   ```powershell
   Import-Certificate -FilePath C:\OpticaSistema\OpticaSistema.cer -CertStoreLocation Cert:\CurrentUser\Root
   ```

   con la misma comprobación de la huella en el aviso. Como alternativa, quien administra puede importarlo una sola vez
   para todas las cuentas en `Cert:\LocalMachine\Root` (PowerShell como administrador; no muestra el aviso).
3. En cada **otra PC** de la red local que vaya a usar el sistema, importá `OpticaSistema.cer` en "Entidades de
   certificación raíz de confianza" de cada cuenta que use el navegador (el mismo comando, o doble clic en el `.cer` →
   Instalar certificado → Usuario actual → "Entidades de certificación raíz de confianza"), comprobando la huella.
4. Cuando el certificado venza, repetí los pasos 1 a 3 y reiniciá la Api. Borrá el certificado viejo de
   `CurrentUser\My` y de los almacenes `Root` donde lo importaste.

## 6. Configuración y definición de la contraseña (antes de conectar a la red)

R-02 del modelo de amenazas: mientras no haya contraseña, cualquiera que llegue a la aplicación puede definirla. Por
eso la contraseña se define **en el mismo acto de instalar, desde la propia PC, antes de exponer la aplicación a la
red local**.

1. Creá `C:\OpticaSistema\App\appsettings.Production.json` (está en `.gitignore`; nunca lo subas al repositorio) con la
   configuración local, escuchando **solo en `localhost`**:

   ```json
   {
     "AllowedHosts": "localhost",
     "Kestrel": {
       "Endpoints": { "Https": { "Url": "https://localhost:5001" } },
       "Certificates": {
         "Default": { "Store": "My", "Location": "CurrentUser", "Subject": "OpticaSistema" }
       }
     },
     "Logging": {
       "LogLevel": { "Microsoft.EntityFrameworkCore": "Warning" }
     },
     "Optica": {
       "Datos": { "Directorio": "" },
       "Marca": { "Directorio": "C:\\OpticaSistema\\Marca" },
       "Web": { "Directorio": "C:\\OpticaSistema\\App\\wwwroot" }
     }
   }
   ```

   `Optica:Datos:Directorio` vacío usa `%LOCALAPPDATA%\OpticaSistema` de la cuenta de la Api. Si configurás otra
   carpeta, tiene que ser una ruta absoluta fuera del repositorio.
2. Con la cuenta de la Api, ejecutá `C:\OpticaSistema\App\Optica.Api.exe` (desde esa carpeta).
3. En el navegador de la misma PC, abrí `https://localhost:5001`. La aplicación muestra "Definí la contraseña de
   acceso". Ingresá la contraseña elegida por las dueñas (de 8 a 128 caracteres) y confirmá.
4. Comprobá que aparece "Sesión iniciada" con el logo y los colores de la óptica. Tocá **Salir** y volvé a ingresar
   para verificar la contraseña.

## 7. Habilitar el acceso desde la red local

Recién con la contraseña definida:

1. En `appsettings.Production.json`, cambiá el endpoint y los hosts permitidos:

   ```json
   "AllowedHosts": "localhost;NOMBRE-PC",
   "Kestrel": {
     "Endpoints": { "Https": { "Url": "https://*:5001" } },
     ...
   }
   ```

   `AllowedHosts` es la lista, separada por `;`, de los nombres con los que se puede abrir la aplicación: un pedido con
   otro nombre recibe 400. Agregá la IP de la PC solo si se va a abrir por IP (y entonces el certificado tiene que
   incluirla). No uses `*`.
2. Abrí el puerto solo para la red privada (PowerShell como administrador):

   ```powershell
   New-NetFirewallRule -DisplayName "Optica Sistema (HTTPS)" -Direction Inbound -Protocol TCP -LocalPort 5001 -Profile Private -Action Allow
   ```

   La aplicación no se publica en internet: no abras ese puerto en el router. La red de la óptica tiene que estar
   marcada como **privada** en Windows (Configuración → Red e Internet → la conexión → Tipo de perfil de red: Privada);
   en una red pública (por ejemplo, un wifi de clientes) la regla no se aplica, y está bien que así sea.
3. Reiniciá la Api. La primera vez que escucha en la red, **Windows puede mostrar el aviso del firewall** ("Firewall
   de Windows Defender bloqueó algunas características de esta aplicación", para `Optica.Api.exe`). Marcá **solo
   "Redes privadas"**, desmarcá "Redes públicas" y tocá **Permitir acceso** (pide la contraseña de quien administra).
   Si alguien ya permitió el acceso en redes públicas, quitá esa regla en "Firewall de Windows Defender con seguridad
   avanzada" → Reglas de entrada (la de `Optica.Api.exe` con perfil Público).
4. Probá desde otra PC: `https://NOMBRE-PC:5001`.

## 8. Restricciones de operación

- **Registro (log):** no subas la categoría `Microsoft.EntityFrameworkCore` a `Debug` ni a `Trace`: a ese nivel EF Core
  registra la ruta completa de la base, que incluye el nombre de usuario de Windows. Dejala en `Warning`. Tampoco bajes
  `Microsoft.AspNetCore` de `Warning`: en `Information` registra la dirección de cada pedido con lo que se buscó en el
  catálogo, y en `Trace` los datos recibidos. La Api fija un piso propio para esas categorías (FEAT-002b, D-28) que le
  gana a la configuración general; no agregues reglas por proveedor (`Logging:<Proveedor>:LogLevel:...`), que sí lo
  vencen.
- **Nunca actives `EnableSensitiveDataLogging`** de EF Core: pone los valores de los parámetros y de las entidades
  (entre ellos, el hash de la contraseña) en el log y en los mensajes de excepción. **Tampoco `EnableDetailedErrors`**:
  por sí solo no muestra valores, pero agrega a los errores de lectura detalles del esquema (tablas, columnas,
  propiedades), y junto con el anterior también los valores. Ninguno de los dos es para producción.
- **No actives el registro HTTP de pedidos** (`AddHttpLogging` o `W3CLogging`), y menos con cuerpos: los pedidos de
  definir e ingresar llevan la contraseña en claro.
- **PDB:** instalá solo la publicación Release del paso 2 (rutas reemplazadas por `/_/` y Source Link desactivado, ver
  §2), o publicá sin PDB.
- **Interfaz web (`wwwroot`):** para actualizarla, detené la Api, reemplazá el contenido de `wwwroot` y volvé a
  iniciarla. Con la Api en marcha, mientras falta `index.html` las pantallas responden 404, y un navegador puede
  recibir una mezcla de archivos de la versión anterior y de la nueva.
- **`Optica:Web:Directorio`:** nunca apunta a la carpeta de datos (`Optica:Datos:Directorio`) ni a la de la aplicación
  (ver §1).
- **Marca:** después de cambiar `branding.json` o el logo, reiniciá la Api.
- **`npm run dev`:** nunca contra una Api con datos reales.
- **Datos reales:** con FEAT-001b instalado, el ingreso ya se bloquea después de 5 intentos fallidos (R-03). Antes de
  cargar datos reales de clientes hay que revisar la retención de los eventos de acceso (R-14): hoy se guardan sin
  límite, en la base y en cada backup (sección 9). También hay que revisar el riesgo aceptado de la base y los backups
  sin cifrar, que guardan los datos personales de los clientes en claro (R-01 de FEAT-003a, D-27; ver secciones 4 y 9).
- **Restablecimiento:** solo con la Api detenida y desde la PC, con la cuenta de la Api (sección 11). No hay pantalla
  ni dirección web para restablecer la contraseña, y no hay que agregarla.
- **Restauración de un backup:** solo con la Api detenida y desde la PC, con la cuenta de la Api y el comando
  `restaurar-backup` (sección 10). No hay pantalla ni dirección web para restaurar, y no hay que agregarla. Nunca
  restaures copiando archivos a mano sobre `optica.db`.

## 9. Copia de seguridad (backup)

**La aplicación hace el backup sola**, mientras la Api está en marcha (FEAT-010):

- **Todos los días a las 20:00** hace el backup del día.
- **Al iniciarse**, si falta el backup de ayer (por ejemplo, porque la PC estaba apagada a las 20:00), hace uno
  enseguida.

Cada copia se llama `optica-AAAA-MM-DD.db`, con la fecha del día en que se hizo (por ejemplo, `optica-2026-11-02.db`).
Se hace con la API de backup de SQLite, que saca una copia completa y coherente aunque se esté trabajando; mientras se
escribe se llama `optica-AAAA-MM-DD.db.parcial`, y recién cuando pasa la verificación de integridad queda con su nombre
definitivo. Una copia que no pasa la verificación no cuenta y se borra.

**Cuántas copias se guardan.** Las de los últimos 7 días y la última de cada uno de los 4 meses anteriores. Las demás
se borran solas, pero solo después de un backup del día que salió bien: un backup que falla no borra ninguna copia.

**Cómo saber si el backup se está haciendo.** Al ingresar, si falta el backup de ayer, la pantalla de inicio lo avisa:
"Falta el backup de ayer.", con la fecha del último ("El último backup es del …" o "No hay ningún backup disponible.") y,
si corresponde, "No hay una ubicación de backup elegida." o "La ubicación del backup no está accesible.". El resultado
de cada backup (hecho o fallido, y en qué paso) queda también en el Registro de eventos de Windows, sin la ruta de la
carpeta.

La base SQLite está en modo WAL: los cambios recientes pueden estar todavía en `optica.db-wal`. **Nunca copies el
archivo `optica.db` con el Explorador ni con `copy`**: la copia puede quedar incompleta o dañada.

### Elegir la ubicación del backup

En la pantalla de configuración, sección **Backup**, escribí la carpeta en **Carpeta del backup** (la ruta completa,
empezando por la unidad, por ejemplo `E:\Backups`) y presioná **Grabar ubicación del backup**. Antes, creá la carpeta
y aplicale los permisos de la sección 4. Al grabar, la aplicación comprueba que la carpeta:

- exista y se pueda escribir en ella (el pendrive o el disco tiene que estar conectado);
- esté en **otra unidad** que la base de datos (un pendrive o un disco externo), **o** dentro de la carpeta de
  **OneDrive, Google Drive o Dropbox** de la cuenta de Windows que ejecuta la Api;
- no sea una unidad ni una ruta de red, ni la carpeta de datos, ni esté dentro de ella.

Una partición distinta del mismo disco se acepta, pero si ese disco se rompe se pierden juntas la base y las copias:
usá un pendrive o un disco externo. No uses unidades creadas con `subst`, uniones ni enlaces simbólicos: la aplicación
no siempre los distingue de la unidad de la base.

**Pendrive o disco externo.** Tiene que estar conectado a las 20:00 (o al iniciar la Api) para que el backup se haga.
Si es NTFS, aplicale los permisos de la sección 4. **Un pendrive con FAT32 o exFAT no guarda permisos**: cualquiera que
lo conecte a una PC puede leer las copias. Mientras no esté conectado a la PC del local, guardalo bajo llave.

**Nube (OneDrive, Google Drive o Dropbox).**

- La carpeta tiene que ser la de la **cuenta de Windows que ejecuta la Api**, no la de quien administra la PC: la
  aplicación solo reconoce las carpetas de nube de esa cuenta (OneDrive, Google Drive para escritorio y Dropbox).
- La copia se escribe primero en el disco de la PC y la sube el programa de la nube. Cada tanto, entrá a la página de la
  nube y comprobá que las copias recientes estén ahí: si la sincronización se corta, las copias quedan solo en la PC y
  se pierden con ella (riesgo R-09, aceptado al elegir la nube).
- La cuenta de la nube tiene que tener una **contraseña propia** (que no se use en otro lado) y la **verificación en
  dos pasos** activada: quien entre a esa cuenta puede descargar las copias y leer todos los datos de los clientes.
- Las copias que la aplicación borra van a la **papelera de la nube**, que las guarda entre 30 y 93 días según el
  servicio, y ahí se siguen pudiendo leer. Si hace falta que desaparezcan antes, vaciá la papelera.

**Al cambiar la ubicación**, las copias que estaban en la ubicación anterior no se mueven ni se borran: quedan ahí, y
la aplicación ya no las cuenta ni las borra. Guardalas con la misma protección que las nuevas o borralas a mano (y
vaciá la papelera, si estaban en la nube).

**Archivos `.parcial` que quedan.** Un `optica-AAAA-MM-DD.db.parcial` es una copia que se estaba escribiendo cuando
algo la cortó (un corte de luz, el pendrive desconectado). No cuenta como backup y la aplicación lo borra en el próximo
backup de esa ubicación. Si quedó en una ubicación que ya no se usa, borralo a mano: también tiene los datos de los
clientes.

**Qué no está en el backup.** El backup es solo la base de datos. Guardá aparte, con la misma protección que las copias:

- **el certificado de ARCA** (`.pfx`) y su clave (sección 12): sin ellos no se puede facturar después de reinstalar el
  sistema en otra PC;
- **la marca** (`C:\OpticaSistema\Marca`: `branding.json` y el logo, sección 3);
- con `Arca:Entorno = Simulado`, **`arca-simulado.json`** de la carpeta de datos (sección 12): tiene el último número
  que dio el simulador. Sin él, el simulador vuelve a numerar desde 1 y repite números que la base restaurada ya tiene,
  y esas facturas no se pueden grabar.

Las claves de `claves\` no hace falta respaldarlas: si se pierden, solo se cierran las sesiones abiertas.

**Datos personales.** Los backups contienen el hash de la contraseña, **los eventos de acceso** (fecha y hora de cada
ingreso, rechazo, bloqueo, cambio y restablecimiento: datos personales de las operadoras, que se guardan sin límite de
tiempo, riesgo R-14) y **los datos personales de los clientes** de cada presupuesto y de cada factura: apellido,
nombre, DNI, domicilio, email y teléfono, **sin cifrar**, alcanzados por la Ley 25.326 (D-04 de FEAT-010, riesgo R-06
aceptado por las dueñas). Cualquiera que obtenga una copia puede leerlos con `sqlite3.exe` o con cualquier visor de
SQLite. **Cada backup se guarda con la misma protección que `optica.db`**: los permisos de la sección 4, el pendrive
bajo llave o la nube con contraseña propia y verificación en dos pasos, y nunca en una carpeta compartida en red.

### Copia manual (solo si hace falta)

El backup automático alcanza para el uso diario. Si necesitás una copia en otro momento (por ejemplo, antes de
restablecer la contraseña, sección 11), hacela con `VACUUM INTO`, que también genera una copia coherente aunque la Api
esté en uso. Guardala **en otra carpeta que la del backup automático** (si no, la aplicación la cuenta como una copia
más y la puede borrar), con el mismo nombre `optica-AAAA-MM-DD.db`, que es el que acepta la restauración.

**De dónde sacar `sqlite3.exe`:** de la página oficial, <https://sqlite.org/download.html>, sección "Precompiled
Binaries for Windows", el archivo `sqlite-tools-win-x64-….zip`. Comprobá que el archivo descargado tiene el hash
SHA3-256 que publica esa página, junto al enlace, y extraé `sqlite3.exe` en
`C:\OpticaSistema\Herramientas` (con los mismos permisos que la carpeta de la aplicación). No uses copias de otros
sitios.

Con la cuenta de la Api, en una carpeta con los permisos de la sección 4 (en el ejemplo, `D:\Backups\Manual`):

```powershell
C:\OpticaSistema\Herramientas\sqlite3.exe "$env:LOCALAPPDATA\OpticaSistema\optica.db" "VACUUM INTO 'D:\Backups\Manual\optica-2026-11-02.db'"
```

(o con la ruta completa de la carpeta de datos de la cuenta de la Api). El archivo de destino no debe existir. Esa copia
tiene los mismos datos personales que un backup y se guarda con la misma protección.

## 10. Restaurar un backup

Restaurar **reemplaza todos los datos** por los de la copia: lo que se cargó después de la fecha de la copia
(presupuestos, facturas, cambios de configuración y de contraseña) deja de estar en la base y queda solo en la base
anterior, que se aparta sin borrarla. Se hace **desde la PC del local, con la cuenta de la Api y la Api detenida**, con
un comando. No hay pantalla ni dirección web para restaurar (FR-14).

La restauración completa lleva minutos (el objetivo es menos de 4 horas, NFR-01). La primera vez, hacé una prueba en
una PC de prueba o en otra cuenta, abrí un presupuesto de la copia y anotá cuánto tardó.

1. **Elegí la copia.** Tiene que ser un archivo `optica-AAAA-MM-DD.db` (nunca un `.parcial`), normalmente el más
   reciente que esté completo. Anotá su fecha. Si está en la nube, comprobá que esté descargado en la PC (en OneDrive,
   por ejemplo, con "Conservar siempre en este dispositivo"). Si está en un pendrive, conectalo.
2. **Detené la Api** (cerrá su ventana o detené la tarea que la ejecuta) y comprobá que no quedó corriendo:
   `Get-Process Optica.Api -ErrorAction SilentlyContinue` no tiene que mostrar nada.

   **En una PC nueva** (por ejemplo, porque se rompió la anterior), instalá primero el sistema con las secciones 1 a 5,
   configurá la Api solo en `localhost` como en el paso 1 de la sección 6, iniciala una vez para que cree la base
   vacía, y detenela. No hace falta definir una contraseña: la restauración trae la de la copia.
3. **Ejecutá el comando con la cuenta de la Api**, en PowerShell sin elevar, desde la carpeta de la aplicación, con la
   ruta de la copia entre comillas:

   ```powershell
   cd C:\OpticaSistema\App
   .\Optica.Api.exe restaurar-backup "E:\Backups\optica-2026-11-02.db"
   $LASTEXITCODE
   ```

4. **Leé lo que muestra antes de confirmar.** El comando verifica la copia (que sea una copia del sistema, que esté
   completa y que sea de esta versión de la aplicación o de una anterior) y muestra:

   - el nombre y la fecha de la copia;
   - si la copia es de una versión anterior (se actualiza sola al iniciar la aplicación);
   - **cuántas facturas con CAE tiene la base actual posteriores a la última de la copia.** Esas facturas existen en
     ARCA, pero después de restaurar quedan **solo en la base anterior**. Si son más de cero, no las vas a ver en la
     aplicación: tenelo en cuenta en el paso 6. **Si el comando dice que no se pudieron contar** (la base actual puede
     estar dañada), tratalo como si hubiera facturas y conservá `anterior\` (paso 8).

   Después pide escribir `RESTAURAR`. Cualquier otra respuesta cancela sin cambiar nada. `$LASTEXITCODE` vale `0` si
   restauró, `1` si se canceló y `2` si falló.

   **Mientras el comando espera que escribas `RESTAURAR`, la Api no puede arrancar** (el comando tiene tomado el
   candado de la carpeta de datos). Si decidís no seguir, escribí cualquier otra cosa, cerrá la ventana o presioná
   Ctrl+C: no se cambia nada. **Desde que escribís `RESTAURAR` hasta que el comando termina, Ctrl+C no lo corta**
   (avisa que la restauración no se puede cortar): esperá, no cierres la ventana ni apagues la PC.
5. **Qué hace al confirmar.** Restaura la copia a un archivo temporal (`optica.db.restaurando`) en la carpeta de datos
   y lo verifica con las mismas comprobaciones que la copia (también que tenga las protecciones de los presupuestos en
   Final y de las facturas con CAE). Recién entonces aparta la base actual, con sus archivos `optica.db-wal` y
   `optica.db-shm`, a la carpeta `anterior\AAAAMMDD-HHMMSS` (la fecha y la hora de la restauración) dentro de la
   carpeta de datos, y pone la restaurada como `optica.db`. Si algo falla en el medio, la base actual vuelve sola a su
   lugar. La copia elegida no se modifica. Después **cierra todas las sesiones abiertas** (renueva las claves de
   sesión de `claves\`). Al terminar, el comando dice en qué carpeta quedó la base anterior.
6. **Antes de volver a facturar, revisá en ARCA los comprobantes emitidos desde la fecha de la copia** (riesgo R-07).
   Los que ARCA tiene y la base restaurada no **ya están facturados**: no vuelvas a facturar esos presupuestos, porque
   la factura quedaría duplicada en ARCA y no se puede anular desde el sistema. Para esos casos, consultá con el
   contador; el detalle de cada una sigue en la base anterior. El comando lo recuerda al terminar.
7. **Iniciá la Api** e ingresá con la contraseña que estaba vigente **en la fecha de la copia** (la contraseña viaja con
   la base). Si nadie la recuerda, restablecela con la sección 11. Si la copia es de una versión anterior, la Api la
   actualiza al arrancar. Todas las sesiones se cerraron: en cada equipo hay que volver a ingresar. **Si la contraseña
   se había cambiado después de la fecha de la copia** (por ejemplo, porque alguien que ya no trabaja en el local la
   conocía), la restauración trajo la anterior: cambiala enseguida desde la aplicación. Si la Api estaba restringida a
   `localhost` (PC nueva), habilitá el acceso desde la red con la sección 7.
8. **Conservá la carpeta `anterior\` bajo llave** mientras tenga facturas que la copia no tiene (la cantidad que mostró
   el paso 4; **si no se pudieron contar, tratalo como si hubiera facturas**): es el único registro en el sistema de
   esas facturas. Tiene los mismos datos personales que un backup, sin cifrar: no la saques de la carpeta de datos, no
   la copies a una carpeta compartida ni a la nube. Cuando ya no la necesites (las facturas están revisadas con ARCA y
   con el contador, o eran cero), borrala.

| Mensaje | Qué hacer |
|---|---|
| "La aplicación está en marcha. Detenela antes de restaurar un backup." | Volvé al paso 2. |
| "No se encontró la base de datos en la carpeta de datos…" | Ejecutalo con la cuenta de la Api. En una PC nueva, iniciá la aplicación una vez, detenela y repetí el paso 3. |
| "El archivo elegido no es una copia de backup del sistema…" | Elegí un archivo `optica-AAAA-MM-DD.db`. No renombres otros archivos para que parezcan una copia. |
| "No se encontró la copia …" | Revisá la ruta, que el pendrive esté conectado o que el archivo de la nube esté descargado. |
| "La copia … está dañada o no es una base de datos…" | No se cambió nada. Elegí la copia del día anterior. |
| "La copia … es de una versión más nueva de la aplicación…" | No se cambió nada. Instalá esa versión de la aplicación (o una más nueva) y repetí. |
| "La copia … no es una base del sistema de la óptica…" | No se cambió nada. La copia no tiene el historial de versiones esperado o le faltan las protecciones de los registros cerrados (puede haber sido modificada). Elegí otra copia. |
| "No se pudo verificar la copia (…): la base actual no se modificó." | Revisá que el pendrive siga conectado o que el archivo de la nube esté descargado, y repetí el paso 3. |
| "No se pudo restaurar la copia …: la base actual no se tocó." | Revisá que haya espacio libre en el disco de la carpeta de datos y repetí el paso 3. |
| "No se pudo poner la base restaurada (…): la base actual volvió a su lugar y no cambió." | Repetí el paso 3. Si vuelve a pasar, revisá los permisos de la carpeta de datos (sección 4). |
| "No se pudo poner la base restaurada ni devolver la base actual (…)" | **No inicies la Api.** Seguí los pasos de abajo. |
| "No se pudo terminar la restauración (…)" | **No inicies la Api** hasta revisar la carpeta de datos con los pasos de abajo: si `optica.db` está, la restauración terminó o no empezó; si no está, volvé la base a su lugar. |
| "La base se restauró, pero no se pudieron renovar las claves de sesión (…)" | La base ya está restaurada. Con la Api detenida, borrá el contenido de la carpeta `claves` de la carpeta de datos (no la carpeta): así se cierran las sesiones de antes. Después seguí con el paso 6. |
| "No se pudo comprobar si la aplicación está en marcha (…)" | No se cambió nada. Revisá que la cuenta de la Api pueda abrir `optica.instancia` en la carpeta de datos y repetí. |
| "No se puede usar el directorio de datos configurado…" o "No se pudo preparar el comando …" | No se cambió nada. Corregí `Optica:Datos:Directorio` o el `appsettings.Production.json`, y repetí. |

**Si la Api no arranca y el registro dice "Falta la base optica.db y la carpeta de datos tiene una restauración sin
terminar"**: una restauración quedó a medias (por ejemplo, se cortó la luz o se cerró la ventana en el medio). La Api
no crea una base vacía en ese caso, para no esconder la base real, que está en `anterior\` o en
`optica.db.restaurando`. Seguí los pasos de abajo.

**Si la base actual no pudo volver sola** (cualquiera de esos casos), con la Api detenida y la cuenta de la Api:

```powershell
cd "$env:LOCALAPPDATA\OpticaSistema"
dir anterior\20261117-101500
```

(con la carpeta que nombró el comando). Si en la carpeta de datos **no** está `optica.db`, mové a la carpeta de datos
todos los archivos de esa carpeta (`optica.db` y, si están, `optica.db-wal` y `optica.db-shm`) y borrá
`optica.db.restaurando`:

```powershell
Move-Item anterior\20261117-101500\* .
Remove-Item optica.db.restaurando
```

Si hay más de una carpeta dentro de `anterior\`, usá la más reciente (el nombre es la fecha y la hora). Después
repetí la restauración desde el paso 3. Nunca muevas solo `optica.db` sin sus archivos `-wal` y `-shm`, ni dejes un
`-wal` o un `-shm` viejo junto a otra base: SQLite intentaría aplicarlos sobre ella.

Una carpeta `anterior\` **junto a una `optica.db`** es lo normal después de una restauración terminada: la Api arranca
igual. Lo que la frena es que falte `optica.db`. Si en una instalación borraste `optica.db` a propósito para empezar
con una base vacía, sacá también la carpeta `anterior\` y el `optica.db.restaurando` (guardalos bajo llave o borralos:
tienen los datos de los clientes).

## 11. Restablecer la contraseña olvidada (en la PC del local)

Si nadie recuerda la contraseña, se restablece **desde la PC del local, con la cuenta de la Api y la Api detenida**.
No hay pantalla ni dirección web para hacerlo (FR-05). El comando elimina solo la contraseña, registra un evento
`restablecimiento` en el historial de accesos y vacía las claves de sesión (`claves\`), así **todas las sesiones
abiertas se cierran**. No modifica ningún otro dato. Después, la aplicación vuelve a pedir "Definí la contraseña de
acceso", como en la instalación: por eso se vuelve a restringir a `localhost` antes de definirla (R-02, R-11).

Quien opere la PC con la cuenta de la Api puede restablecer la contraseña y definir una propia (R-08): la protección
física de la PC y de esa cuenta queda a cargo del negocio. El evento queda registrado con la fecha y la hora.

1. **Hacé un backup** con `VACUUM INTO`, como en la copia manual de la sección 9 (en otra carpeta que la del backup
   automático y con un nombre de archivo nuevo). Es la forma de volver atrás si algo sale mal (sección 10).
2. **Detené la Api** (cerrá su ventana o detené la tarea que la ejecuta) y comprobá que no quedó corriendo:
   `Get-Process Optica.Api -ErrorAction SilentlyContinue` no tiene que mostrar nada.
3. **Restringí la Api a `localhost`** en `C:\OpticaSistema\App\appsettings.Production.json`, como en el paso 1 de la
   sección 6: `"AllowedHosts": "localhost"` y el endpoint `"Url": "https://localhost:5001"`. Así, cuando vuelva a
   arrancar sin contraseña, ningún otro equipo de la red puede definirla primero.
4. **Ejecutá el comando con la cuenta de la Api**, en PowerShell sin elevar, desde la carpeta de la aplicación:

   ```powershell
   cd C:\OpticaSistema\App
   .\Optica.Api.exe restablecer-contrasena
   $LASTEXITCODE
   ```

   El comando muestra la carpeta de datos (sin expandir la carpeta del usuario) y lo que va a hacer, y pide escribir
   `RESTABLECER`. Cualquier otra respuesta cancela sin cambiar nada. `$LASTEXITCODE` vale `0` si restableció, `1` si
   se canceló y `2` si falló. Usá siempre la cuenta de la Api: con otra cuenta, el comando busca la base en otra
   carpeta, no la encuentra y termina sin crear nada.

   **Mientras el comando espera que escribas `RESTABLECER`, la Api no puede arrancar** (el comando tiene tomado el
   candado de la carpeta de datos). No dejes la consola abierta esperando: si decidís no seguir, cerrá la ventana o
   presioná Ctrl+C, y no se cambia nada.

   | Mensaje | Qué hacer |
   |---|---|
   | "La aplicación está en marcha. Detenela antes de restablecer la contraseña." | Volvé al paso 2. |
   | "No se encontró la base de datos en …" | Ejecutalo con la cuenta de la Api, o revisá `Optica:Datos:Directorio`. |
   | "No se pudo comprobar si la aplicación está en marcha (…)" | No se cambió nada. Revisá que la cuenta de la Api pueda abrir el archivo `optica.instancia` de la carpeta de datos (sección 4) y repetí el paso 4. |
   | "No se puede usar el directorio de datos …" o "No se pudo preparar el comando …" | No se cambió nada. Corregí `Optica:Datos:Directorio` (tiene que ser una ruta absoluta, o vacía) o el `appsettings.Production.json`, y repetí el paso 4. |
   | "Iniciá la aplicación una vez antes de restablecer." | La base es de una versión anterior: iniciá la Api (sigue en `localhost`), esperá que arranque, detenela y repetí el paso 4. |
   | "No se pudo restablecer la contraseña: falló la base …" | No se cambió nada. Anotá el código de SQLite y revisá la base; si está dañada, restaurá el último backup (sección 10). |
   | "… no se pudieron renovar las claves de sesión …" | La contraseña ya se eliminó; repetí el paso 4 para cerrar las sesiones. |

   Repetir el comando no hace daño: registra otro evento y vuelve a renovar las claves.
5. **Iniciá la Api y definí la contraseña desde la PC**, como en los pasos 2 a 4 de la sección 6.
6. **Recién entonces habilitá otra vez el acceso desde la red**, como en el paso 1 de la sección 7, y reiniciá la Api.
   Cada equipo vuelve a ingresar con la contraseña nueva.

## 12. Facturación electrónica (ARCA)

Desde FEAT-007 el botón **Facturar** de un presupuesto en Final emite la factura a consumidor final por los web
services de ARCA (WSAA y WSFEv1). La conexión se configura con las claves `Arca:*`; el punto de venta, el tope de
identificación y la alícuota de IVA se cargan en la pantalla de configuración, nunca acá.

> **Entorno simulado: sin validez fiscal (R-10).** Sin `Arca:Entorno`, o con `Arca:Entorno = Simulado`, la aplicación
> **no se comunica con ARCA**: un simulador local numera los comprobantes y les da un CAE inventado. Esas facturas
> **no existen en ARCA, no tienen validez fiscal y no se pueden entregar a un cliente como comprobante**. La factura
> guarda el entorno en que se emitió, y el PDF de la factura (FEAT-009) lleva en cada página la leyenda "COMPROBANTE
> SIMULADO — SIN VALIDEZ FISCAL" (también las de `Homologacion`). Al arrancar en simulado, la Api lo advierte en el registro. Antes de facturar de verdad, comprobá en la configuración de la Api
> que el entorno es el que corresponde.

| Clave | Valores | Qué hace |
|---|---|---|
| `Arca:Entorno` | `Simulado` (por defecto), `Homologacion`, `Produccion` | `Simulado` no sale de la PC (sin validez fiscal). `Homologacion` es el entorno de prueba de ARCA. `Produccion` emite comprobantes reales. Otro valor: la Api no arranca y registra qué clave corregir. |
| `Arca:Cuit` | Los 11 dígitos del CUIT de la óptica, sin guiones | Solo en `Homologacion` y `Produccion`. Un CUIT con dígito verificador inválido no deja arrancar la Api. |
| `Arca:Certificado:Ruta` | Ruta absoluta del certificado (`.pfx`) emitido por ARCA para ese entorno | Solo en `Homologacion` y `Produccion`. |
| `Arca:Certificado:Clave` | La clave del `.pfx` | Solo en `Homologacion` y `Produccion`. |
| `Arca:TiempoMaximoSegundos` | Entero de 1 a 120; por defecto, 30 | Cuánto espera la Api a ARCA en cada emisión y en cada consulta de verificación (NFR-01). Dejalo en 30. |
| `Arca:EsperaVerificacionSegundos` | Entero de 1 a 3600; por defecto, 60 | Cuánto tiene que pasar desde que una emisión quedó pendiente para poder verificarla con ARCA (FEAT-008, FR-16). Dejalo en 60. Tiene que ser al menos `Arca:TiempoMaximoSegundos` más el margen del envío (el menor entre 10 s y un tercio de ese tiempo): con 30 s, al menos 40. Si es menor, la Api no arranca y registra qué clave corregir. **Si subís `Arca:TiempoMaximoSegundos`, subí también esta espera** (por ejemplo, con 60 s de tiempo máximo, al menos 70). |

`Arca:Simulado:Respuesta` (`autorizar`, `rechazar`, `no_responder`, `autorizar_sin_responder`, `no_responder_una_vez`)
y `Arca:Simulado:Consulta` (`responder`, `no_responder`) son solo para las pruebas automáticas: simulan que ARCA
rechaza, no responde o autoriza sin responder, y que no responde la consulta de verificación. No las configures en la
PC del local. Otro valor no deja arrancar la Api.

**El CUIT, la ruta y la clave del certificado nunca van en el repositorio ni en `appsettings.json`.** Cargalos solo en
variables de entorno de la cuenta de la Api (`Arca__Cuit`, `Arca__Certificado__Ruta`, `Arca__Certificado__Clave`: dos
guiones bajos en lugar de `:`) o en un archivo local que no se versiona, como `appsettings.Production.json`
(`.gitignore`), con los permisos de la carpeta de la aplicación (sección 4). Para la **clave del certificado**
preferí la variable de entorno: un archivo con la clave se copia junto con la aplicación sin que nadie lo note. Por
ejemplo, en PowerShell con la cuenta de la Api:

```powershell
[Environment]::SetEnvironmentVariable("Arca__Entorno", "Homologacion", "User")
[Environment]::SetEnvironmentVariable("Arca__Cuit", "<CUIT de la óptica>", "User")
[Environment]::SetEnvironmentVariable("Arca__Certificado__Ruta", "C:\OpticaSistema\Certificados\arca-homologacion.pfx", "User")
[Environment]::SetEnvironmentVariable("Arca__Certificado__Clave", [System.Net.NetworkCredential]::new("", (Read-Host -AsSecureString "Clave del .pfx")).Password, "User")
```

La última línea pide la clave sin mostrarla (`Read-Host -AsSecureString`) y la convierte dentro de la misma llamada:
la clave no queda escrita en el comando, ni en una variable de la consola, ni en el historial. **Nunca escribas la clave
en la línea de comandos.** Si ya la escribiste (por ejemplo, `SetEnvironmentVariable(..., "laclave", ...)`), borrala
del historial de esa misma cuenta:

```powershell
Clear-History
Remove-Item (Get-PSReadLineOption).HistorySavePath
```

`Clear-History` borra el historial de la sesión abierta; el archivo de PSReadLine (`ConsoleHost_history.txt`, en
`%APPDATA%\Microsoft\Windows\PowerShell\PSReadLine\`) guarda los comandos de todas las sesiones y hay que borrarlo
aparte. Cerrá después la consola.

Tené en cuenta que una variable de entorno de alcance `User` se guarda **sin cifrar** en el registro
(`HKCU\Environment`) de esa cuenta: la puede leer cualquier proceso que corra con la cuenta de la Api. Por eso la
cuenta de la Api se usa solo para la Api (sección 1) y nadie navega ni abre correo con ella.

Después de cambiar cualquiera de estas claves, reiniciá la Api. Si faltan el CUIT, la ruta o la clave en
`Homologacion` o `Produccion`, la Api arranca, lo advierte en el registro nombrando las claves (nunca los valores) y
cada emisión termina con "No se pudo enviar la factura a ARCA. Intentá de nuevo en unos minutos.".

**Datos del emisor (FEAT-009).** El PDF de la factura lleva la razón social, el domicilio comercial, el CUIT, el
número de Ingresos Brutos y la fecha de inicio de actividades de la óptica. Se cargan en la pantalla de configuración,
en la sección **Datos del emisor**, con su propio botón **Grabar datos del emisor**: los cinco son obligatorios y el
CUIT se puede escribir con o sin guiones (se guarda sin ellos). Mientras falte alguno, **Descargar PDF** en una factura
no genera el PDF y pide completarlos en la configuración. El PDF muestra siempre los datos vigentes al descargarlo: si
cambia el domicilio, el PDF de una factura vieja descargado después muestra el nuevo. La condición frente al IVA no se
carga: sale del tipo de comprobante (Factura B, "Responsable Inscripto"; Factura C, "Responsable Monotributo").

> **El CUIT de los datos del emisor tiene que ser el mismo que `Arca:Cuit` (R-10).** El sistema no los compara: ARCA
> autoriza la factura con el CUIT de `Arca:Cuit`, pero el PDF y el código QR del comprobante llevan el CUIT de los datos
> del emisor. Si no coinciden, el PDF muestra un emisor distinto del que autorizó ARCA y el QR abre en ARCA la consulta
> de un comprobante que no existe con ese CUIT. Antes de pasar a `Produccion`, revisá en la configuración que el CUIT
> del emisor sea el de `Arca:Cuit` y descargá el PDF de una factura de homologación para comprobarlo (su QR no se
> puede consultar en ARCA: ese comprobante no existe fuera de homologación). La leyenda del Régimen de Transparencia Fiscal al Consumidor (Ley 27.743) de la
> Factura B la tiene que confirmar el contador antes de facturar en producción.

**El certificado de ARCA** (`.pfx`) identifica a la óptica ante ARCA: guardalo fuera del repositorio y de las carpetas
de la aplicación, de la marca y de datos (por ejemplo, `C:\OpticaSistema\Certificados`), con permisos solo para la cuenta
de la Api y para quien administra, como la carpeta de datos de la sección 4. No lo copies a otras PC ni lo mandes por
correo o mensajería.

**Nunca uses el certificado de producción para probar.** Las pruebas van siempre contra `Homologacion`, con el
certificado de homologación. Un comprobante autorizado en producción por error no se puede anular desde el sistema
(las notas de crédito están fuera del alcance de esta versión). Pasá a `Produccion` recién cuando una factura de
homologación salió bien (AC-24) y las dueñas lo decidan.

**Archivos de ARCA en la carpeta de datos** (`Optica:Datos:Directorio`, sección 1):

- `arca-simulado.json`: el contador del simulador (último número por tipo y punto de venta) y, desde FEAT-008, los
  últimos 50 comprobantes que autorizó por tipo y punto de venta (número, importe, CAE y fechas; ningún dato del
  cliente). Solo se usa en `Simulado`. Con él, el simulador responde la verificación de una emisión pendiente.
  **No lo borres ni lo reemplaces mientras haya emisiones pendientes de verificación** (el aviso de la pantalla de
  inicio las lista): sin él, el simulador vuelve a numerar desde 1 y la verificación no puede confirmar lo que ya
  había autorizado. Si se borra sin pendientes, el simulador vuelve a numerar desde 1.
- `arca-ta-homologacion.json` y `arca-ta-produccion.json`: el ticket de acceso de ARCA (vale unas 12 horas), cifrado
  con las claves de `claves\`. Se puede borrar con la Api detenida: se pide otro en la emisión siguiente. No lo copies
  a otra PC.

**Datos personales.** La carpeta de datos, `optica.db` y cada backup guardan los datos de los clientes **sin cifrar**,
entre ellos su DNI (en los presupuestos y en las facturas que lo identifican ante ARCA). Tienen que estar en una carpeta
con permisos restringidos a la cuenta de la Api y a quien administra (sección 4), **nunca compartida en red** ni
sincronizada con servicios en la nube. Los backups se protegen como indica la sección 9 (pendrive bajo llave o nube con
contraseña propia y verificación en dos pasos), y la carpeta `anterior\` de una restauración, como la sección 10.

Si ARCA no responde dentro del tiempo máximo, la pantalla dice "ARCA no respondió. La emisión quedó pendiente de
verificación." y el presupuesto muestra **Verificar con ARCA** en lugar de Facturar: no se vuelve a pedir la factura
hasta verificar en ARCA si se autorizó, porque una segunda emisión podría duplicarla.

**Verificación con ARCA (FEAT-008).** Antes de enviar cada factura, el sistema registra el número de comprobante que
pide. Al iniciar sesión, la pantalla de inicio avisa qué presupuestos tienen una emisión pendiente de verificación, con
un enlace a cada uno. En el presupuesto, **Verificar con ARCA** le pregunta a ARCA por ese número:

- Si pasó menos de `Arca:EsperaVerificacionSegundos` (1 minuto) desde que la emisión quedó pendiente, la pantalla dice
  cuántos segundos faltan y no consulta nada: ARCA puede seguir procesando el envío original.
- Si ARCA tiene ese número autorizado con el mismo importe, el sistema recupera el CAE y guarda la factura, sin emitir
  otra.
- Si ARCA no tiene ese número, el sistema emite la factura en esa misma acción, con un número nuevo. Si esa emisión
  tampoco tiene respuesta, la emisión sigue pendiente y se vuelve a verificar más tarde.
- Si ARCA no responde la consulta, la pantalla lo dice y la emisión sigue pendiente; el sistema no reintenta solo.
- Si ARCA tiene ese número con **otro importe**, si la emisión no tiene número registrado (una pendiente anterior a
  esta versión) o si se envió a **otro entorno** de ARCA que el configurado, el sistema no emite ni recupera nada y
  pide revisar el punto de venta en ARCA. Recién entonces aparece **Descartar la emisión**, que pide confirmación;
  confirmado, el presupuesto vuelve a mostrar Facturar. Cada descarte queda registrado en la base con la fecha y la
  hora, el presupuesto, el número y los importes, para conciliarlo con el contador. Antes de descartar, revisá en ARCA
  (o con el contador) qué comprobante quedó autorizado con ese número: un descarte equivocado deja en ARCA una factura
  que el sistema no registra, y no se puede anular desde el sistema.
