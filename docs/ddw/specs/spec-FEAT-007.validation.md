```
/ddw-validate-spec docs/ddw/specs/spec-FEAT-007.md — PASSED
────────────────────────────────────────────────────────────────
  ✅ F-SPEC-01: all 29 FR from the PRD are referenced by a block
  ✅ F-SPEC-02: all 32 AC from the PRD are named by at least one test
  ✅ F-SPEC-03: all 4 NFR carry a technical strategy
  ·  8 block(s) found
  ✅ F-SPEC-04: every block lists the files it creates or modifies
  ✅ F-SPEC-05: every block has a verifiable completion criterion
  ✅ F-SPEC-06: every block lists at least one required test
  ✅ F-SPEC-07: every endpoint carries a complete contract
  ✅ F-SPEC-08: every schema declares its constraints
  ✅ F-SPEC-09: every block taking input documents its validation
  ✅ F-SPEC-10: every block documents its error handling
  ✅ F-SPEC-16: every documented error is named by a test
  ✅ F-SPEC-11: dependencies between blocks are declared
  ⚠️ W-SPEC-02: large block, consider splitting: Block 1 (Configuración: alícuotas de ARCA y punto de venta) (14 files, 864 words), Block 3 (Dominio de facturación) (13 files, 670 words), Block 4 (Persistencia de facturas y emisiones pendientes) (6 files, 602 words), Block 5 (Puerto de ARCA, opciones y simulador) (8 files, 416 words), Block 6 (Cliente de WSAA y WSFEv1) (10 files, 709 words), Block 7 (Caso de uso y endpoint de emisión) (13 files, 983 words), Block 8 (Pantalla de facturación y e2e) (8 files, 736 words)
  👁  F-SPEC-12 (contradicts the PRD) and F-SPEC-13 (terminology diverging from
      the PRD) are MANUAL: judge them and say so explicitly in your report.
  ✅ F-SPEC-LOOP: 0 loop(s) since a human decided, under the ceiling of 3; 3 in total for this document
────────────────────────────────────────────────────────────────
Total: 13 passed, 0 failed, 1 warnings
Result: PASSED
```
