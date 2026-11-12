# Threat model FEAT-003c: Edición en Borrador y paso a Final irreversible

| Field | Value |
|-------|-------|
| Ticket | FEAT-003c |
| Spec | docs/ddw/specs/spec-FEAT-003c.md |
| Tier | FEATURE |
| Date | 2026-11-11 |

## Components
| Component | Source in the spec |
|---|---|
| `src/Optica.Domain/Presupuestos/Presupuesto.cs` (con `LineaPresupuesto.cs`) | Block 1 |
| `src/Optica.Infrastructure/Persistencia/Migraciones/*_CierrePresupuestos.cs` (con `PresupuestoRepository.cs` y `UnidadDeTrabajoEf.cs`) | Block 2 |
| `src/Optica.Application/Presupuestos/ModificarPresupuesto.cs` (con `PasarPresupuestoAFinal.cs` y `ValidacionPresupuesto.cs`) | Block 3 |
| `src/Optica.Api/Controllers/PresupuestosController.cs` | Block 3 |
| `web/src/paginas/EditarPresupuesto.tsx` (con `Presupuesto.tsx`, `presupuestos/FormularioPresupuesto.tsx` y `api/cliente.ts`) | Block 4 |

## Trust boundaries
- Navegador de la operadora → API ASP.NET Core: `PUT /api/presupuestos/{numero}` lleva la versión leída, los seis
  datos personales del cliente y las líneas (con `orden` para las existentes); `PUT /api/presupuestos/{numero}/estado`
  lleva `estado` y `version`. Cruzan en el cuerpo, nunca en la URL, solo con sesión.
- API → archivo SQLite `optica.db`: la migración `CierrePresupuestos` agrega `Version` y los triggers que rechazan
  modificar o borrar un presupuesto en Final, tocar sus líneas, cambiar `Fecha` o `Numero` y pasar de Final a Borrador.
  La base aplica la regla aunque se saltee la Api (ADR-017).
- API → navegador (lectura): `GET` y los dos `PUT` devuelven el presupuesto releído con su `version`; la pantalla
  traduce los 409 por su `motivo` y nunca muestra el texto del servidor.

## STRIDE analysis
### `src/Optica.Domain/Presupuestos/Presupuesto.cs` (con `LineaPresupuesto.cs`)
- **Spoofing:** sin identidades; lo usan casos de uso que corren detrás de la sesión.
- **Tampering:** `Modificar` y `PasarAFinal` rechazan un presupuesto en Final y conservan `Numero` y `Fecha` (FR-01,
  FR-05, FR-06); una línea existente recalcula con su Precio Unitario grabado, nunca con uno del pedido (R-02).
- **Repudiation:** el dominio no registra; la versión incrementada deja rastro de cada grabado en la fila.
- **Information Disclosure:** excepciones con textos fijos, sin datos del cliente ni valores recibidos.
- **Denial of Service:** el trabajo sigue acotado a 100 líneas.
- **Elevation of Privilege:** no hay transición de Final a Borrador en el modelo (R-01).

### `src/Optica.Infrastructure/Persistencia/Migraciones/*_CierrePresupuestos.cs` (con `PresupuestoRepository.cs` y `UnidadDeTrabajoEf.cs`)
- **Spoofing:** sin identidades; la base es local y solo la abre la Api.
- **Tampering:** los triggers rechazan con `RAISE(ABORT, ...)` todo UPDATE o DELETE de un presupuesto en Final, todo
  INSERT, UPDATE o DELETE de sus líneas, todo cambio de `Fecha` o `Numero` y el paso de Final a Borrador (R-01, R-03);
  `Version` es token de concurrencia, así un grabado con versión vieja no escribe (R-04). `Version` se agrega con
  `ADD COLUMN`, sin reconstruir la tabla (ADR-012).
- **Repudiation:** sin cambios: la fila conserva fecha, número y estado; la versión cuenta los grabados.
- **Information Disclosure:** `UnidadDeTrabajoEf` traduce el rechazo del trigger (`registro_cerrado`) a un conflicto `RegistroCerrado` sin
  adjuntar la excepción de SQLite ni las entradas con los datos de la fila (R-05).
- **Denial of Service:** el reemplazo de líneas de un Borrador ocurre en una sola transacción `BEGIN IMMEDIATE`
  (ADR-013) con hasta 100 líneas.
- **Elevation of Privilege:** ningún camino de escritura queda fuera de los triggers, incluidos scripts o migraciones
  futuras que escriban la tabla directamente.

### `src/Optica.Application/Presupuestos/ModificarPresupuesto.cs` (con `PasarPresupuestoAFinal.cs` y `ValidacionPresupuesto.cs`)
- **Spoofing:** sin identidades propias; recibe datos enlazados por el controlador con sesión.
- **Tampering:** reutiliza las validaciones del alta (cliente, cantidad, descuento, topes); una línea con `orden` debe
  coincidir con una existente y con su código grabado, y una línea nueva solo admite un artículo activo (R-02);
  compara la versión antes de escribir y relee después de confirmar (R-04).
- **Repudiation:** devuelve el presupuesto releído con la versión nueva.
- **Information Disclosure:** los records de datos conservan `PrintMembers => false`; los resultados de conflicto llevan
  solo un motivo fijo (R-05).
- **Denial of Service:** validación lineal sobre como máximo 100 líneas, antes de abrir la transacción.
- **Elevation of Privilege:** un presupuesto en Final devuelve conflicto `final` sin escribir; no existe operación para
  volver a Borrador (R-01).

