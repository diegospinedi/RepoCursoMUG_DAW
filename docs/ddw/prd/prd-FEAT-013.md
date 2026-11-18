# PRD FEAT-013: Búsqueda con sugerencias mientras se escribe

| Campo | Valor |
|-------|-------|
| Ticket | FEAT-013 |
| Tracker | ninguno |
| Fecha | 2026-11-17 |
| PRD loops | 1 |
| Loops since last human decision | 0 |

> **Origen.** El usuario lo pidió el 2026-11-17 en dos mensajes:
> - "agrega en los buscadores filtrado a medida que escribo para hacer más sencillas las búsquedas, ídem en
>   desplegables";
> - "me está faltando que los buscadores sugieran a medida que escribo".
>
> **Decisiones del usuario del mismo día:**
> - Es un ticket aparte de FEAT-012.
> - Los buscadores sugieren y además filtran.
> - Los dos desplegables de Configuración pasan a ser un campo con filtro.
> - Se arranca sobre la rama de FEAT-012.

## Context and Problem
Hoy hay cuatro buscadores, y los cuatro buscan recién cuando la operadora aprieta "Buscar":
- Artículos;
- Buscar presupuestos;
- Facturas;
- el buscador de artículos dentro de un presupuesto.

Para encontrar un artículo o un cliente, la operadora escribe, aprieta "Buscar", revisa la tabla y repite si no acertó.
No ve nada mientras escribe, así que corregir una búsqueda le cuesta varios intentos.

En Configuración hay dos desplegables: la alícuota de IVA y la condición fiscal. El usuario los quiere con el mismo
comportamiento: escribir y ver solo las opciones que coinciden.

## Goals
- Que la operadora encuentre un artículo, un presupuesto o una factura escribiendo unas pocas letras, sin apretar
  "Buscar".
- Que elegir una sugerencia la lleve directo a lo que buscaba.
- Que los desplegables de Configuración se puedan filtrar escribiendo.

## Functional Requirements

### Buscadores
- FR-01: El sistema debe mostrar debajo del campo de búsqueda de Artículos una lista de hasta 8 sugerencias que
  coincidan con lo escrito. Cada sugerencia muestra el código, el código en el proveedor y la descripción. La lista
  aparece cuando hay al menos 2 caracteres y la operadora deja de escribir por 300 ms.
- FR-02: El sistema debe actualizar la tabla de resultados de Artículos con lo escrito, en el mismo momento que las
  sugerencias, sin apretar "Buscar".
- FR-03: El sistema debe llevar a la pantalla de edición del artículo cuando la operadora elige una sugerencia de
  Artículos.
- FR-04: El sistema debe aplicar FR-01 y FR-02 a los campos de texto Apellido, Nombre y DNI de Buscar presupuestos.
  Cada sugerencia muestra el número, el apellido y el nombre del cliente y la fecha del presupuesto, y combina lo
  escrito en los tres campos con las fechas elegidas.
- FR-05: El sistema debe abrir el presupuesto cuando la operadora elige una sugerencia de Buscar presupuestos.
- FR-06: El sistema debe aplicar FR-01 y FR-02 a los campos de texto de Facturas. Cada sugerencia muestra el
  comprobante, el cliente y la fecha.
- FR-07: El sistema debe abrir la factura cuando la operadora elige una sugerencia de Facturas.
- FR-08: El sistema debe aplicar FR-01 y FR-02 al buscador de artículos dentro de un presupuesto, y agregar el
  artículo al presupuesto cuando la operadora elige la sugerencia, igual que con el botón "Agregar".
- FR-09: El sistema debe permitir recorrer las sugerencias con las flechas, elegir una con Enter y cerrar la lista con
  Escape. Con la lista cerrada o sin una sugerencia marcada, Enter busca como hoy.
- FR-10: El sistema debe mantener el botón "Buscar" en los cuatro buscadores.
- FR-11: El sistema debe mostrar siempre las sugerencias y la tabla de la última búsqueda escrita, aunque una
  respuesta anterior llegue después.
- FR-12: El sistema debe ocultar las sugerencias y mostrar el mensaje de error de hoy cuando la búsqueda falla.

### Desplegables de Configuración
- FR-13: El sistema debe reemplazar los desplegables de alícuota de IVA y de condición fiscal por un campo que, al
  escribir, muestra solo las opciones de la lista que coinciden. Se compara sin distinguir mayúsculas ni acentos.
- FR-14: El sistema debe aceptar en esos campos solamente una opción de la lista. Un texto que no coincide con
  ninguna no se graba y se muestra el error junto al campo.

## Non-Functional Requirements
- NFR-01: Con un catálogo de 10.000 artículos, las sugerencias y la tabla deben aparecer en menos de 1 segundo
  (percentil 95) desde la última tecla, contando la pausa de 300 ms.
- NFR-02: Cada pausa de escritura debe generar como mucho 1 pedido por campo. Escribir 10 caracteres seguidos sin
  pausa genera 1 solo pedido.
