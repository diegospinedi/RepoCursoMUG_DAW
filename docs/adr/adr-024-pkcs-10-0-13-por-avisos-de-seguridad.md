# ADR-024: System.Security.Cryptography.Pkcs 10.0.13 en lugar de 10.0.12

| Field | Value |
|-------|-------|
| Date | 2026-11-13 |
| Ticket | FEAT-007 |
| Status | Accepted |

## Context
ADR-023 eligió `System.Security.Cryptography.Pkcs` para firmar en CMS el pedido de acceso a WSAA y fijó la versión
10.0.12, la del resto de los paquetes .NET del proyecto. Al implementar el cliente (Block 6 de FEAT-007), la auditoría
de NuGet informó dos avisos sobre las versiones 10.0.0 a 10.0.12: GHSA-846f-f764-76mx (severidad alta) y
GHSA-2fmv-rv2c-59x7 (severidad moderada), ambos de denegación de servicio y corregidos en 10.0.13. Con 10.0.12 la
compilación daba NU1903 y NU1902, y el gate de SAST del proyecto exige cero paquetes vulnerables. ADR-023 no se edita:
su decisión de diseño sigue vigente y solo cambia la versión.

## Options considered

### Option 1: mantener 10.0.12, como dicen ADR-023 y la spec
- **Pros:** la misma versión que el resto de los paquetes .NET; sin desvío de los documentos aprobados.
- **Cons:** una dependencia con dos avisos conocidos en el componente que maneja el certificado de la empresa;
  advertencias en la compilación y SAST en rojo.

### Option 2: actualizar a 10.0.13, el servicing que corrige los avisos
- **Pros:** sin paquetes vulnerables; mismo paquete y misma API, solo un cambio de servicing.
- **Cons:** una versión distinta del resto de los paquetes 10.0.12 y un desvío de la versión escrita en ADR-023 y en la
  spec, que quedó registrado como D-56.

## Decision
Se eligió la opción 2 (D-56, decisión del usuario del 2026-11-13). El verificador de Block 6 reprodujo los dos avisos con
NuGetAudit en un proyecto aparte y comprobó que 10.0.13 no los tiene.

## Consequences
- `src/Optica.Infrastructure/Optica.Infrastructure.csproj` referencia 10.0.13, y los cuatro `packages.lock.json` que lo
  arrastran se restauran en modo bloqueado.
- El diseño de ADR-023 (SOAP propio con `HttpClient` y `System.Xml.Linq`, firma con `SignedCms`) no cambió.
- La spec de FEAT-007 sigue nombrando 10.0.12; D-56 y este ADR registran la diferencia para VERIFY.
- Las PCs de desarrollo e instalación quedaron con el runtime 10.0.12; su actualización es independiente de este paquete.
