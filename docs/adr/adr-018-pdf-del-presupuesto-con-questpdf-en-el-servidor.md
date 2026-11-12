# ADR-018: PDF del presupuesto generado en el servidor con QuestPDF

| Field | Value |
|-------|-------|
| Date | 2026-11-12 |
| Ticket | FEAT-006 |
| Status | Accepted |

## Context
FEAT-006 entrega el presupuesto en Final como PDF con el logo y los colores de la óptica
(`docs/ddw/prd/prd-FEAT-006.md`, FR-11 a FR-16), y el pedido directo a la Api de un Borrador se rechaza (FR-12). El
logo puede ser PNG, JPG o SVG (`LogoMarca`, FEAT-001a): la marca de ejemplo con la que corren el e2e y la CI es un SVG
y la marca real de la óptica es un PNG. El proyecto no tenía ninguna biblioteca de PDF.

## Options considered

### Option 1: QuestPDF en Optica.Infrastructure
- **Pros:** maquetado declarativo en C#, dibuja PNG, JPG y SVG, y fija las fuentes en el binario.
- **Cons:** licencia Community, gratuita solo para empresas que facturan menos de USD 1 millón por año; suma unos 10 MB
  de binarios nativos de SkiaSharp.

### Option 2: PDFsharp/MigraDoc (MIT)
- **Pros:** licencia libre sin condiciones de facturación.
- **Cons:** no dibuja SVG, así que la marca de ejemplo saldría sin logo o habría que pedir otra imagen; más código de
  maquetado.

### Option 3: jsPDF en el navegador (MIT)
- **Pros:** sin dependencias nuevas en el servidor.
- **Cons:** el documento lo arma la pantalla y no el servidor; dibujar el SVG y verificar el contenido es más frágil.

## Decision
El usuario eligió la opción 1 el 2026-11-12, aceptando la condición de facturación de la licencia Community. La
generación quedó detrás de `IGeneradorPdfPresupuesto` (Application), implementada en Infrastructure, que es el único
proyecto que referencia QuestPDF y donde se fija la licencia. Las pruebas leen el PDF con PdfPig y el e2e con
pdfjs-dist.

## Consequences
- Archivos: `src/Optica.Infrastructure/Pdf/GeneradorPdfPresupuesto.cs`, `ConfiguracionInfraestructura.cs`, el `.csproj`
  de Infrastructure y los `packages.lock.json` de los proyectos que lo referencian.
- Si la óptica supera USD 1 millón de facturación anual, hace falta una licencia paga de QuestPDF o reemplazar el
  generador detrás de la misma interfaz.
- La Api y el dominio no dependen de QuestPDF; cambiar de biblioteca toca solo Infrastructure.
