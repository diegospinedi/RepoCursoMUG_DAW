```
/ddw-validate-spec docs/ddw/specs/spec-FEAT-006.md — PASSED
────────────────────────────────────────────────────────────────
  ✅ F-SPEC-01: all 16 FR from the PRD are referenced by a block
  ✅ F-SPEC-02: all 17 AC from the PRD are named by at least one test
  ✅ F-SPEC-03: all 3 NFR carry a technical strategy
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
  ⚠️ W-SPEC-02: large block, consider splitting: Block 1 (Filtro y consulta de la búsqueda) (10 files, 749 words), Block 2 (Caso de uso y endpoint de la búsqueda) (10 files, 696 words), Block 3 (Pantalla de búsqueda) (10 files, 458 words), Block 4 (Generador del PDF) (7 files, 640 words), Block 5 (Caso de uso y endpoint del PDF) (10 files, 447 words), Block 6 (Botón Descargar PDF y e2e) (8 files, 615 words)
  👁  F-SPEC-12 (contradicts the PRD) and F-SPEC-13 (terminology diverging from
      the PRD) are MANUAL: judge them and say so explicitly in your report.
  ✅ F-SPEC-LOOP: 1 loop(s) since a human decided, under the ceiling of 3; 3 in total for this document
────────────────────────────────────────────────────────────────
Total: 13 passed, 0 failed, 1 warnings
Result: PASSED
```
