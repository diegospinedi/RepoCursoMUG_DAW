# ADR-001: Sesión con cookie de autenticación del servidor en lugar de JWT

| Field | Value |
|-------|-------|
| Date | 2026-10-30 |
| Ticket | FEAT-001a |
| Status | Accepted |

## Context
FEAT-001a exigía una sesión que venciera tras 60 minutos sin actividad y que ninguna operación de
la API respondiera sin ella (`docs/ddw/prd/prd-FEAT-001a.md`, FR-01 y FR-04). La aplicación es un
monolito que sirve su propia SPA desde el mismo origen, en la PC o la red local del negocio, con
una sola contraseña compartida y sin usuarios.

## Options considered

### Option 1: cookie de autenticación de ASP.NET Core
- **Pros:** cookie `HttpOnly`, inaccesible desde JavaScript; `SameSite=Strict` cubre la
  falsificación de pedidos entre sitios; vencimiento deslizante y cierre de sesión resueltos por el
  framework; sin dependencias nuevas.
- **Cons:** atada al mismo origen; requiere persistir las claves de Data Protection para que las
  sesiones sobrevivan a un reinicio.

### Option 2: JWT guardado por la SPA
- **Pros:** sin estado en el servidor; sirve para clientes de otros orígenes.
- **Cons:** el token queda al alcance de JavaScript (riesgo de robo por XSS); cerrar sesión o vencer
  por inactividad exige lógica propia o tokens de refresco; ninguna de sus ventajas aplica a una
  app de un solo origen.

## Decision
Se eligió la cookie de autenticación de ASP.NET Core, renovada en cada pedido autenticado para que
el vencimiento cuente desde el último pedido, porque cumple los 60 minutos de inactividad con el
framework y no expone la sesión al navegador.

## Consequences
- Las claves de Data Protection se guardan en `Optica:Datos:Directorio/claves`, fuera del repo.
- La app escucha solo por HTTPS para que la cookie `Secure` viaje cifrada (spec, Block 3).
- Archivos: `src/Optica.Api/Program.cs`, `src/Optica.Api/Acceso/RenovacionSesion.cs`.
- Un cliente de otro origen (por ejemplo, una app móvil) necesitaría otro mecanismo; está fuera de
  alcance (PRD-001).