- NFR-03: Los cuatro buscadores y los dos campos con filtro deben cumplir WCAG 2.1 AA. Los anuncios del lector de
  pantalla dicen la cantidad de sugerencias, y la navegación es completa con teclado.
- NFR-04: Todos los criterios deben cumplirse en Chrome y en Edge, en su versión estable vigente (2 combinaciones).
- NFR-05: El cambio no debe agregar dependencias nuevas a la interfaz web (0 paquetes).

## Acceptance Criteria
- AC-01 (FR-01, FR-02): WHEN la operadora escribe "arm" en Artículos y espera 300 ms, THE sistema SHALL mostrar hasta 8
  sugerencias que contienen "arm" en el código, en el código en el proveedor o en la descripción, y la tabla filtrada
  con el mismo texto.
- AC-02 (FR-01): IF lo escrito tiene menos de 2 caracteres, THEN THE sistema SHALL no mostrar sugerencias.
- AC-03 (FR-03): WHEN la operadora elige la sugerencia del artículo 7, THE sistema SHALL abrir la edición del artículo
  7.
- AC-04 (FR-04, FR-05): WHEN la operadora escribe "pér" en Apellido de Buscar presupuestos y elige la sugerencia del
  presupuesto 155, THE sistema SHALL abrir el presupuesto 155.
- AC-05 (FR-06, FR-07): WHEN la operadora escribe parte del apellido del cliente en Facturas y elige una sugerencia,
  THE sistema SHALL abrir esa factura.
- AC-06 (FR-08): WHEN dentro de un presupuesto en Borrador la operadora escribe en el buscador de artículos y elige
  una sugerencia, THE sistema SHALL agregar ese artículo como línea del presupuesto.
- AC-07 (FR-09): WHEN la lista de sugerencias está abierta y la operadora usa flecha abajo, Enter o Escape, THE
  sistema SHALL marcar la siguiente sugerencia, elegir la marcada o cerrar la lista, respectivamente.
- AC-08 (FR-10): WHEN la operadora aprieta "Buscar", THE sistema SHALL buscar como antes de este cambio.
- AC-09 (FR-11): IF la respuesta de una búsqueda anterior llega después que la de la última búsqueda, THEN THE sistema
  SHALL seguir mostrando las sugerencias y la tabla de la última búsqueda.
- AC-10 (FR-12): IF la búsqueda falla por un error de la Api o de la red, THEN THE sistema SHALL ocultar las
  sugerencias y mostrar el mensaje de error de hoy.
- AC-11 (FR-13): WHEN la operadora escribe "10" en el campo de alícuota de IVA, THE sistema SHALL mostrar solo la
  opción 10,5.
- AC-12 (FR-14): IF la operadora escribe en el campo de condición fiscal un texto que no coincide con ninguna opción e
  intenta grabar, THEN THE sistema SHALL no grabar y mostrar el error junto al campo.
- AC-13 (NFR-01, NFR-02): WHEN, con 10.000 artículos, la operadora escribe "armazón" seguido y espera, THE sistema
  SHALL hacer 1 solo pedido y mostrar las sugerencias en menos de 1 segundo.
- AC-14 (NFR-03, NFR-04): WHEN se ejecutan AC-01 a AC-12 en Chrome y en Edge, en su versión estable vigente, con
  teclado, THE sistema SHALL cumplirlos y anunciar la cantidad de sugerencias a los lectores de pantalla.

## Out of Scope
- Sugerencias en los campos de fecha (Desde y Hasta).
- Historial de búsquedas o sugerencias guardadas en el navegador.
- Búsqueda difusa o tolerante a errores de tipeo: se busca por coincidencia del texto, como hoy.
- Cambiar los filtros, los textos o los resultados de las búsquedas actuales.
- Otros desplegables: hoy no hay más que los dos de Configuración.

## Risks and Mitigations
- Riesgo: las sugerencias de presupuestos y de facturas muestran datos personales de clientes (apellido, nombre y
  DNI). Mitigación: se muestran solo con la sesión iniciada, como hoy la tabla. No se guardan en el navegador ni en el
  historial de autocompletado, y no van a la URL ni al log.
- Riesgo: un pedido por tecla sobrecarga la Api. Mitigación: la pausa de 300 ms y el mínimo de 2 caracteres (NFR-02).
- Riesgo: una respuesta lenta pisa a otra más nueva. Mitigación: FR-11 y AC-09.
- Riesgo: la lista de sugerencias no es accesible. Mitigación: NFR-03 y AC-14.

## Dependencies
- PRD 02 (DISC-001) y FEAT-002b: búsqueda de artículos.
- PRD 05 (DISC-001), FEAT-006: búsqueda de presupuestos.
- PRD 08 (DISC-001), FEAT-009: búsqueda de facturas.
- FEAT-003a: buscador de artículos dentro de un presupuesto.
- FEAT-001c y FEAT-007: desplegables de Configuración.
- FEAT-012: estilo uniforme, base de esta rama.
