# SAST FEAT-005

| Field | Value |
|-------|-------|
| Ticket | FEAT-005 |
| Tools | revisión manual del diff `master..508fccb` (grep de secretos, ejecución y patrones), npm audit (web y e2e), `dotnet list package --vulnerable --include-transitive`, CodeQL de GitHub sobre el PR #11 |
| Date | 2026-11-07 |

Alcance: `.github/workflows/ci.yml`, `e2e/ci.mjs`, `e2e/ci.test.mjs`, `e2e/playwright.config.ts`, `e2e/package.json`,
`e2e/README.md`, `Directory.Build.props` y los 8 `packages.lock.json`. Sin cambios en `src/` ni en `web/src`.

| Rule | Verdict | Notes |
|---|---|---|
| F-SAST-01 | ✅ | sin secretos en el diff; `ci.yml` no referencia `secrets` ni el token (regla `secretos`); las apariciones de `token` en `e2e/ci.test.mjs` son textos de prueba de esa regla; los lockfiles solo tienen nombres, versiones y hashes |
| F-SAST-02 | ✅ | no hay consultas a bases de datos en el código del ticket |
| F-SAST-03 | ✅ | `e2e/ci.test.mjs:61` usa `execFileSync('git', [...])` con argumentos fijos y sin shell; `ci.yml` no interpola expresiones `${{ }}` en `run` (solo `github.event.pull_request.number` en `concurrency.group`) |
| F-SAST-04 | ✅ | sin `eval` ni `new Function`; `ci.mjs` lee texto y no lo evalúa; la importación de `playwright.config.ts` en la prueba carga un archivo del propio repositorio |
| F-SAST-05 | ✅ | las rutas leídas salen de `git ls-files` y de rutas fijas del repositorio, sin entrada de usuario |
| F-SAST-06 | ✅ | no genera HTML; la alerta de CodeQL js/incomplete-multi-character-sanitization (`e2e/ci.mjs:40`, PR #11) se corrigió en cdf5fc5 con `quitarComentarios`, que recorre el texto hasta que no queda `<!--`; la alerta se cierra cuando CodeQL analice el push |
| F-SAST-07 | ✅ | sin pedidos de red; la regla de resolución impide que los navegadores de la suite resuelvan nombres externos (FR-10) |
| F-SAST-08 | ✅ | sin criptografía en el ticket; el certificado de desarrollo HTTPS se genera por corrida y se descarta |
| F-SAST-09 | ✅ | sin cambios en la configuración de producción; la CI compila en Debug para pruebas, sin publicar binarios |
| F-SAST-10 | ✅ | la CI no imprime secretos ni datos reales; el job usa `HOME: /tmp/perfil-ci` para que las pruebas que verifican que los logs no lleven la carpeta del usuario sigan activas; la posible fuga de "Content root path" en producción es previa al ticket y quedó como riesgo aceptado R-09 (FIX aparte) |
| F-SAST-11 | ✅ | sin superficie de subida de archivos |
| F-SAST-12 | ✅ | sin formularios ni endpoints nuevos |
| F-SAST-13 | ✅ | npm audit: 0 vulnerabilidades en `web/` y en `e2e/`; `dotnet list package --vulnerable --include-transitive`: ningún proyecto con paquetes vulnerables |
| F-SAST-14 | ✅ | las funciones de `ci.mjs` validan su entrada (texto vacío → `vacio`, arreglos vacíos) según la spec |
| F-SAST-15 | ✅ | los problemas informan regla, archivo y host; sin stack traces ni rutas locales en la salida |
| F-SAST-16 | ✅ | dependencias nuevas de la corrida: `actions/checkout` v7.0.1, `actions/setup-dotnet` v6.1.0 y `actions/setup-node` v7.1.0 fijadas por SHA verificado con `git ls-remote`; sin paquetes npm ni NuGet nuevos |
| F-SAST-17 | ✅ | sin rutas de código dinámicas |

## Suppressions
None.

Total: 17 clean, 0 vulnerabilities (0 critical, 0 high)
Result: PASSED
