```
/ddw-validate-spec docs/ddw/specs/spec-FEAT-003a.md — PASSED
────────────────────────────────────────────────────────────────
  ✅ F-SPEC-01: all 15 FR from the PRD are referenced by a block
  ✅ F-SPEC-02: all 19 AC from the PRD are named by at least one test
  ✅ F-SPEC-03: all 2 NFR carry a technical strategy
  ·  6 block(s) found
  ✅ F-SPEC-04: every block lists the files it creates or modifies
  ✅ F-SPEC-05: every block has a verifiable completion criterion
  ✅ F-SPEC-06: every block lists at least one required test
  ✅ F-SPEC-07: every endpoint carries a complete contract
  ✅ F-SPEC-08: every schema declares its constraints
  ✅ F-SPEC-09: every block taking input documents its validation
  ✅ F-SPEC-10: every block documents its error handling
  ✅ F-SPEC-16: every documented error is named by a test
  ✅ F-SPEC-11: dependencies between blocks are declared
  ⚠️ W-SPEC-02: large block, consider splitting: Block 1 (Dominio: cliente, línea, presupuesto y numeración) (6 files, 992 words), Block 2 (Infraestructura: tablas, migración `Presupuestos`, repositorio y filtro de activos) (6 files, 858 words), Block 3 (Aplicación: alta, consulta y búsqueda de artículos activos) (5 files, 784 words), Block 4 (Api: alta y consulta de presupuestos y `soloActivos`) (8 files, 889 words), Block 5 (Web: alta del presupuesto y presupuesto grabado) (5 files, 766 words), Block 6 (E2E: AC-01 a AC-19 en Chrome y Edge) (3 files, 694 words)
  👁  F-SPEC-12 (contradicts the PRD) and F-SPEC-13 (terminology diverging from
      the PRD) are MANUAL: judge them and say so explicitly in your report.
  ✅ F-SPEC-LOOP: 0 loop(s) since a human decided, under the ceiling of 3; 1 in total for this document
────────────────────────────────────────────────────────────────
Total: 13 passed, 0 failed, 1 warnings
Result: PASSED
```
