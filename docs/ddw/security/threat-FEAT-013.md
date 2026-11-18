# Threat model FEAT-013: Búsqueda con sugerencias mientras se escribe

| Field | Value |
|-------|-------|
| Ticket | FEAT-013 |
| Spec | docs/ddw/specs/spec-FEAT-013.md |
| Tier | FEATURE |
| Date | 2026-11-17 |

Los cuatro buscadores pasan a consultar la Api mientras se escribe y a mostrar sugerencias. Los dos desplegables de
Configuración pasan a ser campos con filtro local. Los endpoints de búsqueda no cambian. Siguen vigentes los modelos de
FEAT-001a (sesión y CSP), FEAT-006 (filtros en el cuerpo, ADR-019) y FEAT-009.

## Components
| Component | Source in the spec |
|---|---|
| `web/src/componentes/useBusquedaEnVivo.ts` | Block 1 |
| `web/src/componentes/ListaSugerencias.tsx` | Block 1 |
| `web/src/api/cliente.ts` | Block 1 |
| `web/src/paginas/BuscarPresupuestos.tsx` | Block 3 |
| `web/src/paginas/Configuracion.tsx` | Block 4 |

## Trust boundaries
- Navegador → Api: cada pausa de escritura envía una búsqueda con la sesión. Los artículos van por GET con el texto en
  la query; presupuestos y facturas, por POST con el filtro en el cuerpo (ADR-019).
- Api → navegador: la respuesta con datos de clientes (apellido, nombre y fecha) se dibuja en la lista de sugerencias.

## STRIDE analysis
### `web/src/componentes/useBusquedaEnVivo.ts`
- **Spoofing:** los pedidos usan la cookie de sesión de siempre. Sin sesión, la Api responde 401 y la pantalla va al
  ingreso.
- **Tampering:** la respuesta de un pedido viejo se descarta por número de pedido, así que nunca pisa a la búsqueda
  actual (R-03).
- **Repudiation:** no aplica: buscar no cambia datos.
- **Information Disclosure:** no escribe en la consola ni guarda lo buscado en el almacenamiento del navegador.
- **Denial of Service:** con una pausa de 300 ms y 2 caracteres como mínimo, hace como mucho un pedido por pausa
  (R-01).
- **Elevation of Privilege:** no aplica: llama a los mismos endpoints, con la misma autorización.

### `web/src/componentes/ListaSugerencias.tsx`
- **Spoofing:** no aplica.
- **Tampering:** dibuja con JSX los textos que vienen de la Api, sin `dangerouslySetInnerHTML`. Un nombre con `<script>`
  se ve como texto (R-04).
- **Repudiation:** no aplica.
- **Information Disclosure:** la región que anuncia los resultados dice solo la cantidad, nunca nombres (R-02).
- **Denial of Service:** muestra como mucho 8 sugerencias.
- **Elevation of Privilege:** no aplica.

### `web/src/api/cliente.ts`
- **Spoofing:** sin cambios en las credenciales: `credentials: 'same-origin'` y `cache: 'no-store'`.
- **Tampering:** la señal de cancelación solo corta el pedido. Un pedido cancelado devuelve `cancelado`, no un error
  falso.
- **Repudiation:** no aplica.
- **Information Disclosure:** los filtros con datos personales siguen en el cuerpo y no en la URL (ADR-019).
- **Denial of Service:** cancelar libera la conexión del navegador. En la Api, el token de cancelación corta la
  consulta.
- **Elevation of Privilege:** no aplica.

### `web/src/paginas/BuscarPresupuestos.tsx`
- **Spoofing:** detrás de la sesión, como hoy.
- **Tampering:** las validaciones de largo y de fechas se mantienen. En vivo, una entrada inválida no se envía.
- **Repudiation:** no aplica.
- **Information Disclosure:** las sugerencias muestran número, apellido, nombre y fecha, sin el DNI (minimización,
  Ley 25.326, R-02). Los campos siguen con `autoComplete="off"`.
- **Denial of Service:** un pedido por pausa. La guarda vieja se reemplaza por el número de pedido.
- **Elevation of Privilege:** no aplica.

### `web/src/paginas/Configuracion.tsx`
- **Spoofing:** detrás de la sesión, como hoy.
- **Tampering:** el campo con filtro solo acepta una opción de la lista. Un texto libre no se graba, y la Api valida
  igual que hoy.
- **Repudiation:** sin cambios: la grabación sigue registrada en el log de configuración.
- **Information Disclosure:** no aplica: solo hay opciones fijas.
- **Denial of Service:** no aplica: filtra en el navegador, sin pedidos.
- **Elevation of Privilege:** no aplica.

## Data classification
| Data | Class | At rest | In transit |
|---|---|---|---|
| Apellido, nombre y DNI del cliente (filtros y sugerencias) | PII | en la base, sin cambios; no se guarda en el navegador ni en la URL | TLS; filtros en el cuerpo del POST (ADR-019) |
| Texto buscado de artículos | public | no se guarda | TLS, query del GET |
| Alícuota y condición fiscal | public | en la base, sin cambios | TLS |

## Risks and mitigations
| ID | Risk | STRIDE | Likelihood | Impact | Mitigation |
|---|---|---|---|---|---|
| R-01 | Un pedido por tecla sobrecarga la Api o SQLite | D | M | M | Pausa de 300 ms, mínimo de 2 caracteres, cancelación del pedido anterior; prueba de 1 pedido por pausa (AC-13) |
| R-02 | Las sugerencias o los anuncios exponen más datos personales de los necesarios | I | M | M | Sin DNI en las sugerencias, anuncio solo con la cantidad, sin consola ni almacenamiento, `autoComplete="off"` (D-15) |
| R-03 | Una respuesta vieja muestra resultados de otra búsqueda | T | M | L | Número de pedido y `AbortController` (FR-11, AC-09) |
| R-04 | Un nombre con HTML en una sugerencia se ejecuta | I | L | M | JSX sin `dangerouslySetInnerHTML`; prueba con `<img onerror>` |
| R-05 | Cada pedido cancelado deja un error en el log de la Api | R | M | L | Prueba de integración y, si hace falta, el manejador lo trata como fin silencioso (D-16) |

## Supply chain
Sin dependencias nuevas (NFR-05): el combobox es propio (ADR-032).

## Availability
El único vector es R-01, mitigado en el cliente. Las búsquedas no tienen limitador en la Api y la app la usan pocas
operadoras en la PC del local.
