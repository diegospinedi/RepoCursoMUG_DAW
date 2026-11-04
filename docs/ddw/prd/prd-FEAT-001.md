# Parent PRD: Acceso, configuración y marca

| Metric | Value |
|--------|-------|
| Ticket | FEAT-001 |
| Date | 2026-10-30 |
| Status | Split |
| Acceptance criteria to cover | 22 |

## Sub-tickets

| Sub-ticket | Title | PRD | ACs | Dependencies | Status |
|---|---|---|---|---|---|
| FEAT-001a | Ingreso, sesión y marca | prd-FEAT-001a.md | AC-01, AC-02, AC-03, AC-04, AC-07, AC-08, AC-15, AC-22 | none | done (merged into master, PR #2) |
| FEAT-001b | Bloqueo y gestión de la contraseña | prd-FEAT-001b.md | AC-05, AC-06, AC-14, AC-16, AC-17, AC-18, AC-19 | depends on a | done (merged into master, PR #3) |
| FEAT-001c | Configuración de parámetros de negocio | prd-FEAT-001c.md | AC-09, AC-10, AC-11, AC-12, AC-13, AC-20, AC-21 | depends on a | done (merged into master, PR #4) |

AC-01 a AC-21 vienen de `prd-DISC-001-01.md` (PRD 01 de DISC-001). AC-22 se agregó el 2026-10-30
en DEFINE, por decisión de las dueñas: el repositorio es público, lleva una marca de ejemplo y la
marca real de la óptica se instala solo en la PC del local.

Los AC de esta tabla son los números globales. Cada sub-PRD numera sus criterios desde AC-01 y
cita junto a cada uno su número global (por ejemplo, `AC-01 (FR-01, NFR-01; FEAT-001 AC-05)` en
FEAT-001b): el validador de DDW exige a la vez numeración sin saltos en cada PRD (F-PRD-05) y
números globales en las partes de una división (F-PRD-11). Decisión del 2026-10-30.

## Suggested implementation order
a → b → c (b y c son independientes entre sí; ambos dependen de a)

## Original context
`prd-DISC-001-01.md` cubre el acceso con contraseña, la configuración de los cuatro parámetros de
negocio y la marca de la óptica: 20 FR y 21 AC. Al validarlo como FEAT-001 superó el umbral de
alcance (W-PRD-06), y siendo además el primer código del proyecto, las dueñas decidieron dividirlo
en tres partes que se pueden poner en producción por separado, cada una con 7 u 8 criterios.
