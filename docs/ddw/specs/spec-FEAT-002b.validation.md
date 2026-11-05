```
/ddw-validate-spec docs/ddw/specs/spec-FEAT-002b.md — PASSED
────────────────────────────────────────────────────────────────
  ✅ F-SPEC-01: all 7 FR from the PRD are referenced by a block
  ✅ F-SPEC-02: all 6 AC from the PRD are named by at least one test
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
  ⚠️ W-SPEC-02: large block, consider splitting: Block 1 (Dominio: estado, versión, modificación y plegado de búsqueda) (8 files, 662 words), Block 2 (Infraestructura: columnas `Activo` y `Version`, función `plegar` y repositorio) (11 files, 785 words), Block 3 (Aplicación: búsqueda, modificación y cambio de estado) (8 files, 689 words), Block 4 (Api: listado, modificación y cambio de estado) (8 files, 717 words), Block 5 (Web: listado y edición de artículos) (7 files, 565 words)
  👁  F-SPEC-12 (contradicts the PRD) and F-SPEC-13 (terminology diverging from
      the PRD) are MANUAL: judge them and say so explicitly in your report.
  ✅ F-SPEC-LOOP: 1 loop(s) since a human decided, under the ceiling of 3; 3 in total for this document
────────────────────────────────────────────────────────────────
Total: 13 passed, 0 failed, 1 warnings
Result: PASSED
```
