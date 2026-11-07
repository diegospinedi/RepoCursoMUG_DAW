# Spec FEAT-004: Configurar un linter en la web (oxlint)

| Field | Value |
|-------|-------|
| Ticket | FEAT-004 |
| PRD | docs/ddw/prd/prd-FEAT-004.md |
| Tier | FEATURE |
| Date | 2026-11-07 |
| Spec loops | 1 |
| Loops since last human decision | 0 |

## Summary
oxlint (dependencia de desarrollo con versión exacta) analiza `web/src`, aplicaciones y pruebas, con la categoría
`correctness` como error (incluye `react/set-state-in-effect` y `react/immutability`), las reglas recomendadas de
TypeScript que oxlint ubica en otras categorías, activadas una por una, y las reglas de React de la plantilla de Vite:
`rules-of-hooks`, `exhaustive-deps` y `only-export-components` (D-01, D-02). `npm run lint` lo corre y termina con
código distinto de 0 ante cualquier problema. Un script `node:test` fuera de `src` (`npm run test:lint`, como
`test:unidad` de e2e) corre oxlint sobre una carpeta temporal y comprueba los rechazos (D-03). El Block 2 corrige los 7
problemas actuales sin cambiar el comportamiento: la suite de Vitest sigue en 602 pruebas y pasa. `AGENTS.md` declara
los dos comandos.

## Coverage: PRD → blocks
| Requirement | Covered by |
|---|---|
| FR-01 | Block 1 |
| FR-02 | Block 1 |
| FR-03 | Block 1 |
| FR-04 | Block 2 |
| FR-05 | Block 1 |
| FR-06 | Block 1 |
| NFR-01 | Strategy: oxlint analiza `web/src` en menos de 1 s en la PC de desarrollo (medido en PLAN: ~0,1 s); la prueba `Lint_TodoElCodigo_MenosDe60Segundos` lo fija con margen |
| NFR-02 | Strategy: `oxlint` en `devDependencies` con versión exacta (1.92.0) y `package-lock.json`; sin `@types/node` ni otra dependencia (la prueba es `.mjs` fuera de `src`, no pasa por `tsc`); `npm audit` sin vulnerabilidades; la salida de `npm run build` no lo incluye |

## Dependencies between blocks
Block 1 → Block 2. Block 2 necesita el comando y la configuración de Block 1 para comprobar que no quedan problemas.

## Block 1 — Configuración de oxlint, scripts y prueba del análisis

**Files**
- `web/package.json` y `web/package-lock.json` (modified) — `devDependencies.oxlint` = `1.92.0` exacto; scripts `"lint": "oxlint -c .oxlintrc.json --max-warnings 0 src"` y `"test:lint": "node --test lint.test.mjs"`.
- `web/.oxlintrc.json` (new) — plugins `typescript`, `react`, `unicorn`, `oxc`; `categories.correctness` = `error`; en `error`: `react/rules-of-hooks`, `react/exhaustive-deps`, `react/only-export-components` (`allowConstantExport: true`), `typescript/no-explicit-any`, `typescript/ban-ts-comment`, `typescript/no-empty-object-type`, `typescript/no-require-imports`, `typescript/no-unsafe-function-type`, `typescript/prefer-as-const`, `typescript/no-namespace` y `no-unused-expressions`; `env` `browser` y `es2022`; `ignorePatterns` `dist/**`, `coverage/**` y `node_modules/**`.
- `web/lint.test.mjs` (new) — pruebas `node:test` del análisis sobre una carpeta temporal (D-03).
- `AGENTS.md` (modified) — en "Stack", la línea `Lint: web: npm run lint · prueba del lint: npm run test:lint` (FR-06).

**Logic**
Implementa FR-01, FR-02, FR-03, FR-05 y FR-06. La prueba resuelve el ejecutable JavaScript del paquete `oxlint` con
`createRequire` y lo lanza con `execFile(process.execPath, [binario, ...args])`, sin intérprete de comandos, así anda
igual en Linux y en Windows (donde `node_modules/.bin/oxlint` es un `.cmd`). Para los rechazos arma una carpeta
temporal con `mkdtemp` que copia `.oxlintrc.json` y contiene `src/`, `dist/` y `coverage/`; corre oxlint con los mismos
argumentos que el script `lint`, con `cwd` en esa carpeta, y la borra al terminar.

**Input validation**
- Los archivos que recibe el análisis: solo los de `src` (el script nombra `src`), que contiene todo el código de
  la aplicación y todas sus pruebas de Vitest (FR-01, AC-02); `dist`, `coverage` y `node_modules` no se analizan.
  Los archivos de herramientas de la raíz de `web/` (`vite.config.ts`, `lint.test.mjs`) no son código de la web y
  quedan fuera (D-06).

**Error handling**
- Código con un problema (por ejemplo, una variable sin uso) — `npm run lint` termina con código 1 e informa archivo,
  línea y regla; ningún archivo se corrige solo.
- Un aviso también es un problema rechazado: el script lleva `--max-warnings 0`.

