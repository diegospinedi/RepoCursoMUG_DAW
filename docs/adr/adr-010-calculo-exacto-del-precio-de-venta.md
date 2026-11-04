# ADR-010: Cálculo exacto del precio de venta antes del redondeo hacia arriba

| Field | Value |
|-------|-------|
| Date | 2026-11-04 |
| Ticket | FEAT-002a |
| Status | Accepted |

## Context
FR-06 de FEAT-002a describe el precio de venta en Responsable Inscripto en tres pasos (quitar el IVA del
costo, aplicar el margen, volver a sumar el IVA) sin redondear los intermedios, y FR-09 redondea el
resultado hacia arriba al múltiplo comercial. En `decimal` de .NET la división por (1 + alícuota / 100) no
es exacta: la auditoría de arquitectura midió 7 de 108 combinaciones en las que el resto mínimo, pasado por
el redondeo hacia arriba, grababa un centavo de diferencia (costo −100 y margen 50 % daba −149,99 en lugar
de los −150,00 de AC-06).

## Options considered

### Option 1: los tres pasos literales en `decimal` y redondeo hacia arriba del resultado
- **Pros:** el código lee igual que el PRD.
- **Cons:** precios incorrectos por el resto de la división, justo en los casos que el redondeo hacia
  arriba no perdona.

### Option 2: redondear el resultado a pocos decimales antes del redondeo hacia arriba
- **Pros:** corrige los casos medidos.
- **Cons:** elige una precisión arbitraria y deja abierto el mismo problema con otros valores.

### Option 3: usar la identidad algebraica costo × (100 + margen) / 100 para el valor final
- **Pros:** en Responsable Inscripto la alícuota se cancela entre los pasos (a) y (c), así que el resultado
  es el mismo que el de FR-06 en aritmética exacta; con entradas de 2 decimales el producto tiene como máximo
  4 decimales y `decimal` lo representa sin error.
- **Cons:** la fórmula del código no es la del PRD paso por paso; los pasos (a) y (b) quedan solo como
  desglose.

## Decision
Se eligió la opción 3. `CalculadoraPrecioVenta` calcula el valor final con la identidad exacta en las dos
condiciones fiscales y lo redondea hacia +∞ al múltiplo (D-04); los pasos (a) y (b) se calculan aparte para
el desglose de AC-07. Los casos medidos por la auditoría quedan como pruebas de regresión.

## Consequences
- El precio de venta no depende de la alícuota; la alícuota solo cambia el desglose.
- Archivos: `src/Optica.Domain/Catalogo/CalculadoraPrecioVenta.cs` y sus pruebas.
- Si un PRD futuro cambia la fórmula (por ejemplo, alícuotas de compra y de venta distintas), la identidad
  deja de valer y este ADR se reemplaza.
