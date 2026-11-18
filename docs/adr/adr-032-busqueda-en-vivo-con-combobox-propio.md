# ADR-032: Búsqueda en vivo con combobox propio y cancelación de pedidos

| Field | Value |
|-------|-------|
| Date | 2026-11-17 |
| Ticket | FEAT-013 |
| Status | Accepted |

## Context
Hasta FEAT-012, los cuatro buscadores de la web buscaban solo con Enter o con el botón "Buscar", nunca en cada tecla
(D-12 de FEAT-002b, D-15 de FEAT-003a, FEAT-006 y FEAT-009). El usuario pidió que sugieran y filtren mientras se
escribe, y que los desplegables de Configuración también filtren (`docs/ddw/prd/prd-FEAT-013.md`). Hacía falta decidir
dos cosas: cómo limitar los pedidos y que la última búsqueda gane, y con qué componente dibujar la lista.

## Options considered

### Option 1: combobox propio, pausa de 300 ms, número de pedido y AbortController
- **Pros:** sin dependencias (NFR-05); estilo de FEAT-012; ARIA 1.2 controlado y probado; un pedido por pausa; una
  respuesta vieja nunca pisa a la nueva.
- **Cons:** más código propio; hay que enseñarle al cliente de la Api a cancelar sin reportar falla de red.

### Option 2: `<datalist>` nativo
- **Pros:** cero código de lista.
- **Cons:** no permite plegar acentos (FR-13); no obliga a elegir una opción de la lista (FR-14); su comportamiento
  cambia según el navegador; no toma el estilo de FEAT-012 y Playwright no puede inspeccionarlo.

### Option 3: una librería de combobox
- **Pros:** accesibilidad resuelta.
- **Cons:** agrega una dependencia (NFR-05 pide cero) y otra superficie de cadena de suministro.

### Option 4: buscar en cada tecla, sin pausa
- **Pros:** respuesta inmediata.
- **Cons:** un pedido por tecla (NFR-02) y más carga para SQLite.

## Decision
Se eligió la opción 1:
- una base `ListaSugerencias`, compartida por `CampoBusqueda` (sugerencias de la Api) y `CampoConOpciones` (filtrado
  local);
- el hook `useBusquedaEnVivo`, con 300 ms de pausa, mínimo de 2 caracteres, número de pedido y `AbortController`;
- `cliente.ts` acepta una señal de cancelación y devuelve `cancelado`;
- una sola búsqueda (la página 1) alimenta la tabla y las 8 sugerencias.

## Consequences
- Queda revertida la regla "nunca en cada tecla" de FEAT-002b, FEAT-003a, FEAT-006 y FEAT-009. El botón "Buscar" sigue.
- Cambian `web/src/api/cliente.ts`, las cuatro pantallas de búsqueda y `Configuracion.tsx`. Se agregan tres
  componentes y un hook en `web/src/componentes`.
- La Api recibe como mucho un pedido de búsqueda por pausa y por campo. Las búsquedas no tienen limitador; se confía
  en la pausa y en que trabajan pocas operadoras.
- Los dos `select` de Configuración dejan de existir. Las pruebas que usaban `selectOption` pasan a escribir y elegir.
