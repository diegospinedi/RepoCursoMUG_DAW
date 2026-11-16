# ADR-027: QRCoder para el QR de ARCA

| Field | Value |
|-------|-------|
| Date | 2026-11-16 |
| Ticket | FEAT-009 |
| Status | Accepted |

## Context
El PDF de la factura lleva el código QR que exige ARCA (FR-10 de `prd-FEAT-009.md`): una URL
`https://www.afip.gob.ar/fe/qr/?p=` con el JSON del comprobante en base64. QuestPDF (ADR-018) dibuja imágenes pero no
genera códigos QR, y el repositorio no tenía ninguna librería de QR.

## Options considered

### Option 1: QRCoder 1.6.0, solo `PngByteQRCode`
- **Pros:** licencia MIT; código administrado; `PngByteQRCode` arma el PNG en memoria sin `System.Drawing`; el asset
  net6.0 (el que resuelve net10) no declara dependencias.
- **Cons:** una dependencia nueva en Infrastructure y en cuatro lockfiles; no es la última versión.

### Option 2: QRCoder 1.8.0
- **Pros:** versión más reciente.
- **Cons:** su asset net6.0 declara `System.Drawing.Common` 6.0.0, que entra al grafo aunque no se use y trae
  `ArtQRCode` atado a GDI+.

### Option 3: generador de QR propio
- **Pros:** sin dependencias.
- **Cons:** codificación Reed-Solomon, máscaras y versiones a mano; mucho código sin relación con el negocio y fácil de
  hacer mal.

### Option 4: ZXing.Net
- **Pros:** codifica y decodifica.
- **Cons:** para producir PNG necesita un binding de imágenes adicional; más paquetes que QRCoder.

## Decision
Se eligió QRCoder 1.6.0 usando solo `PngByteQRCode`, porque produce el PNG que QuestPDF dibuja sin arrastrar
`System.Drawing`. La URL y el JSON se arman en Application (`QrArca`) con `System.Text.Json`; Infrastructure solo
convierte ese texto en imagen.

## Consequences
- `Optica.Infrastructure.csproj` suma `QRCoder` 1.6.0; cambian los lockfiles de Infrastructure, Api, Api.Tests e
  Infrastructure.Tests (restauración en modo bloqueado).
- La Api no referencia QRCoder (`Api_NoReferenciaQrCoder`), igual que QuestPDF.
- No hay decodificador en las pruebas: el contenido se verifica sobre la URL y el JSON de `QrArca`; el escaneo con un
  teléfono quedó como verificación manual.
- `QrArca` repite en Application los códigos de ARCA que ya estaban en `Infrastructure/Arca/CodigosWsfe.cs` (tipo de
  comprobante, tipo de documento, moneda y cotización), porque el QR es un dato del comprobante impreso y no una
  llamada a ARCA; una prueba de Infrastructure.Tests compara ambas fuentes.
- La versión se revisa con `dotnet list package --vulnerable --include-transitive` en el SAST de cada ticket.