### `src/Optica.Api/Controllers/PresupuestosController.cs`
- **Spoofing:** la política de autorización por defecto exige sesión en los dos `PUT` (401 antes de 413 y 415).
- **Tampering:** JSON estricto, `version` obligatoria entre 0 y 2^53−1, `estado` con un único valor admitido ("final"),
  cuerpo hasta 16 KB (413) y JSON obligatorio (415); los precios que lleguen en el pedido se ignoran (R-02).
- **Repudiation:** sin cambios: el número identifica el presupuesto.
- **Information Disclosure:** las solicitudes nuevas anulan `PrintMembers`; los 400 y 409 llevan campo, motivo y texto
  fijos, sin valores; las respuestas llevan `Cache-Control: no-store` (R-05).
- **Denial of Service:** los mismos límites de cuerpo y de líneas que el alta.
- **Elevation of Privilege:** `estado: "borrador"` sobre un Final responde 409 `final` (R-01).

### `web/src/paginas/EditarPresupuesto.tsx` (con `Presupuesto.tsx`, `presupuestos/FormularioPresupuesto.tsx` y `api/cliente.ts`)
- **Spoofing:** sin identidades; usa la cookie de sesión existente.
- **Tampering:** el paso a Final pide confirmación con un diálogo propio y envía la versión mostrada (R-03, R-04);
  las líneas existentes muestran su Precio Unitario grabado y no lo envían.
- **Repudiation:** no aplica en el navegador.
- **Information Disclosure:** los datos del cliente no van a la URL, al almacenamiento del navegador ni a la consola;
  los 409 muestran textos propios de la pantalla (R-05).
- **Denial of Service:** no aplica.
- **Elevation of Privilege:** un presupuesto en Final no muestra acciones de edición; aunque se fuerce la ruta
  `/editar`, la Api rechaza el cambio (R-01).

## Data classification
| Data | Class | At rest | In transit |
|---|---|---|---|
| Apellido, Nombre, DNI, Domicilio, Email y Nro. de Teléfono del cliente | PII (Ley 25.326) | sin cambios respecto de FEAT-003a: tabla `Presupuesto` sin cifrar, carpeta con permisos NTFS; no van al log ni a `ToString` (R-06) | TLS (HTTPS de Kestrel), solo con sesión, en el cuerpo y nunca en la URL; `Cache-Control: no-store` |
| Estado, versión y líneas del presupuesto | financial (base de la factura) | tabla `Presupuesto` y `LineaPresupuesto`, inmutables en Final por triggers | TLS, solo con sesión |
| Cookie de sesión | credentials | sin cambios respecto de FEAT-001a: cifrada con Data Protection, claves con DPAPI | TLS, `Secure`, `HttpOnly`, `SameSite=Strict` |

## Risks and mitigations
| ID | Risk | STRIDE | Likelihood | Impact | Mitigation |
|---|---|---|---|---|---|
| R-01 | Un presupuesto en Final se modifica o vuelve a Borrador por la pantalla, un pedido directo o una escritura directa en la base | T | M | H | regla en dominio, caso de uso (409 `final`) y triggers de `CierrePresupuestos` (ADR-017); pruebas de Api con pedidos directos sobre un Final y pruebas de integración que escriben la tabla saltando la Api |
| R-02 | Un pedido de edición cambia el Precio Unitario de una línea existente o la reasigna a otro artículo | T | L | H | el pedido no lleva precios; la línea con `orden` debe coincidir con su código grabado y recalcula con su precio grabado; prueba que envía precios y otro código |
| R-03 | La operadora pasa a Final por error | T | M | M | diálogo de confirmación con aviso y opción de cancelar (FR-04); riesgo residual aceptado en el PRD: se corrige con un presupuesto nuevo |
| R-04 | Dos sesiones graban el mismo Borrador y una pisa a la otra, o se pasa a Final un contenido que la operadora no vio | T | M | M | `Version` como token de concurrencia (D-01): grabado o paso a Final con versión vieja → 409 `cambiado` sin escribir; prueba con dos grabados de la misma versión |
| R-05 | Un 409, el error del trigger o un log repiten datos del cliente | I | L | M | textos y motivos fijos, sin adjuntar la excepción de SQLite ni las entradas de EF; records con `PrintMembers` falso; prueba que el cuerpo del 409 no contiene datos del pedido |
| R-06 | Quien accede a `optica.db` o a un respaldo lee los datos personales de los clientes en claro | I | L | H | sin cambios en esta parte: permisos NTFS y advertencia de `docs/instalacion.md`; aceptado, ver abajo |

## Accepted risks
### R-06
- **Accepted by:** el usuario (dueño del producto), 2026-11-06, como R-01 de FEAT-003a: "dejalo sin cifrar, es un software de prueba". FEAT-003c permite modificar esos datos pero no agrega datos nuevos ni cambia dónde se guardan.
- **Justification:** el sistema es, por ahora, un software de prueba; la base y los respaldos quedan protegidos por los
  permisos NTFS de sus carpetas, los datos del cliente no van al log, y `docs/instalacion.md` advierte que la base y
  las copias contienen datos personales sin cifrar.
- **Review conditions:** antes de cargar datos reales de clientes o de poner el sistema en producción en la óptica,
  al definir el backup (PRD 09), o si la PC pasa a compartirse con otras cuentas.

## Supply chain
Sin dependencias nuevas: los triggers son SQL de SQLite y el diálogo usa el elemento `<dialog>` nativo del navegador.

## Availability
El vector sigue siendo la carga de la Api con cuerpos grandes o muchas líneas, acotado por los 16 KB y las 100 líneas
del alta, que los dos `PUT` reutilizan.
