```
/ddw-verify-module docs/ddw/reports/verify-FEAT-013.md — FAILED (2 FAILS)
────────────────────────────────────────────────────────────────
  ❌ F-VER-01: AC reported as failing: AC-09, AC-10, AC-14
  ✅ F-VER-02: all 5 spec block(s) are accounted for
  ❌ F-VER-06: test promised by the spec and absent from the report: cliente_BusquedaCancelada_DevuelveCanceladoNoError, useBusquedaEnVivo_EnviarAhoraCancelaLaPausa, ListaSugerencias_AriaActivedescendantYAnuncioSoloCantidad, ListaSugerencias_HtmlEnSugerenciaSeVeComoTexto, CampoConOpciones_FiltraPlegadoYFlechaAbreLista (and others)
  ✅ F-VER-03: line, branch and function coverage all stated and ≥ 80%
  ✅ F-VER-04: the report names sad-path testing
  ✅ F-VER-05: lint / type check reported and clean
  👁  DDW does not run your suite: the numbers above are the report's, not this
      script's. What is checked is that the verdict is COMPLETE — see the header.
────────────────────────────────────────────────────────────────
Total: 4 passed, 2 failed, 0 warnings
Result: FAILED (2 FAILS)
```
