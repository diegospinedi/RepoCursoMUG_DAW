```
/ddw-validate-spec docs/ddw/specs/spec-FEAT-003c.md — PASSED
────────────────────────────────────────────────────────────────
  ✅ F-SPEC-01: all 7 FR from the PRD are referenced by a block
  ✅ F-SPEC-02: all 10 AC from the PRD are named by at least one test
  ✅ F-SPEC-03: all 1 NFR carry a technical strategy
  ·  5 block(s) found
  ✅ F-SPEC-04: every block lists the files it creates or modifies
  ✅ F-SPEC-05: every block has a verifiable completion criterion
  ✅ F-SPEC-06: every block lists at least one required test
  ✅ F-SPEC-07: every endpoint carries a complete contract
  ✅ F-SPEC-08: every schema declares its constraints
  ✅ F-SPEC-09: every block taking input documents its validation
  ✅ F-SPEC-10: every block documents its error handling
  ✅ F-SPEC-16: every documented error is named by a test
  ✅ F-SPEC-11: dependencies between blocks are declared
  ⚠️ W-SPEC-02: large block, consider splitting: Block 1 (Dominio: versión, modificación y paso a Final) (8 files, 454 words), Block 2 (Persistencia: versión, triggers de cierre y actualización) (12 files, 1177 words), Block 3 (Aplicación y Api: modificar y pasar a Final) (16 files, 1049 words), Block 4 (Web: pantalla de modificación y paso a Final) (9 files, 620 words)
  👁  F-SPEC-12 (contradicts the PRD) and F-SPEC-13 (terminology diverging from
      the PRD) are MANUAL: judge them and say so explicitly in your report.
  ✅ F-SPEC-LOOP: 2 loop(s) since a human decided, under the ceiling of 3; 2 in total for this document
────────────────────────────────────────────────────────────────
Total: 13 passed, 0 failed, 1 warnings
Result: PASSED
```
