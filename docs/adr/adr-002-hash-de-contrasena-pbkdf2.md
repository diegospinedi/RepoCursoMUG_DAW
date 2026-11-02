# ADR-002: Hash de la contraseña con PBKDF2 de la biblioteca estándar de .NET

| Field | Value |
|-------|-------|
| Date | 2026-10-30 |
| Ticket | FEAT-001a |
| Status | Accepted |

## Context
La contraseña de acceso se guarda solo como hash con sal (`docs/ddw/prd/prd-FEAT-001a.md`, FR-05;
PRD-001 RNF-14 admitía "PBKDF2, bcrypt o equivalente"). Es una sola credencial, en una PC del local,
y el repositorio es público, así que cada dependencia de terceros suma superficie de cadena de
suministro.

## Options considered

### Option 1: PBKDF2-HMAC-SHA256 con `Rfc2898DeriveBytes.Pbkdf2`
- **Pros:** incluido en .NET, sin paquetes nuevos; algoritmo aprobado por NIST; parámetros
  explícitos (600.000 iteraciones, sal de 16 bytes, recomendación de OWASP para SHA-256).
- **Cons:** no es resistente a memoria: un atacante con GPU lo calcula más rápido que Argon2id.

### Option 2: bcrypt o Argon2id con un paquete de terceros
- **Pros:** Argon2id resiste mejor el ataque con hardware dedicado.
- **Cons:** dependencia externa más para auditar y mantener; para una única contraseña local la
  ganancia frente a PBKDF2 con 600.000 iteraciones es menor que el costo de la dependencia.

## Decision
Se eligió PBKDF2-HMAC-SHA256 con 600.000 iteraciones y sal aleatoria de 16 bytes, con el formato
`PBKDF2-SHA256$<iteraciones>$<sal>$<hash>` y comparación en tiempo constante.

## Consequences
- Archivo: `src/Optica.Infrastructure/Seguridad/HasherContrasenaPbkdf2.cs`.
- El formato guarda las iteraciones: subirlas más adelante no invalida la contraseña existente.
- Cada verificación cuesta del orden de cientos de milisegundos de CPU; por eso `ingresar` y
  `definir` tienen limitador de concurrencia (threat model R-05).
