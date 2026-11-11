# Threat model FEAT-003b: Cantidad, descuento y cálculos de las líneas y el total

| Field | Value |
|-------|-------|
| Ticket | FEAT-003b |
| Spec | docs/ddw/specs/spec-FEAT-003b.md |
| Tier | FEATURE |
| Date | 2026-11-11 |

## Components
| Component | Source in the spec |
|---|---|
| `src/Optica.Domain/Presupuestos/CalculoLinea.cs` (con `LineaPresupuesto.cs` y `Presupuesto.cs`) | Block 1 |
| `src/Optica.Application/Presupuestos/AltaPresupuesto.cs` (con `ValidacionPresupuesto.cs` y `ObtenerPresupuesto.cs`) | Block 2 |
| `src/Optica.Api/Controllers/PresupuestosController.cs` | Block 2 |
| `web/src/presupuestos/reglas.ts` (con `web/src/api/cliente.ts`) | Block 3 |
| `web/src/paginas/NuevoPresupuesto.tsx` (con `Presupuesto.tsx`) | Block 4 |

## Trust boundaries
- Navegador de la operadora → API ASP.NET Core: `POST /api/presupuestos` suma por línea `cantidad` y
  `porcentajeDescuento`, que ahora vienen del pedido; el servidor calcula el Precio con Descuento, el Precio Final y el
  total, y `GET /api/presupuestos/{numero}` devuelve además `total`. El cliente sigue sin poder enviar precios (D-12 de
  FEAT-003a). Los seis datos personales del cliente cruzan igual que en FEAT-003a, sin cambios.
- API → archivo SQLite `optica.db`: las columnas `Cantidad`, `PorcentajeDescuento` (centésimos), `PrecioConDescuento` y
  `PrecioFinal` de `LineaPresupuesto` ya existen, con CHECK `Cantidad >= 1` y descuento `BETWEEN 0 AND 10000`; sin
  migración.
- API → navegador (lectura): la web descarta como ilegible un importe fuera de `IMPORTE_MAXIMO_SEGURO`; el servidor
  aplica el mismo tope antes de grabar (D-04).

## STRIDE analysis
### `src/Optica.Domain/Presupuestos/CalculoLinea.cs` (con `LineaPresupuesto.cs` y `Presupuesto.cs`)
- **Spoofing:** sin identidades; lo usan casos de uso que corren detrás de la sesión.
- **Tampering:** el cálculo es aritmética entera en centésimos con redondeo mitad hacia arriba (FR-04 a FR-06); el
  constructor de `LineaPresupuesto` rechaza cantidad fuera de 1..9.999, descuento fuera de 0..100 o con más de 2
  decimales, y un Precio Final o un total por encima del límite del tipo; el tope del contrato con la web lo controla
  `ValidacionPresupuesto.ControlarTopes` (R-02, ADR-016). El Precio con Descuento y el Precio Final no se aceptan del
  pedido: salen siempre de `DesdeArticulo` (R-01).
- **Repudiation:** el dominio no registra; los importes calculados quedan grabados en la fila de la línea.
- **Information Disclosure:** sin datos personales en la línea; las excepciones llevan textos fijos sin el valor
  recibido (D-17 de FEAT-003a).
- **Denial of Service:** cálculo constante por línea, con 100 líneas como máximo (D-16 de FEAT-003a).
- **Elevation of Privilege:** el dominio sigue creando solo presupuestos en `Borrador`; no hay roles ni descuentos que
  requieran autorización especial en esta versión.

### `src/Optica.Application/Presupuestos/AltaPresupuesto.cs` (con `ValidacionPresupuesto.cs` y `ObtenerPresupuesto.cs`)
- **Spoofing:** sin identidades propias; recibe datos enlazados por el controlador con sesión.
- **Tampering:** valida cantidad y descuento por línea antes de leer la base y devuelve un error por campo
  (`lineas[i].cantidad`, `lineas[i].porcentajeDescuento`); rechaza el alta si el total supera el tope (R-02). El precio
  unitario se relee del catálogo dentro de la transacción (ADR-013).
- **Repudiation:** el alta devuelve el número grabado; los importes grabados son los que calculó el servidor.
- **Information Disclosure:** `DatosPresupuesto` y el nuevo `DatosLinea` conservan `PrintMembers => false`; los textos de
  error son fijos y no repiten el valor (R-04).
- **Denial of Service:** validación lineal sobre como máximo 100 líneas.
- **Elevation of Privilege:** no expone modificación ni cambio de estado (FEAT-003c).

### `src/Optica.Api/Controllers/PresupuestosController.cs`
- **Spoofing:** la política de autorización por defecto exige sesión; el controlador no cambia de ruta.
- **Tampering:** `SolicitudLinea` pasa a llevar `cantidad` (entero) y `porcentajeDescuento` (decimal) obligatorios con
  `JsonNumberHandling.Strict`: un texto, un 2.5 en la cantidad o un valor ausente terminan en 400 con el campo de la
  línea (D-07, D-09); JSON obligatorio (415) y cuerpo hasta 16 KB (413) como en FEAT-003a.
- **Repudiation:** sin cambios: el número del presupuesto identifica el alta.
- **Information Disclosure:** el 400 de deserialización devuelve solo el campo y el texto fijo, nunca el valor ni el
  mensaje del serializador (R-04).
- **Denial of Service:** los mismos límites de cuerpo y de líneas (R-03).
- **Elevation of Privilege:** sin endpoints nuevos.

