# Verification FEAT-004

Verificación de la configuración de oxlint en la web, sobre `3f74440` (rama `feat/FEAT-004-eslint-web`), con la corrida
de cierre de CODE (`docs/ddw/reports/tests-FEAT-004.md`): 2175/2175.

| Field | Value |
|---|---|
| Module | `web/.oxlintrc.json`, `web/package.json`, `web/lint.test.mjs`, `AGENTS.md` y 8 archivos de `web/src` |
| Line coverage | 98.92% |
| Branch coverage | 96.87% |
| Function coverage | 100% |
| Coverage floor | 80% (method default — .ddw/rules/testing.instructions.md) |
| Lint | `npm run lint` en `web/` (oxlint 1.92.0, `--max-warnings 0`) — clean, 0 findings; `npm run build` (tsc + vite) — clean |

La cobertura es la de la web, donde cambió el código de este ticket. `Presupuesto.tsx` queda en 93,18 % de líneas y
94,87 % de ramas por la rama de cambio de número sin prueba propia (D-07, aceptada por el usuario).

## Acceptance criteria
- ✅ AC-01 — `npm run lint` sobre el código actual termina sin problemas → `Lint_CodigoActual_SinProblemas` (`web/lint.test.mjs`)
- ✅ AC-02 — revisa aplicación y pruebas → `Lint_RevisaAplicacionYPruebas`
- ✅ AC-03 — variable sin uso rechazada con archivo, línea y regla → `Lint_VariableSinUso_Rechazado`
- ✅ AC-04 — hook dentro de una condición rechazado → `Lint_HookEnCondicion_Rechazado`
- ✅ AC-05 — las mismas 602 pruebas de la web pasan y la compilación es limpia (`npm run test:coverage`, `npm run build`)
- ✅ AC-06 — `dist/` y `coverage/` ignorados → `Lint_DistYCoverage_Ignorados`
- ✅ AC-07 — `AGENTS.md` declara el comando en "Stack" → `Lint_AgentsDeclaraElComando`

## Spec blocks
- ✅ Block 1 — configuración `.oxlintrc.json`, scripts `lint` y `test:lint`, prueba `lint.test.mjs` (8 pruebas) y línea en `AGENTS.md`; revisiones PASS (`a57277b`)
- ✅ Block 2 — los 7 problemas corregidos sin desactivar reglas (D-04, D-05); revisiones PASS (`0447b22`)

Pruebas de la spec, todas presentes y pasando:
- ✅ `Lint_CodigoActual_SinProblemas`
- ✅ `Lint_RevisaAplicacionYPruebas`
- ✅ `Lint_VariableSinUso_Rechazado`
- ✅ `Lint_HookEnCondicion_Rechazado`
- ✅ `Lint_ScriptConMaxWarnings0_Rechazado`
- ✅ `Lint_DistYCoverage_Ignorados`
- ✅ `Lint_TodoElCodigo_MenosDe60Segundos` (NFR-01)
- ✅ `Lint_AgentsDeclaraElComando`
- ✅ `npm run test:coverage` con las mismas 602 pruebas
- ✅ Las pruebas existentes de error rechazado y de navegación de `EditarArticulo.test.tsx` y `Presupuesto.test.tsx` pasan sin cambios
- ✅ `npm run build` sin errores

## Tests
- ✅ Sad-path tests: `Lint_VariableSinUso_Rechazado`, `Lint_HookEnCondicion_Rechazado` y `Lint_ScriptConMaxWarnings0_Rechazado` cubren el rechazo; en la web siguen pasando las pruebas de error (404, 401, respuesta ilegible) de las pantallas corregidas.
- ✅ Suite completa: .NET 1415, web 602, prueba del lint 8, e2e unidad 49, e2e 104 (Chrome y Edge).

## Warnings
- ⚠️ W-VER-02 — rama de cambio de número en `Presupuesto.tsx` (líneas 63-64) sin prueba propia; aceptada por el usuario (D-07).
- ⚠️ W-VER-03 — `Lint_TodoElCodigo_MenosDe60Segundos` mide tiempo y depende de la máquina; el margen medido es amplio (menos de 1 s).

Result: PASSED