**Required tests**
- [ ] `Lint_CodigoActual_SinProblemas` — `npm run lint` sobre `web/src` termina con código 0; validates AC-01
- [ ] `Lint_RevisaAplicacionYPruebas` — un problema en `src/x.ts` y otro en `src/x.test.ts` de la carpeta temporal se informan los dos; validates AC-02
- [ ] `Lint_VariableSinUso_Rechazado` — una variable sin uso rechazada: código 1, archivo, línea y regla `no-unused-vars`; validates AC-03
- [ ] `Lint_HookEnCondicion_Rechazado` — un hook dentro de un `if` rechazado: código 1, archivo, línea y `rules-of-hooks`; validates AC-04
- [ ] `Lint_ScriptConMaxWarnings0_Rechazado` — el script `lint` de `package.json` lleva `--max-warnings 0`, así un aviso queda rechazado
- [ ] `Lint_DistYCoverage_Ignorados` — con `dist/` y `coverage/` con problemas en la carpeta temporal, los argumentos del script no los informan; validates AC-06
- [ ] `Lint_TodoElCodigo_MenosDe60Segundos` — el análisis de `web/src` termina en menos de 60 s; NFR-01
- [ ] `Lint_AgentsDeclaraElComando` — `AGENTS.md` contiene `npm run lint` en la sección "Stack"; validates AC-07

**Completion criterion**
`npm run lint` existe y analiza `web/src`; `npm run test:lint` pasa salvo `Lint_CodigoActual_SinProblemas`, que pasa con
el Block 2 (en este bloque se marca `todo` con los 7 problemas conocidos); `npm run build` y `npm audit` siguen limpios.

## Block 2 — Correcciones de los 7 problemas del código actual

**Files**
- `web/src/paginas/EditarArticulo.tsx` (modified) — `immutability` (línea 158) y `set-state-in-effect` (líneas 141 y 145).
- `web/src/paginas/Presupuesto.tsx` (modified) — `set-state-in-effect` (líneas 60 y 63).
- `web/src/paginas/NuevoArticulo.tsx` (modified) — `only-export-components` (línea 34): `mensajeGrabado` deja de exportarse (solo se usa en el archivo).
- `web/src/marca/MarcaProvider.tsx` (modified) y `web/src/marca/contextoMarca.ts` (new) — `ContextoMarca` y `useMarca` pasan a `contextoMarca.ts`; `web/src/componentes/Encabezado.test.tsx` (modified) importa `useMarca` desde ahí.
- `web/src/pruebas/servidorFalso.tsx` (modified) y `web/src/pruebas/sondas.tsx` (new) — los componentes `Ubicacion` y `Navegador` pasan a `sondas.tsx`, que exporta solo componentes; `Navegador` recibe una prop `registrar`; `navegarEnPrueba` sigue en `servidorFalso.tsx`, que pasa una función estable de módulo. Ningún archivo que importa `servidorFalso` cambia.

**Logic**
Implementa FR-04 sin cambiar comportamiento (D-04, D-05):
- `set-state-in-effect`: el estado de carga se ajusta durante el render cuando cambia el parámetro de la ruta
  (patrón de React "guardar información de renders anteriores": `numeroMostrado`/`codigoMostrado` en el estado), y el
  efecto queda solo con la consulta a la Api; si oxlint marcara ese ajuste, se usa un estado derivado `{parametro,
  carga}`. "Volver a abrir" sigue poniendo `cargando` desde su propio manejador.
- `immutability`: `mostrar` se declara antes del efecto con `useCallback(..., [])` (solo usa setters estables y
  `textosDe`, de módulo) y se suma a las dependencias del efecto, que no se ejecuta ninguna vez de más.
- No se desactiva ninguna regla ni se agregan comentarios de desactivación. Si una corrección exigiera cambiar
  comportamiento, se detiene y se consulta al usuario.

**Input validation**
- Las pantallas corregidas aceptan exactamente las mismas entradas que hoy: el número de ruta de `Presupuesto`, el
  código de `EditarArticulo` y los campos de `NuevoArticulo`, con las mismas validaciones de pantalla.

**Error handling**
- Una respuesta de error de la Api en `EditarArticulo` o `Presupuesto` (404, 401 o ilegible) se sigue mostrando con el
  mismo mensaje y la misma navegación; las pruebas existentes de esos casos de error rechazado no cambian.

**Required tests**
- [ ] `Lint_CodigoActual_SinProblemas` — pasa con los 7 problemas corregidos; validates AC-01
- [ ] `npm run test:coverage` pasa con las mismas 602 pruebas, sin cambiar ninguna aserción existente (solo la ruta del `import` de `useMarca` en `Encabezado.test.tsx`); validates AC-05
- [ ] Las pruebas existentes de error rechazado y de navegación de `EditarArticulo.test.tsx` (incluida Atrás entre `/articulos/8` y `/articulos/7`) y `Presupuesto.test.tsx` (404, 401, respuesta ilegible) pasan sin cambios
- [ ] `npm run build` compila sin errores; validates AC-05

**Completion criterion**
`npm run lint` termina con código 0, `npm run test:lint`, `npm run test:coverage` (602) y `npm run build` pasan, y ninguna
prueba existente cambió sus aserciones.

## Final verification
`npm run lint`, `npm run test:lint`, `npm run test:coverage` y `npm run build` en `web/` pasan; `npm audit` sin vulnerabilidades; la suite e2e
sigue en verde; `AGENTS.md` declara el comando de lint.
