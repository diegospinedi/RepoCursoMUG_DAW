```
/ddw-validate-spec docs/ddw/specs/spec-FEAT-003b.md — PASSED
────────────────────────────────────────────────────────────────
  ✅ F-SPEC-01: all 8 FR from the PRD are referenced by a block
  ✅ F-SPEC-02: all 9 AC from the PRD are named by at least one test
  ✅ F-SPEC-03: all 2 NFR carry a technical strategy
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
  ⚠️ W-SPEC-02: large block, consider splitting: Block 1 (Dominio: cálculo de la línea, política de cantidad y descuento, total) (9 files, 653 words), Block 2 (Aplicación y Api: cantidad y descuento en el alta, errores por campo y total) (10 files, 881 words), Block 3 (Web: reglas de cantidad y descuento, cálculo y cliente de la Api) (7 files, 538 words)
  👁  F-SPEC-12 (contradicts the PRD) and F-SPEC-13 (terminology diverging from
      the PRD) are MANUAL: judge them and say so explicitly in your report.
  ✅ F-SPEC-LOOP: 2 loop(s) since a human decided, under the ceiling of 3; 4 in total for this document
────────────────────────────────────────────────────────────────
Total: 13 passed, 0 failed, 1 warnings
Result: PASSED
```
