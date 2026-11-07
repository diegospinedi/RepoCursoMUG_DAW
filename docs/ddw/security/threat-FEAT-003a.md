# Threat model FEAT-003a: Alta del presupuesto con cliente y líneas del catálogo

| Field | Value |
|-------|-------|
| Ticket | FEAT-003a |
| Spec | docs/ddw/specs/spec-FEAT-003a.md |
| Tier | FEATURE |
| Date | 2026-11-06 |

## Components
| Component | Source in the spec |
|---|---|
| `src/Optica.Domain/Presupuestos/PoliticaCliente.cs` (con `ClientePresupuesto.cs`, `LineaPresupuesto.cs` y `Presupuesto.cs`) | Block 1 |
| `src/Optica.Infrastructure/Persistencia/PresupuestoRepository.cs` (con `OpticaDbContext.cs` y la migración `Presupuestos`) | Block 2 |
| `src/Optica.Application/Presupuestos/AltaPresupuesto.cs` (con `ObtenerPresupuesto.cs` y `BuscarArticulos.cs`) | Block 3 |
| `src/Optica.Api/Controllers/PresupuestosController.cs` (con `ArticulosController.cs` y `soloActivos`) | Block 4 |
| `web/src/paginas/NuevoPresupuesto.tsx` (con `Presupuesto.tsx` y `web/src/api/cliente.ts`) | Block 5 |
| `src/Optica.Api/Web/SinCacheApi.cs` | Block 7 |

## Trust boundaries
- Navegador de la operadora → API ASP.NET Core: `POST /api/presupuestos` lleva en el cuerpo JSON el Apellido, el
  Nombre, el DNI, el Domicilio, el Email y el Nro. de Teléfono del cliente y los códigos de artículo;
  `GET /api/presupuestos/{numero}` devuelve esos mismos datos; `GET /api/articulos?soloActivos=true` recibe el texto
  buscado. La cookie de sesión de FEAT-001a viaja en cada pedido.
- API → archivo SQLite `optica.db`: cruzan la lectura de los artículos, de `MAX(Numero)` y la grabación de
  `Presupuesto` y `LineaPresupuesto` dentro de `BEGIN IMMEDIATE` (ADR-013, ADR-015).
- Base y respaldos → personas con acceso a la PC: `optica.db`, `optica.db-wal` y cada copia de `VACUUM INTO` guardan
  los datos personales del cliente sin cifrar (D-23).

## STRIDE analysis
### `src/Optica.Domain/Presupuestos/PoliticaCliente.cs` (con `ClientePresupuesto.cs`, `LineaPresupuesto.cs` y `Presupuesto.cs`)
- **Spoofing:** sin identidades; lo invocan casos de uso que corren detrás de la sesión.
- **Tampering:** `ClientePresupuesto` no se construye sin pasar `PoliticaCliente` (obligatorios, DNI de 7 u 8 dígitos,
  email con extensión de 2 letras, largos de D-08, UTF-16 válido), y `LineaPresupuesto.DesdeArticulo` toma la
  descripción y el precio del artículo, nunca del pedido (D-12): el cliente HTTP no puede fijar el precio de una línea.
- **Repudiation:** el dominio no registra; el número y la fecha quedan grabados en la fila.
- **Information Disclosure:** las entidades no sobrescriben `ToString` y las excepciones llevan textos fijos sin el
  valor recibido (D-17), así un DNI no llega al log por un mensaje de excepción (R-02).
- **Denial of Service:** validaciones lineales sobre textos acotados (como máximo 254 caracteres por campo).
- **Elevation of Privilege:** no hay roles en esta versión; el dominio solo crea presupuestos en `Borrador`, no expone
  cambio de estado (FEAT-003c).

### `src/Optica.Infrastructure/Persistencia/PresupuestoRepository.cs` (con `OpticaDbContext.cs` y la migración `Presupuestos`)
- **Spoofing:** usa la conexión de `FabricaConexion` con la cuenta de la Api; no hay credenciales de base.
- **Tampering:** EF Core parametriza todas las consultas, incluido `MAX(Numero)`; CHECK de largos, DNI solo dígitos,
  importes en centésimos y claves foráneas `ON DELETE RESTRICT` con `PRAGMA foreign_keys = ON`; la clave primaria
  `Numero` rechaza un número repetido (R-04). La migración solo crea tablas y no toca `Articulo` (ADR-012).
- **Repudiation:** cada presupuesto conserva número y fecha; no hay borrado ni modificación en esta parte.
- **Information Disclosure:** sin `EnableSensitiveDataLogging`: EF registra el SQL sin los valores de los parámetros,
  así los datos del cliente no llegan al log (R-02). Los datos quedan en claro en el archivo (R-01).
