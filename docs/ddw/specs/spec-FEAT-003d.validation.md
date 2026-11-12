```
/ddw-validate-spec docs/ddw/specs/spec-FEAT-003d.md — PASSED
────────────────────────────────────────────────────────────────
  ✅ F-SPEC-01: all 1 FR from the PRD are referenced by a block
  ✅ F-SPEC-02: all 3 AC from the PRD are named by at least one test
  ✅ F-SPEC-03: all 3 NFR carry a technical strategy
  ·  3 block(s) found
  ✅ F-SPEC-04: every block lists the files it creates or modifies
  ✅ F-SPEC-05: every block has a verifiable completion criterion
  ✅ F-SPEC-06: every block lists at least one required test
  ✅ F-SPEC-07: every endpoint carries a complete contract
  ✅ F-SPEC-08: every schema declares its constraints
  ✅ F-SPEC-09: every block taking input documents its validation
  ✅ F-SPEC-10: every block documents its error handling
  ✅ F-SPEC-16: every documented error is named by a test
  ✅ F-SPEC-11: dependencies between blocks are declared
  ⚠️ W-SPEC-02: large block, consider splitting: Block 1 (Tiempo de espera explícito y dos altas simultáneas en integración) (5 files, 944 words), Block 2 (Dos sesiones por la Api y volumen de 10.000 presupuestos) (6 files, 888 words), Block 3 (Dos sesiones en el navegador, Chrome y Edge estables) (2 files, 523 words)
  👁  F-SPEC-12 (contradicts the PRD) and F-SPEC-13 (terminology diverging from
      the PRD) are MANUAL: judge them and say so explicitly in your report.
  ✅ F-SPEC-LOOP: 2 loop(s) since a human decided, under the ceiling of 3; 2 in total for this document
────────────────────────────────────────────────────────────────
Total: 13 passed, 0 failed, 1 warnings
Result: PASSED
```