### `web/src/presupuestos/reglas.ts` (con `web/src/api/cliente.ts`)
- **Spoofing:** sin identidades; el cliente HTTP usa la cookie de sesión existente.
- **Tampering:** el cálculo en pantalla usa BigInt en centésimos y las mismas fórmulas que el servidor; la pantalla
  muestra lo que devuelve el servidor después de grabar, así una diferencia no queda grabada (R-05). `leerPresupuesto`
  exige `total` y que sea la suma de los Precios Finales recibidos. Con el punto de miles (D-29), `validarCantidad`
  solo acepta grupos de 3 dígitos bien formados y lo que envía es el entero ya leído, como número JSON: el servidor no
  interpreta separadores y sigue validando 1 a 9.999 por su cuenta (R-01), así que un texto ambiguo no puede llegar
  grabado como otra cantidad.
- **Repudiation:** no aplica en el navegador.
- **Information Disclosure:** las reglas no registran ni guardan nada; los mensajes son fijos.
- **Denial of Service:** validación y cálculo constantes por línea en el navegador de la operadora; leer una cantidad con
  puntos de miles es lineal en el largo del texto escrito, sin expresiones con retroceso exponencial.
- **Elevation of Privilege:** no aplica.

### `web/src/paginas/NuevoPresupuesto.tsx` (con `Presupuesto.tsx`)
- **Spoofing:** sin cambios: las páginas solo se ven con sesión (FEAT-001a).
- **Tampering:** los campos Cantidad y Descuento validan antes de enviar; la validación que vale es la del servidor.
- **Repudiation:** no aplica en el navegador.
- **Information Disclosure:** React escapa los textos; sin `dangerouslySetInnerHTML`; nada va al almacenamiento del
  navegador.
- **Denial of Service:** recálculo en cada cambio, acotado a 100 líneas.
- **Elevation of Privilege:** no aplica.

## Data classification
| Data | Class | At rest | In transit |
|---|---|---|---|
| Apellido, Nombre, DNI, Domicilio, Email y Nro. de Teléfono del cliente | PII (Ley 25.326) | sin cambios respecto de FEAT-003a: tabla `Presupuesto` sin cifrar, carpeta con permisos NTFS; no van al log ni a `ToString` (R-06) | TLS (HTTPS de Kestrel), solo con sesión, en el cuerpo y nunca en la URL; `Cache-Control: no-store` |
| Cantidad, Porcentaje de Descuento, Precio con Descuento, Precio Final y total | financial (condiciones comerciales de la venta) | tabla `LineaPresupuesto` en centésimos con CHECK; el total se calcula y no se guarda | TLS, solo con sesión |
| Cookie de sesión | credentials | sin cambios respecto de FEAT-001a: cifrada con Data Protection, claves con DPAPI | TLS, `Secure`, `HttpOnly`, `SameSite=Strict` |

## Risks and mitigations
| ID | Risk | STRIDE | Likelihood | Impact | Mitigation |
|---|---|---|---|---|---|
| R-01 | Un pedido directo a la Api fija el Precio con Descuento o el Precio Final de una línea | T | L | H | `SolicitudLinea` no tiene esos campos; el servidor los calcula siempre con `CalculoLinea` a partir del precio del catálogo (Block 1, Block 2); prueba de Api que envía precios y verifica que se ignoran |
| R-02 | Una cantidad o un precio enormes desbordan el cálculo o graban un importe que la web no puede leer | T | L | M | cantidad 1..9.999 (D-01), aritmética en `decimal`/BigInt, tope único `IMPORTE_MAXIMO_SEGURO` para el Precio Final y el total con rechazo y texto fijo (D-04); pruebas en el límite |
| R-03 | Muchas líneas o cuerpos grandes para cargar la Api | D | L | L | sin cambios: 16 KB (413) y 100 líneas (D-16 de FEAT-003a) |
| R-04 | El 400 de un número mal formado repite el valor o el mensaje del serializador | I | L | M | `CampoDeLaClave` traduce el camino JSON al campo y el texto es fijo (D-09); prueba que el cuerpo del 400 no contiene el valor enviado |
| R-05 | El total en pantalla difiere del grabado por redondear distinto | T | M | M | mismas fórmulas en servidor y web, verificadas con los mismos ejemplos (AC-01, AC-04 a AC-07) en pruebas de los dos lados; la pantalla del presupuesto grabado muestra lo que devuelve el servidor |
| R-06 | Quien accede a `optica.db` o a un respaldo lee los datos personales de los clientes en claro | I | L | H | sin cambios en esta parte: permisos NTFS y advertencia de `docs/instalacion.md`; aceptado, ver abajo |

## Accepted risks
### R-06
- **Accepted by:** el usuario (dueño del producto), 2026-11-06, como R-01 de FEAT-003a: "dejalo sin cifrar, es un software de prueba". FEAT-003b no agrega datos personales ni cambia dónde se guardan.
- **Justification:** el sistema es, por ahora, un software de prueba; la base y los respaldos quedan protegidos por los
  permisos NTFS de sus carpetas, los datos del cliente no van al log, y `docs/instalacion.md` advierte que la base y
  las copias contienen datos personales sin cifrar.
- **Review conditions:** antes de cargar datos reales de clientes o de poner el sistema en producción en la óptica,
  al definir el backup (PRD 09), o si la PC pasa a compartirse con otras cuentas.

## Supply chain
Sin dependencias nuevas: BigInt es nativo del navegador (Chrome y Edge vigentes) y el servidor usa `decimal` de .NET.

## Availability
El vector sigue siendo la carga de la Api con cuerpos grandes o muchas líneas, acotado por los 16 KB y las 100 líneas
(R-03). El cálculo agrega trabajo constante por línea.
