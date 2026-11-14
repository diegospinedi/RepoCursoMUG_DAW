# ADR-022: ARCA detrás de un puerto, con el simulador como entorno por defecto

| Field | Value |
|-------|-------|
| Date | 2026-11-13 |
| Ticket | FEAT-007 |
| Status | Accepted |

## Context
`AGENTS.md` pide aislar la integración con ARCA en su propio módulo por los cambios normativos (RF-27). El usuario no
tiene todavía el certificado de homologación (D-02 de FEAT-007), las pruebas no pueden depender de la red, y nada de
desarrollo ni de prueba puede apuntar a producción (`AGENTS.md`).

## Options considered

### Option 1: cliente de WSFEv1 llamado directamente por el caso de uso, con dobles solo en las pruebas
- **Pros:** menos tipos.
- **Cons:** sin certificado la aplicación instalada no puede emitir nada; el e2e necesitaría red o código de prueba
  dentro de la Api.

### Option 2: puerto `IAutorizadorComprobantes` en Application con un simulador y el cliente real en Infrastructure
- **Pros:** el caso de uso no conoce SOAP ni certificados; el simulador permite usar y probar la aplicación sin
  certificado; el entorno se elige por configuración en la raíz de composición.
- **Cons:** una instalación puede quedar facturando en simulado sin notarlo (R-10 del modelo de amenazas).

## Decision
Se eligió la opción 2 (D-11). `Arca:Entorno` vale `Simulado` (también sin la clave), `Homologacion` o `Produccion`; el
simulado es el valor por defecto para que una configuración faltante nunca apunte a producción (R-14).

## Consequences
- `src/Optica.Infrastructure/Arca/` contiene el simulador, el cliente de WSAA y WSFEv1 y sus opciones.
- La factura guarda el entorno en que se emitió (FR-18), que el PRD 08 usa para la leyenda de comprobante simulado.
- La Api registra una advertencia al arrancar en simulado; `docs/instalacion.md` explica el cambio a homologación.
