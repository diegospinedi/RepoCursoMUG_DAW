# SAST FEAT-004

| Field | Value |
|-------|-------|
| Ticket | FEAT-004 |
| Alcance | `git diff master...0447b22` en `web/` y `AGENTS.md`: `package.json`, `package-lock.json`, `.oxlintrc.json`, `lint.test.mjs` y 8 archivos de `web/src` |
| Tools | revisión manual del diff con búsquedas de patrones (grep), `npm audit` (web), `npm run lint` (oxlint 1.92.0) |
| Date | 2026-11-07 |

| Rule | Verdict | Notes |
|---|---|---|
| F-SAST-01 | ✅ | sin secretos embebidos; `.env` y `.env.*` en `.gitignore` |
| F-SAST-02 | ✅ | sin cambios en consultas: el ticket no toca la Api ni la base |
| F-SAST-03 | ✅ | `web/lint.test.mjs` lanza oxlint con `execFile(process.execPath, [binario, ...argumentos])`, `shell: false`, argumentos fijos tomados del script y `timeout` de 60 s; ninguna entrada externa llega al comando |
| F-SAST-04 | ✅ | sin `eval`, `new Function` ni deserialización insegura |
| F-SAST-05 | ✅ | la prueba escribe solo en una carpeta propia de `mkdtemp` con rutas literales fijas y la borra al terminar |
| F-SAST-06 | ✅ | sin `dangerouslySetInnerHTML` ni `innerHTML`; React sigue escapando los datos del cliente en `Presupuesto.tsx` |
| F-SAST-07 | ✅ | sin pedidos salientes nuevos; oxlint no usa la red |
| F-SAST-08 | ✅ | sin criptografía nueva |
| F-SAST-09 | ✅ | oxlint es solo de desarrollo y no llega a `web/dist`; sin modo debug |
| F-SAST-10 | ✅ | sin `console`, `localStorage` ni `sessionStorage` en el código de producción de `web/src`; los datos del cliente siguen solo en el estado del componente |
| F-SAST-11 | ✅ | no hay superficie de carga de archivos |
| F-SAST-12 | ✅ | sin cambios en sesión ni en pedidos que modifican estado |
| F-SAST-13 | ✅ | `npm audit`: 0 vulnerabilidades |
| F-SAST-14 | ✅ | las pantallas corregidas aceptan las mismas entradas con las mismas validaciones |
| F-SAST-15 | ✅ | los errores de la Api se muestran con los mismos mensajes fijos |
| F-SAST-16 | ✅ | dependencia nueva `oxlint` 1.92.0 (MIT, `oxc-project/oxc`) con sus `@oxlint/binding-*`: versión exacta, integridad `sha512` en el lockfile, sin scripts de instalación, solo de desarrollo |
| F-SAST-17 | ✅ | sin rutas de código dinámico |

## Suppressions
None.

Total: 17 clean, 0 vulnerabilities (0 critical, 0 high)
Result: PASSED
