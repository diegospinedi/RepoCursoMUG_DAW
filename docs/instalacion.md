# Instalación en la PC del local — Óptica Sistema

Procedimiento para instalar la aplicación en la PC de la óptica (Windows 10 u 11). Corresponde a FEAT-001a
(ingreso, sesión y marca) y FEAT-001b (bloqueo por intentos fallidos, cambio y restablecimiento de la contraseña).
Seguí los pasos en orden: el paso 6 (definir la contraseña) se hace **antes** de conectar la aplicación a la red local.
Si se olvida la contraseña, el restablecimiento en la PC está en la sección 10.

> **Bloqueo por intentos fallidos.** Desde FEAT-001b, después de 5 intentos de ingreso fallidos seguidos el ingreso
> queda bloqueado 5 minutos, también si se reinicia la Api (riesgo R-03 del modelo de amenazas, ya mitigado). Si la
> hora de la PC retrocede durante el bloqueo o poco después de que termine (por ejemplo, al sincronizarse sola), el
> bloqueo puede extenderse hasta 10 minutos desde ese momento (FIX-001); si no se puede esperar, la contraseña se
> restablece desde la PC (sección 10).
> Antes de cargar datos reales de clientes, revisá la sección 8.

## 1. Qué se instala y dónde

Nada de lo que se instala vive dentro del repositorio. Usá estas carpetas (podés elegir otras, siempre con rutas
absolutas y fuera de la copia del código):

