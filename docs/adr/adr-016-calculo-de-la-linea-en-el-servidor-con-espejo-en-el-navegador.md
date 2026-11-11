# ADR-016: Cálculo de la línea del presupuesto en el servidor, con espejo en el navegador

| Field | Value |
|-------|-------|
| Date | 2026-11-11 |
| Ticket | FEAT-003b |
| Status | Accepted |

## Context
FEAT-003b (FR-04 a FR-07) pidió calcular el Precio con Descuento, el Precio Final y el total, y recalcularlos en
pantalla cada vez que la operadora cambia la cantidad o el descuento. Hasta entonces el proyecto calculaba los importes
solo en el servidor: el precio de venta del catálogo se mostraba recién después de grabar (`NuevoArticulo.tsx`).
Además, la web lee los importes como `number`, exactos solo hasta `Number.MAX_SAFE_INTEGER` centésimos.

## Options considered

### Option 1: mostrar los importes solo después de grabar
- **Pros:** una sola implementación del cálculo; sigue el patrón del catálogo.
- **Cons:** no cumplía FR-07, que pide el recálculo en pantalla antes de grabar.

### Option 2: endpoint de vista previa calculado en el servidor
- **Pros:** una sola implementación; el navegador nunca calcula.
- **Cons:** un pedido con sesión por cada tecla, una ruta nueva que proteger y probar, y espera visible en la PC de la
  óptica para un cálculo de cuatro operaciones.

### Option 3: el servidor calcula y graba; el navegador calcula en espejo para mostrar
- **Pros:** cumple FR-07 sin pedidos; el servidor sigue siendo la autoridad porque el pedido no lleva precios.
- **Cons:** la misma fórmula en C# y en TypeScript, que puede divergir.

## Decision
Se eligió la opción 3. El dominio (`CalculoLinea`) calcula en centésimos con redondeo mitad hacia arriba y el alta graba
lo que calcula el servidor. La web aplica la misma fórmula en BigInt de centésimos solo para mostrar. La divergencia se
controla con una tabla de casos común (AC-01, AC-04 a AC-07, el punto medio y el tope) que corren `CalculoLineaTests` y
`reglas.test.ts`. El tope de importe que admite la Api es el de la web, `Number.MAX_SAFE_INTEGER / 100`, y vive en la
capa de aplicación como límite del contrato, igual que `CodigoArticuloMaximo`. El dominio conserva su límite de tipo
(`ReglasImporte.ImporteMaximo`).

## Consequences
- Archivos: `src/Optica.Domain/Presupuestos/CalculoLinea.cs`, `ValidacionPresupuesto.cs`,
  `web/src/presupuestos/reglas.ts` y `web/src/configuracion/numeros.ts` (centésimos y suma en BigInt compartidos).
- Un cambio de la fórmula obliga a cambiar los dos lados y la tabla de casos común.
- Limitación heredada de D-22 de FEAT-002a: por encima de 2^46 centésimos, un `number` del JSON puede llegar con un
  centavo de diferencia, y en ese rango el recálculo en pantalla puede diferir del servidor. Se aceptó porque son
  importes que una óptica no usa, y la pantalla del presupuesto grabado muestra lo que devolvió el servidor.