- **Denial of Service:** `BEGIN IMMEDIATE` serializa las altas; con una sola PC y pocas operadoras la espera es
  corta; si se excede el tiempo de espera por defecto de `Microsoft.Data.Sqlite` (30 s), la Api responde 500 genérico.
- **Elevation of Privilege:** el repositorio no expone actualización ni borrado de presupuestos (AGENTS.md).

### `src/Optica.Application/Presupuestos/AltaPresupuesto.cs` (con `ObtenerPresupuesto.cs` y `BuscarArticulos.cs`)
- **Spoofing:** sin identidades propias; recibe datos ya enlazados por el controlador con sesión.
- **Tampering:** el alta relee cada artículo dentro de la transacción y rechaza uno inexistente o inactivo (AC-15,
  D-24), y toma su precio de venta del catálogo; hasta 100 líneas (D-16). Un mismo artículo en dos líneas se admite
  por decisión del usuario (D-16).
- **Repudiation:** el alta devuelve el número grabado y la fecha local del día (D-06).
- **Information Disclosure:** los records `DatosCliente`, `PresupuestoGrabado` y `ClienteGrabado` no imprimen sus
  miembros (`PrintMembers` falso) y la prueba `Alta_NoRegistraDatosDelCliente` fija que el registrador no recibe datos
  del cliente (R-02).
- **Denial of Service:** como máximo 100 lecturas de artículos por alta, todas por clave primaria.
- **Elevation of Privilege:** `BuscarArticulos` con `soloActivos` solo filtra; no cambia permisos ni el listado del
  catálogo.

### `src/Optica.Api/Controllers/PresupuestosController.cs` (con `ArticulosController.cs` y `soloActivos`)
- **Spoofing:** las dos acciones exigen sesión (401 antes que 415 y 413), con la cookie de FEAT-001a; la prueba
  `RutasNuevas_SinSesion_401` y el recorrido de `ProteccionTests` lo fijan (R-03).
- **Tampering:** CSRF: cookie `SameSite=Strict`, sin CORS y JSON obligatorio en el POST (415); números JSON estrictos;
  el pedido lleva solo códigos de artículo, nunca precios (D-12); cuerpo hasta 16 KB (413).
- **Repudiation:** el 201 devuelve el número y `Location`; el log de la Api registra método, ruta y estado, sin el
  cuerpo.
- **Information Disclosure:** los errores 400 usan textos fijos que no repiten el DNI ni el email
  (`Post_Errores400_NoRepitenElValor`); ningún dato personal viaja en la URL (el GET usa solo el número); el 500 es
  genérico (R-02). `SolicitudPresupuesto` y `SolicitudCliente` no imprimen sus miembros.
- **Denial of Service:** cuerpo hasta 16 KB y 100 líneas; `soloActivos` ilegible responde 400 sin consultar la base.
- **Elevation of Privilege:** no hay endpoint de modificación ni de borrado; toda operadora con sesión puede abrir
  cualquier presupuesto por número, que es lo que pide el PRD (sin roles, fuera de alcance) (R-05).

### `web/src/paginas/NuevoPresupuesto.tsx` (con `Presupuesto.tsx` y `web/src/api/cliente.ts`)
- **Spoofing:** las rutas `/presupuestos/nuevo` y `/presupuestos/:numero` usan la misma protección de sesión que el
  resto de la SPA.
- **Tampering:** el Precio Unitario se muestra `readOnly` y no se envía; la validación de pantalla es comodidad y la
  que vale es la del servidor.
- **Repudiation:** sin registro en el navegador.
- **Information Disclosure:** React escapa los textos del cliente al mostrarlos (sin `dangerouslySetInnerHTML`), los
  datos personales no se guardan en `localStorage` ni `sessionStorage` y no van en la URL (R-02, R-06).
- **Denial of Service:** el buscador pide páginas de 50 de a una.
- **Elevation of Privilege:** la SPA no expone acciones que la API no permita.

### `src/Optica.Api/Web/SinCacheApi.cs`
- **Spoofing:** no maneja identidades; corre antes de la autenticación y no la reemplaza.
- **Tampering:** solo agrega un encabezado de respuesta; no lee el cuerpo ni cambia el código de estado.
- **Repudiation:** sin registro propio; el log de la Api no cambia.
- **Information Disclosure:** fija `Cache-Control: no-store` en toda respuesta de `/api`, incluidas las de error,
  para que el navegador no guarde en su caché las respuestas con DNI, domicilio, email y teléfono del cliente (R-08).
- **Denial of Service:** costo constante por pedido (una comparación de ruta y un encabezado).
- **Elevation of Privilege:** no cambia la autorización; la política de sesión por defecto sigue igual.