| Carpeta | Contenido | Clave de configuración |
|---|---|---|
| `C:\OpticaSistema\App` | La Api publicada (`Optica.Api.exe` y sus archivos) | — |
| `C:\OpticaSistema\App\wwwroot` | La interfaz web compilada (`index.html` y `assets\`) | `Optica:Web:Directorio` |
| `C:\OpticaSistema\Marca` | La marca real: `branding.json` y el logo | `Optica:Marca:Directorio` |
| `%LOCALAPPDATA%\OpticaSistema` de la cuenta que ejecuta la Api | La base `optica.db`, las claves de sesión (`claves\`) y el candado de instancia (`optica.instancia`) | `Optica:Datos:Directorio` (vacía = esta ubicación) |

`optica.instancia` es un archivo vacío que la Api mantiene abierto en exclusiva mientras corre: una segunda Api sobre la
misma carpeta de datos no arranca (registra un error crítico y termina), y el restablecimiento de la sección 10 se niega
mientras la Api está en marcha. No lo borres con la Api en marcha; con la Api detenida no hace falta tocarlo.

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

Carpeta de backups (sección 9): control total para Administradores y SYSTEM, y modificación para la cuenta de la Api,
que es la que genera los backups. Ninguna otra cuenta la lee: los backups contienen el hash de la contraseña, los
eventos de acceso (fecha y hora de cada ingreso, rechazo, bloqueo, cambio y restablecimiento: datos personales de las
operadoras, riesgo R-14) y los datos personales de los clientes de cada presupuesto (apellido, nombre, DNI,
domicilio, email y teléfono). Si está en un disco externo, tiene que ser NTFS (FAT32 y exFAT no guardan permisos).

> **Datos personales de clientes sin cifrar (Ley 25.326).** La base (`optica.db` y `optica.db-wal`) y **cada copia**
> de backup guardan el DNI, el domicilio, el email y el teléfono de los clientes **en claro**: quien pueda leer el
> archivo puede leer esos datos, que están alcanzados por la Ley 25.326 de Protección de los Datos Personales. Lo único
> que los protege es la ubicación fuera del árbol de fuentes y los permisos NTFS de esta sección: la base, los de la
> carpeta de datos, y los backups, los de la carpeta de backups. El cifrado de la base y de las copias no está
> implementado: es un riesgo aceptado mientras el sistema se usa como software de prueba, y se tiene que revisar antes
> de cargar datos reales de clientes, de pasar a producción, al definir el backup (PRD 09) o si la PC se comparte (R-01
> del modelo de amenazas de FEAT-003a, D-23 y D-27).

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
- **Restablecimiento:** solo con la Api detenida y desde la PC, con la cuenta de la Api (sección 10). No hay pantalla
  ni dirección web para restablecer la contraseña, y no hay que agregarla.

## 9. Copia de seguridad (backup)

La base SQLite está en modo WAL: los cambios recientes pueden estar todavía en `optica.db-wal`. **Nunca copies el
archivo `optica.db` con el Explorador ni con `copy`**: la copia puede quedar incompleta o dañada. Hacé el backup con
`VACUUM INTO`, que genera una copia consistente aunque la Api esté en uso.

**De dónde sacar `sqlite3.exe`:** de la página oficial, <https://sqlite.org/download.html>, sección "Precompiled
Binaries for Windows", el archivo `sqlite-tools-win-x64-….zip`. Comprobá que el archivo descargado tiene el hash
SHA3-256 que publica esa página, junto al enlace, y extraé `sqlite3.exe` en
`C:\OpticaSistema\Herramientas` (con los mismos permisos que la carpeta de la aplicación). No uses copias de otros
sitios.

Con la cuenta de la Api, en la carpeta de backups de la sección 4:

```powershell
C:\OpticaSistema\Herramientas\sqlite3.exe "$env:LOCALAPPDATA\OpticaSistema\optica.db" "VACUUM INTO 'D:\Backups\OpticaSistema\optica-2026-11-02.db'"
```

(o con la ruta completa de la carpeta de datos de la cuenta de la Api). El archivo de destino no debe existir.
Guardá los backups en un medio con acceso restringido: contienen el hash de la contraseña, **los eventos de acceso**
(fecha y hora de cada ingreso, rechazo, bloqueo, cambio y restablecimiento: datos personales de las operadoras, que se
guardan sin límite de tiempo, riesgo R-14) y **los datos personales de los clientes** de cada presupuesto: apellido,
nombre, DNI, domicilio, email y teléfono, **sin cifrar**, alcanzados por la Ley 25.326 (R-01 del modelo de amenazas de
FEAT-003a, D-23 y D-27). Cualquiera que obtenga una copia puede leerlos con `sqlite3.exe` o con cualquier visor de
SQLite. **Cada backup se guarda con la misma protección que `optica.db`**: la carpeta de backups de la sección 4, sin
compartirla en red, y un medio externo solo si es NTFS con esos mismos permisos. Las claves de `claves\` no hace falta
respaldarlas: si se pierden, solo se cierran las sesiones abiertas.

### Restaurar un backup

1. **Detené la Api** (cerrá su ventana o detené la tarea que la ejecuta) y comprobá que no quedó corriendo:
   `Get-Process Optica.Api -ErrorAction SilentlyContinue` no tiene que mostrar nada. Con la Api en marcha, la base
   está abierta y la restauración se pierde o daña la base.
2. Con la cuenta de la Api, en la carpeta de datos (`$env:LOCALAPPDATA\OpticaSistema`), **sacá la base actual y sus
   archivos `-wal` y `-shm`**: los tres juntos, a una carpeta con acceso restringido (por si hay que volver atrás), o
   borralos. Los `optica.db-wal` y `optica.db-shm` viejos **no pueden quedar** junto a la base restaurada: SQLite
   intentaría aplicarlos sobre ella.

   ```powershell
   cd "$env:LOCALAPPDATA\OpticaSistema"
   New-Item -ItemType Directory -Force anterior
   Move-Item optica.db, optica.db-wal, optica.db-shm anterior -ErrorAction SilentlyContinue
   ```

3. **Copiá el backup como `optica.db`.** El backup es un archivo cerrado y sin WAL: acá sí vale la copia común.

   ```powershell
   Copy-Item D:\Backups\OpticaSistema\optica-2026-11-02.db optica.db
   C:\OpticaSistema\Herramientas\sqlite3.exe optica.db "PRAGMA integrity_check"
   ```

   El segundo comando tiene que responder `ok`.
4. Iniciá la Api e ingresá con la contraseña **vigente a la fecha del backup** (la contraseña viaja con la base).
5. Cuando compruebes que todo funciona, borrá la carpeta `anterior`: tiene los mismos datos sensibles que un backup,
   incluidos los datos personales de los clientes sin cifrar (R-01 de FEAT-003a).

## 10. Restablecer la contraseña olvidada (en la PC del local)

Si nadie recuerda la contraseña, se restablece **desde la PC del local, con la cuenta de la Api y la Api detenida**.
No hay pantalla ni dirección web para hacerlo (FR-05). El comando elimina solo la contraseña, registra un evento
`restablecimiento` en el historial de accesos y vacía las claves de sesión (`claves\`), así **todas las sesiones
abiertas se cierran**. No modifica ningún otro dato. Después, la aplicación vuelve a pedir "Definí la contraseña de
acceso", como en la instalación: por eso se vuelve a restringir a `localhost` antes de definirla (R-02, R-11).

Quien opere la PC con la cuenta de la Api puede restablecer la contraseña y definir una propia (R-08): la protección
física de la PC y de esa cuenta queda a cargo del negocio. El evento queda registrado con la fecha y la hora.

1. **Hacé un backup** con `VACUUM INTO`, como en la sección 9 (con un nombre de archivo nuevo). Es la forma de volver
   atrás si algo sale mal.
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
   | "No se pudo restablecer la contraseña: falló la base …" | No se cambió nada. Anotá el código de SQLite y revisá la base (sección 9). |
   | "… no se pudieron renovar las claves de sesión …" | La contraseña ya se eliminó; repetí el paso 4 para cerrar las sesiones. |

   Repetir el comando no hace daño: registra otro evento y vuelve a renovar las claves.
5. **Iniciá la Api y definí la contraseña desde la PC**, como en los pasos 2 a 4 de la sección 6.
6. **Recién entonces habilitá otra vez el acceso desde la red**, como en el paso 1 de la sección 7, y reiniciá la Api.
   Cada equipo vuelve a ingresar con la contraseña nueva.
