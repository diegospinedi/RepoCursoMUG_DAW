```
/ddw-validate-spec docs/ddw/specs/spec-FEAT-001a.md — PASSED
────────────────────────────────────────────────────────────────
  ✅ F-SPEC-01: all 8 FR from the PRD are referenced by a block
  ✅ F-SPEC-02: all 8 AC from the PRD are named by at least one test
  ✅ F-SPEC-03: all 4 NFR carry a technical strategy
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
  ⚠️ W-SPEC-02: large block, consider splitting: Block 1 (Esqueleto de la solución y persistencia) (13 files, 617 words), Block 2 (Dominio y casos de uso de la contraseña) (16 files, 644 words), Block 3 (Api de acceso, sesión y protección) (7 files, 683 words), Block 4 (Marca instalada y marca de ejemplo) (8 files, 589 words), Block 5 (Interfaz web: definir contraseña, ingresar, sesión y marca) (12 files, 485 words), Block 6 (Servir la SPA desde la Api y pruebas de punta a punta) (5 files, 526 words)
  👁  F-SPEC-12 (contradicts the PRD) and F-SPEC-13 (terminology diverging from
      the PRD) are MANUAL: judge them and say so explicitly in your report.
  ✅ F-SPEC-LOOP: 0 loop(s) since a human decided, under the ceiling of 3; 3 in total for this document
────────────────────────────────────────────────────────────────
Total: 13 passed, 0 failed, 1 warnings
Result: PASSED
```