## Data classification
| Data | Class | At rest | In transit |
|---|---|---|---|
| Apellido, Nombre, DNI, Domicilio, Email y Nro. de Teléfono del cliente | PII (Ley 25.326) | tabla `Presupuesto` de `optica.db` sin cifrar, en el perfil de la cuenta de la Api con permisos NTFS solo para esa cuenta, Administradores y SYSTEM; los respaldos en la carpeta con la ACL de `docs/instalacion.md`; no van al log ni a `ToString` (R-01, R-02) | TLS (HTTPS de Kestrel), solo con sesión, en el cuerpo y nunca en la URL; respuestas con `Cache-Control: no-store` (R-08) |
| Código, descripción y Precio Unitario de la línea | financial (precio comercial de la empresa) | tabla `LineaPresupuesto`, centésimos con CHECK | TLS, solo con sesión |
| Número y fecha del presupuesto | public (identificador interno) | tabla `Presupuesto`, clave primaria | TLS, solo con sesión |
| Cookie de sesión | credentials | sin cambios respecto de FEAT-001a: cifrada con Data Protection, claves con DPAPI | TLS, `Secure`, `HttpOnly`, `SameSite=Strict` |

## Risks and mitigations
| ID | Risk | STRIDE | Likelihood | Impact | Mitigation |
|---|---|---|---|---|---|
| R-01 | Quien accede al archivo `optica.db`, a `optica.db-wal` o a una copia de respaldo lee los datos personales de los clientes en claro | I | L | H | permisos NTFS sobre la carpeta de la base y la de respaldos (`docs/instalacion.md`); `docs/instalacion.md` pasa a advertir que la base y los respaldos contienen datos personales de clientes (Block 6); el cifrado queda aceptado, ver abajo |
| R-02 | Datos del cliente en el log, en un mensaje de error o en `ToString` | I | M | H | `PrintMembers` falso en los records, textos de error fijos, sin `EnableSensitiveDataLogging`, pruebas `Alta_NoRegistraDatosDelCliente`, `Post_Errores400_NoRepitenElValor` y `ToString_ClienteYPresupuesto_SinDatosPersonales` (D-17) |
| R-03 | Lectura o alta de presupuestos sin sesión | S | L | H | la política de autorización por defecto de la Api (`SetFallbackPolicy` con sesión, `Program.cs`) cubre el controlador nuevo sin atributos; `RutasNuevas_SinSesion_401` y el recorrido de `ProteccionTests` |
| R-04 | Dos altas simultáneas toman el mismo número | T | L | M | `MAX(Numero)` dentro de `BEGIN IMMEDIATE` y `Numero` como clave primaria (ADR-015); prueba con 2 sesiones en FEAT-003d |
| R-05 | CSRF o manipulación del precio desde el pedido | T | L | H | `SameSite=Strict`, sin CORS, JSON obligatorio (415); el pedido solo lleva códigos y el precio sale del catálogo (D-12) |
| R-06 | XSS con un dato del cliente (por ejemplo, un domicilio con HTML) | I | L | M | React escapa el texto; sin `dangerouslySetInnerHTML`; los datos no se guardan en el almacenamiento del navegador |
| R-07 | Cuerpo grande o muchas líneas para cargar la API | D | L | L | 16 KB (413) y 100 líneas (D-16); lecturas por clave primaria |
| R-08 | El navegador guarda en su caché las respuestas de `/api` con los datos del cliente y quedan disponibles para otra persona en una PC compartida | I | M | H | `Cache-Control: no-store` en toda respuesta de `/api` (`SinCacheApi`, Block 7, D-37), con pruebas `Api_RespuestasExitosas_NoStore` y `Api_RespuestasDeError_NoStore` |

## Accepted risks
### R-01
- **Accepted by:** el usuario (dueño del producto), 2026-11-06: "dejalo sin cifrar, es un software de prueba".
- **Justification:** el sistema es, por ahora, un software de prueba; la base y los respaldos quedan protegidos por los
  permisos NTFS de sus carpetas, los datos del cliente no van al log, y `docs/instalacion.md` advierte que la base y
  las copias contienen datos personales sin cifrar.
- **Review conditions:** antes de cargar datos reales de clientes o de poner el sistema en producción en la óptica,
  al definir el backup (PRD 09), o si la PC pasa a compartirse con otras cuentas.

## Supply chain
Sin dependencias nuevas: el módulo usa EF Core, ASP.NET Core y React ya presentes en el proyecto.

## Availability
El vector es la carga de la API con cuerpos grandes o muchas líneas, acotado por los 16 KB y las 100 líneas (R-07).
La espera de `BEGIN IMMEDIATE` entre altas simultáneas es corta con una sola PC; la verificación con 2 sesiones y el
volumen de 10.000 presupuestos son de FEAT-003d.
