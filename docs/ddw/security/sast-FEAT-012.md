# SAST FEAT-012

| Field | Value |
|-------|-------|
| Ticket | FEAT-012 |
| Tools | Revisión estática del diff `cf9dc7f..HEAD` por ddw-sec-auditor (lectura del código contra §4 del catálogo, el modelo de amenazas R-01 a R-04 y las decisiones D-01 a D-22); `npm audit` y `npm audit --omit=dev` en `web/` y `e2e/`; `dotnet list Optica.sln package --vulnerable --include-transitive` |
| Date | 2026-11-17 |

## Alcance

El diff solo toca la web y las e2e: no cambia .NET ni ningún `package*.json`, `.csproj`, `playwright.config.ts`,
`ci.mjs`, `index.html` o `vite.config.ts`.
- **Menú de Inicio:** `MenuInicio.tsx`, `seccionesMenu.ts`, `IconoMenu.tsx`.
- **Estilo uniforme:** `estilos.css`, más clases en 10 pantallas.
- **`contraste.ts`.**
- **e2e:** `inicio-menu`, `estilo-uniforme` y `medidas.ts`; `backup-aviso` ajustado.

| Rule | Verdict | Notes |
|---|---|---|
| F-SAST-01 | ✅ | Sin claves, tokens ni certificados. Las contraseñas de `e2e/tests/estilo-uniforme.spec.ts:26` y `e2e/tests/inicio-menu.spec.ts:21` son de una base temporal del harness: falso positivo (H-02) |
| F-SAST-02 | ✅ | Sin cambios de backend ni de SQL |
| F-SAST-03 | ✅ | Sin procesos nuevos; `e2e/ci.mjs` y `e2e/servidor.mjs` sin cambios |
| F-SAST-04 | ✅ | Solo `response.json()` sobre la propia Api en la e2e (`e2e/tests/estilo-uniforme.spec.ts:131`) |
| F-SAST-05 | ✅ | Sin acceso a archivos nuevo; destinos fijos en `web/src/paginas/seccionesMenu.ts:22` |
| F-SAST-06 | ✅ | Todo por JSX desde una constante (`web/src/paginas/MenuInicio.tsx:15`); SVG estático en JSX (`web/src/componentes/IconoMenu.tsx:85`); sin `dangerouslySetInnerHTML` ni `innerHTML` de escritura |
| F-SAST-07 | ✅ | Sin hosts externos: la e2e usa `https://localhost:<puerto>` y rutas relativas; `e2e/ci.mjs` sin cambios |
| F-SAST-08 | ✅ | Sin criptografía; `contraste.ts` es aritmética de luminancia |
| F-SAST-09 | ✅ | Sin cambios de configuración ni de entorno |
| F-SAST-10 | ✅ | Sin `console.*` nuevos en la web; la e2e solo junta textos de violaciones de CSP (`e2e/tests/estilo-uniforme.spec.ts:89`) |
| F-SAST-11 | ✅ | No aplica: sin subida de archivos nueva |
| F-SAST-12 | ✅ | Sin operaciones nuevas que cambien estado; "Salir" sigue igual (`web/src/paginas/Inicio.tsx:135`) |
| F-SAST-13 | ✅ | Dependencias publicadas sin CVE: `npm audit --omit=dev` en `web/` da 0, `e2e/` da 0 y `dotnet list` no muestra paquetes vulnerables. En herramientas de desarrollo de `web/` (`web/package-lock.json:2220`, picomatch 4.0.7) sigue el mismo aviso Alto GHSA-fc5q-39xc-vjpf, aceptado por el usuario en D-28 de FEAT-011; el ticket no cambia paquetes (H-01) |
| F-SAST-14 | ✅ | Sin entradas nuevas; `contraste()` valida `#RRGGBB` y lanza `RangeError` sin repetir el valor (`web/src/marca/contraste.ts:5`) |
| F-SAST-15 | ✅ | El error de "Salir" conserva el mensaje genérico, solo cambia de lugar (`web/src/paginas/Inicio.tsx:137`) |
| F-SAST-16 | ✅ | Sin CVE Medium |
| F-SAST-17 | ✅ | Sin `eval`, `new Function` ni `setTimeout` con texto |

## CSP (R-02)

El diff no agrega ningún atributo `style=`, `<style>`, `data:`, `url(`, `@import` ni URL externa. Lo vigilan tres pruebas:
- `Inicio.test.tsx`;
- `MenuInicio.test.tsx`;
- la e2e, que falla ante cualquier violación de CSP.

## Rastros de la e2e (R-04)

- `playwright.config.ts` tiene trazas, capturas y video en `off`, y `e2e/test-results/` está en `.gitignore`.
- La base es temporal y ARCA es simulado.
- Los datos sembrados son ficticios (H-03): DNI que empiezan con `00`, correo en `example.com` y teléfono en ceros.
- El modelo de amenazas decía que las capturas quedan en `test-results/`. En realidad la configuración no genera
  capturas, un control más estricto que el documentado.

## Hallazgos

- **H-01:** dependencia de desarrollo ya aceptada (D-28 de FEAT-011), sin cambios en este ticket. No bloquea.
- **H-02:** falso positivo. Las contraseñas de la e2e son de una base temporal.
- **H-03:** falso positivo. El cliente sembrado por la e2e es ficticio.

No hay exposición de datos personales, de clientes, de la empresa, de empleados ni de proveedores.

## Suppressions
None.

Total: 17 clean, 0 vulnerabilities (0 critical, 0 high)
Result: PASSED
