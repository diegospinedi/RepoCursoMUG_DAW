# ADR-023: Cliente SOAP propio para WSAA y WSFEv1, y firma CMS con System.Security.Cryptography.Pkcs

| Field | Value |
|-------|-------|
| Date | 2026-11-13 |
| Ticket | FEAT-007 |
| Status | Accepted |

## Context
FEAT-007 usa tres operaciones de ARCA: `loginCms` de WSAA, y `FECompUltimoAutorizado` y `FECAESolicitar` de WSFEv1.
WSAA exige un pedido de acceso firmado en CMS (PKCS#7) con el certificado de la empresa. El proyecto no tenía cliente
HTTP ni dependencias de SOAP, y fija sus paquetes con archivos de bloqueo (FEAT-005).

## Options considered

### Option 1: proxies WCF generados (`dotnet-svcutil`, `System.ServiceModel.*`)
- **Pros:** tipos generados desde el WSDL.
- **Cons:** cuatro paquetes y código generado para tres operaciones; el WSDL de ARCA cambia con la normativa y habría que
  regenerar.

### Option 2: SOAP armado con `HttpClient` y `System.Xml.Linq`, y `SignedCms` para la firma
- **Pros:** una sola dependencia nueva (`System.Security.Cryptography.Pkcs`, de Microsoft); el XML enviado se prueba
  byte a byte contra respuestas grabadas; control total del tiempo máximo y de lo que llega al log.
- **Cons:** los mensajes SOAP se escriben a mano y un campo nuevo de ARCA se agrega a mano.

### Option 3: biblioteca de terceros para ARCA
- **Pros:** menos código propio.
- **Cons:** una dependencia de mantenimiento incierto que maneja el certificado y la clave privada de la empresa.

## Decision
Se eligió la opción 2 (D-12): es la de menor superficie de dependencias para un componente que maneja credenciales
fiscales.

## Consequences
- `System.Security.Cryptography.Pkcs` 10.0.12 en `Optica.Infrastructure` y en los archivos de bloqueo que lo arrastran.
- `ClienteWsaa` y `ClienteWsfe` en `src/Optica.Infrastructure/Arca/`, probados con XML grabado y un certificado
  autofirmado generado en la prueba.
- La lectura de respuestas prohíbe DTD y se limita a 1 MB (R-12).
