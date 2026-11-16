# ADR-028: Datos del emisor como agregado propio

| Field | Value |
|-------|-------|
| Date | 2026-11-16 |
| Ticket | FEAT-009 |
| Status | Accepted |

## Context
El PDF de la factura necesita la razón social, el domicilio comercial, el CUIT, el número de Ingresos Brutos y la fecha
de inicio de actividades de la óptica, que se cargan en la configuración (FR-01 de `prd-FEAT-009.md`) y se exigen los
cinco juntos (FR-02). La configuración de negocio (`ConfiguracionNegocio`, FEAT-001c y FEAT-007) ya tenía alícuota,
condición fiscal, múltiplo, tope y punto de venta, grabados en un solo POST que recalcula los precios del catálogo.

## Options considered

### Option 1: columnas nuevas en `ConfiguracionNegocio`
- **Pros:** una sola tabla, un solo endpoint y una sola pantalla de grabación.
- **Cons:** al ser obligatorios (FR-02), grabar una alícuota exigiría cargar antes los datos del emisor, y cambiaría el
  comportamiento de FEAT-001c y FEAT-007 y sus pruebas; mezcla datos fiscales del comprobante con el recálculo de
  precios.

### Option 2: agregado propio `DatosEmisor`
- **Pros:** reglas y obligatoriedad propias sin tocar la grabación existente; tabla nueva sin reconstruir
  `ConfiguracionNegocio`; endpoint y sección de pantalla independientes.
- **Cons:** una tabla, un repositorio y un endpoint más; dos botones Grabar en la pantalla de configuración.

## Decision
Se eligió el agregado propio: `DatosEmisor` (fila única) con la tabla `DatosEmisor`, `IDatosEmisorRepository`,
`GET/POST /api/configuracion/emisor` y su sección en la pantalla de configuración. El CUIT pasó a ser un objeto de valor
de dominio (`Cuit`) que también usa la validación de `Arca:Cuit` en `OpcionesArca`.

## Consequences
- Migración nueva `DatosEmisor` (`CREATE TABLE`), sin cambios en `ConfiguracionNegocio` ni en su POST.
- La grabación de la configuración de negocio y su recálculo siguen igual.
- El PDF no se genera hasta que los cinco datos estén cargados (FR-04).
- El CUIT del emisor y `Arca:Cuit` quedan en lugares distintos; que coincidan quedó a cargo de la instalación
  (`docs/instalacion.md`, R-10 de `threat-FEAT-009.md`) y es condición previa para facturar en producción.
- El PDF de una factura vieja se arma con los datos del emisor vigentes al generarlo, no con los de la fecha de
  emisión (Out of Scope de `prd-FEAT-009.md`); si el contador lo objeta, la alternativa es guardar una copia del emisor
  con cada factura.
