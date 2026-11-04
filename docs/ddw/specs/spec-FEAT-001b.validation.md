```
/ddw-validate-spec docs/ddw/specs/spec-FEAT-001b.md — PASSED
────────────────────────────────────────────────────────────────
  ✅ F-SPEC-01: all 6 FR from the PRD are referenced by a block
  ✅ F-SPEC-02: all 7 AC from the PRD are named by at least one test
  ✅ F-SPEC-03: all 2 NFR carry a technical strategy
  ·  7 block(s) found
  ✅ F-SPEC-04: every block lists the files it creates or modifies
  ✅ F-SPEC-05: every block has a verifiable completion criterion
  ✅ F-SPEC-06: every block lists at least one required test
  ✅ F-SPEC-07: every endpoint carries a complete contract
  ✅ F-SPEC-08: every schema declares its constraints
  ✅ F-SPEC-09: every block taking input documents its validation
  ✅ F-SPEC-10: every block documents its error handling
  ✅ F-SPEC-16: every documented error is named by a test
  ✅ F-SPEC-11: dependencies between blocks are declared
  ⚠️ W-SPEC-02: large block, consider splitting: Block 1 (Política de bloqueo y consulta de rechazos) (10 files, 649 words), Block 2 (Ingreso con bloqueo en la Api) (8 files, 495 words), Block 3 (Contratos y persistencia del cambio de contraseña) (10 files, 349 words), Block 4 (Cambio de contraseña: caso de uso y Api) (8 files, 600 words), Block 5 (Restablecimiento por comando en la PC) (15 files, 1079 words), Block 6 (Interfaz web: mensaje de bloqueo y cambio de contraseña) (10 files, 520 words), Block 7 (Sin datos de ARCA en pantalla y pruebas de punta a punta) (3 files, 620 words)
  👁  F-SPEC-12 (contradicts the PRD) and F-SPEC-13 (terminology diverging from
      the PRD) are MANUAL: judge them and say so explicitly in your report.
  ✅ F-SPEC-LOOP: 2 loop(s) since a human decided, under the ceiling of 3; 2 in total for this document
────────────────────────────────────────────────────────────────
Total: 13 passed, 0 failed, 1 warnings
Result: PASSED
```
