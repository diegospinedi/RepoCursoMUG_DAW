# SAST FEAT-013

| Field | Value |
|-------|-------|
| Ticket | FEAT-013 |
| Tools | Revisión estática del diff `master..HEAD` de la rama (lectura del código contra §4 del catálogo, el modelo de amenazas R-01 a R-05 y las decisiones D-01 a D-25); búsqueda de patrones (`innerHTML`, `dangerouslySetInnerHTML`, `eval`, `new Function`, `console.`, almacenamiento, hosts externos, secretos); `npm audit` y `npm audit --omit=dev` en `web/` y `e2e/`; `dotnet list Optica.sln package --vulnerable --include-transitive` |
| Date | 2026-11-18 |

## Alcance

La rama se abrió sobre FEAT-012 (PR #22), así que `master..HEAD` incluye también sus commits. Esos commits ya pasaron su
propio SAST (`sast-FEAT-012.md`).

Lo propio de FEAT-013 es:
- **Base de la búsqueda en vivo:** `useBusquedaEnVivo.ts`, `ListaSugerencias.tsx`, `CampoBusqueda.tsx`,
  `CampoConOpciones.tsx` y `plegar`. En `api/cliente.ts`, la opción de cancelación con `AbortSignal`.
- **Pantallas:** Artículos, el buscador del presupuesto, Buscar presupuestos, Buscar facturas y Configuración.
- **Pruebas y helpers de prueba:** `web/src/pruebas/`, la e2e `busqueda-en-vivo` y los specs actualizados.
- **Pruebas .NET:** `BusquedaCanceladaTests.cs` y `VolumenCatalogoTests.cs`.

El ticket no toca código .NET de producción, ningún `package*.json` o `.csproj`, ni `vite.config.ts`, `index.html`,
`playwright.config.ts`, `ci.mjs` o `servidor.mjs`.

| Rule | Verdict | Notes |
|---|---|---|
| F-SAST-01 | ✅ | Sin claves, tokens ni certificados. La contraseña de `e2e/tests/busqueda-en-vivo.spec.ts:27` es la de la base temporal del harness, igual que en los specs anteriores: falso positivo (H-02). `.env` y `.env.*` están en `.gitignore` |
| F-SAST-02 | ✅ | Sin cambios de backend ni de SQL; los filtros siguen viajando en el cuerpo del POST hacia los endpoints existentes (ADR-019) |
| F-SAST-03 | ✅ | Sin procesos nuevos; `e2e/ci.mjs`, `e2e/compilacion.mjs` y `e2e/servidor.mjs` sin cambios |
| F-SAST-04 | ✅ | Sin deserialización nueva: las respuestas de la propia Api pasan por el cliente existente; la clave del hook la serializa la misma pantalla y se lee con un discriminante tipado (`web/src/paginas/BuscarPresupuestos.tsx:104`) |
| F-SAST-05 | ✅ | Sin acceso a archivos; la navegación usa números de la respuesta (`/presupuestos/{numero}`, `/facturas/{presupuesto}`) |
| F-SAST-06 | ✅ | Las sugerencias se dibujan como texto por JSX (`web/src/componentes/ListaSugerencias.tsx:173`), sin `dangerouslySetInnerHTML` ni escritura de `innerHTML` en `web/src`; los ids de las opciones se codifican. Lo prueban `ListaSugerencias.test.tsx` (HTML como texto) y la e2e de CSP con una sugerencia que contiene `<img onerror>` (R-04) |
| F-SAST-07 | ✅ | Sin hosts externos: los pedidos van a rutas relativas de la propia Api; la e2e usa `https://localhost:<puerto>` y bloquea cualquier otro host |
| F-SAST-08 | ✅ | Sin criptografía |
| F-SAST-09 | ✅ | Sin cambios de configuración ni de entorno; la e2e fuerza `Arca__Entorno=Simulado` (`e2e/compilacion.mjs:546`, sin cambios) |
| F-SAST-10 | ✅ | Sin `console.*` en el código de producción de `web/src`; los `catch` del hook descartan en silencio; el DNI no aparece en sugerencias, ids, anuncio, URL ni almacenamiento (R-02, `BuscarPresupuestos_SugerenciaSinDni`, `BuscarFacturas_SugerenciaSinDni`) |
| F-SAST-11 | ✅ | No aplica: sin subida de archivos nueva |
| F-SAST-12 | ✅ | Sin operaciones nuevas que cambien estado: las búsquedas en vivo usan los POST de búsqueda existentes, con la misma protección; grabar Configuración sigue por el mismo PUT (`web/src/paginas/Configuracion.tsx`) |
| F-SAST-13 | ✅ | Dependencias publicadas sin CVE: `npm audit --omit=dev` da 0 en `web/` y 0 en `e2e/`; `dotnet list` no muestra paquetes vulnerables. En herramientas de desarrollo de `web/` sigue el aviso Alto GHSA-fc5q-39xc-vjpf (picomatch, vía vite/vitest), aceptado por el usuario en D-28 de FEAT-011; el ticket no cambia paquetes (H-01) |
| F-SAST-14 | ✅ | En vivo no se envía una entrada que la Api rechazaría: UTF-16 válido, largos máximos iguales a los del backend (100/100/20 y 100) y rango de fechas válido (D-07, D-23); Configuración solo acepta opciones de la lista (D-13, FR-14); la Api sigue validando todo |
| F-SAST-15 | ✅ | Un 400 en vivo solo oculta las sugerencias; las fallas muestran los mensajes fijos de hoy, nunca el texto del servidor; un pedido cancelado no deja "Error no controlado" en el log (`tests/Optica.Api.Tests/Catalogo/BusquedaCanceladaTests.cs`, D-16) |
| F-SAST-16 | ✅ | Sin CVE Medium |
| F-SAST-17 | ✅ | Sin `eval`, `new Function` ni `setTimeout` con texto; la pausa usa `setTimeout` con una función (`web/src/componentes/useBusquedaEnVivo.ts`) |

## Re-cierre tras el corrective loop de VERIFY

El delta `0087924..HEAD` (commit `0fc3ec9`) solo toca `e2e/tests/busqueda-en-vivo.spec.ts` y `e2e/README.md`:
- Suma tres e2e para AC-09 y AC-10 que interceptan con `page.route` las búsquedas a la propia Api local. Una retiene
  la respuesta, otra responde un 500 sintético y otra hace `abort('failed')`.
- No hay hosts nuevos, secretos, datos personales ni `eval`.
- La única evaluación en la página es `requestAnimationFrame` fijo (`page.evaluate` sin datos externos).
- Los avisos de recurso esperados (500 y `net::ERR_FAILED`) se descuentan una vez cada uno; el resto de la consola y la
  CSP siguen exigiendo cero.
- `web/src` y `src` no cambian, así que los veredictos de abajo se mantienen.

## Carga de pedidos (R-01, R-03)

- **Pedidos por pausa:** el hook espera 300 ms y pide con 2 caracteres o más. Cancela el pedido anterior con
  `AbortController` y descarta las respuestas viejas por número de pedido.
- **Pruebas:** la e2e mide 1 pedido por pausa. La Api responde "armazón" sobre 10.000 artículos con un p95 de unos
  50 ms, contra un límite de 700 ms.
- **Sin cambio de backend:** no hay endpoints nuevos ni límites nuevos que configurar.

## Hallazgos

- **H-01:** dependencia de desarrollo ya aceptada (D-28 de FEAT-011), sin cambios en este ticket. No bloquea.
- **H-02:** falso positivo. La contraseña de la e2e es de una base temporal.
- **H-03:** falso positivo. Los clientes sembrados por la e2e son ficticios, con DNI que empiezan con `00` y sin
  correo.
- **H-04 (informativo, datos personales):** ya se informó al usuario y quedó registrado en VERIFY.
  - Vaciar el último campo de texto busca sola la primera página (D-07). Son los mismos datos que da "Buscar" con la
    sesión iniciada.
  - Los listbox ocultos de los campos sin foco conservan apellido y nombre en el DOM, nunca el DNI.
  - No es una vulnerabilidad nueva: no hay datos fuera de la sesión ni fuera de la pestaña.

No hay exposición nueva de datos personales, de clientes, de la empresa, de empleados ni de proveedores.

## Suppressions
None.

Total: 17 clean, 0 vulnerabilities (0 critical, 0 high)
Result: PASSED
