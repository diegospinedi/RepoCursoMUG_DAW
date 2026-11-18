# Verification FEAT-013

Búsqueda con sugerencias mientras se escribe. Esta es la ronda 1 de VERIFY, sobre el commit del cierre de CODE `0087924`.

**Contexto de la verificación:**
- **Rama:** `feat/FEAT-013-busqueda-en-vivo`, abierta sobre FEAT-012. Lo propio del ticket es `27e02ca^..HEAD`.
- **Verificador:** ddw-module-verifier, un agente que no escribió el código.
- **Fuentes contrastadas:**
  - `docs/ddw/prd/prd-FEAT-013.md` (14 AC);
  - `docs/ddw/specs/spec-FEAT-013.md` (5 bloques);
  - `docs/ddw/specs/decisions-FEAT-013.md` (D-01 a D-25);
  - `docs/ddw/security/threat-FEAT-013.md`;
  - `docs/ddw/security/sast-FEAT-013.md`;
  - la corrida de `docs/ddw/reports/tests-FEAT-013.md` (4864/4864).
- **Lo que el verificador volvió a correr:**
  - web con cobertura: 1512/1512;
  - oxlint, `tsc` y `test:lint` (8/8);
  - Playwright `chrome-busqueda-en-vivo` y `msedge-busqueda-en-vivo`: 16/16;
  - .NET `BusquedaCancelada_NoRegistraErrorNoControlado` y `Volumen_BuscarArmazon_P95MenorA700ms` (p95 de 45,3 ms).
- **Lo que no volvió a correr:** la suite .NET completa (2930), el resto del harness y `dotnet format`. Esos números salen
  del informe de pruebas.

| Field | Value |
|---|---|
| Module | `web/src/componentes` (useBusquedaEnVivo, ListaSugerencias, CampoBusqueda, CampoConOpciones), `web/src/api/cliente.ts`, `web/src/catalogo` (sugerencias, reglas), `web/src/paginas` (Articulos, BuscarPresupuestos, BuscarFacturas, Configuracion), `web/src/presupuestos/BuscadorArticulos.tsx`, `e2e/tests` (busqueda-en-vivo y los 10 specs de D-18), `tests/Optica.Api.Tests/Catalogo` (BusquedaCancelada, VolumenCatalogo) |
| Line coverage | 99.17% |
| Branch coverage | 97.55% |
| Function coverage | 100% |
| Coverage floor | 80% (method default — .ddw/rules/testing.instructions.md) |
| Lint | `npm run lint` (oxlint) — clean; `tsc --noEmit` — clean; `npm run build` — clean; `npm run test:lint` 8/8; `dotnet format --verify-no-changes` — clean |

**Cobertura.** La de la tabla es la de toda la web.

Por archivo, en líneas, ramas y funciones:
- **Al 100 % en las tres:** `useBusquedaEnVivo.ts`, `ListaSugerencias.tsx`, `CampoConOpciones.tsx`, `CampoBusqueda.tsx`,
  `BuscarPresupuestos.tsx`, `api/cliente.ts`, `catalogo/sugerencias.ts` y `catalogo/reglas.ts`.
- **Con alguna rama sin cubrir:**

  | Archivo | Líneas | Ramas | Funciones |
  |---|---|---|---|
  | `Articulos.tsx` | 100 % | 98,48 % | 100 % |
  | `BuscarFacturas.tsx` | 100 % | 98,48 % | 100 % |
  | `BuscadorArticulos.tsx` | 100 % | 98,03 % | 100 % |
  | `Configuracion.tsx` | 97,93 % | 90,74 % | 100 % |

Todos los archivos superan el 80 % y ninguno de lógica queda entre 80 y 90 % (W-VER-02 ✅). Las líneas sin cubrir son
guardas defensivas anteriores al ticket, salvo `BuscarFacturas.tsx:281` (H-6). El backend no cambió: el ticket agrega
solo pruebas .NET.

## Acceptance criteria
- ✅ AC-01 — `Articulos_EscribirArm_SugerenciasYTablaFiltrada`; e2e `sugiere y filtra artículos y abre la edición` (`web/src/paginas/Articulos.tsx`, `catalogo/sugerencias.ts:mostrarSugerencia`)
- ✅ AC-02 — `Articulos_UnCaracter_SinSugerencias`, `useBusquedaEnVivo_UnPedidoPorPausaYMinimoDosCaracteres`; en la e2e, de forma indirecta en `Buscar sigue funcionando` (`sugerencias.ts:esBuscableEnVivo`)
- ✅ AC-03 — `Articulos_ElegirSugerencia_AbreLaEdicion`; e2e `sugiere y filtra artículos y abre la edición` (artículo 7)
- ✅ AC-04 — `BuscarPresupuestos_EscribirApellido_SugerenciasYAbrir155`; e2e `sugiere presupuestos y facturas y los abre` (elegida con teclado)
- ✅ AC-05 — `BuscarFacturas_EscribirApellido_SugerenciasYAbrirLaFactura`; e2e `sugiere presupuestos y facturas y los abre`
- ✅ AC-06 — `NuevoPresupuesto_ElegirSugerencia_AgregaLaLinea`; e2e `agrega la sugerencia al presupuesto` (`BuscadorArticulos.tsx:agregar`)
- ✅ AC-07 — `ListaSugerencias_TecladoFlechasEnterEscapeTab`; e2e `teclado y anuncio de sugerencias`
- ✅ AC-08 — `Articulos_Buscar_ComoAntes`; e2e `Buscar sigue funcionando` (los cuatro buscadores)
- ✅ AC-09 — `useBusquedaEnVivo_RespuestaViejaDescartada`, `BuscarPresupuestos_RespuestaViejaNoPisaLaNueva`, `BuscarFacturas_RespuestaViejaNoPisaLaNueva` (sin e2e, ver H-3)
- ✅ AC-10 — `useBusquedaEnVivo_ErrorDeRed_SinSugerencias`, `Articulos_ErrorDeBusqueda_SinSugerencias`, `BuscarFacturas_ErrorDeBusqueda_SinSugerencias` (sin e2e, ver H-3)
- ✅ AC-11 — `Configuracion_Alicuota10_SoloOpcion105`; e2e `configuración filtra la alícuota y rechaza una condición inexistente`
- ✅ AC-12 — `Configuracion_CondicionInexistente_RechazadaSinGrabar`; e2e `configuración filtra la alícuota y rechaza una condición inexistente`
- ✅ AC-13 — e2e `un pedido por pausa y sugerencias en menos de un segundo`; `Volumen_BuscarArmazon_P95MenorA700ms` (p95 de 45,3 ms contra un límite de 700)
- ❌ AC-14 — la e2e corre en Chrome y Edge estables (16/16 y harness 258/258) y cubre AC-01, AC-03 a AC-08 y AC-11 a AC-13, y AC-02 de forma indirecta. Pero AC-14 pide ejecutar AC-01 a AC-12 en los dos navegadores, y AC-09 y AC-10 quedan solo en Vitest con jsdom (H-3). Falta esa cobertura: corrective loop a CODE

Todas las pruebas verifican comportamiento: cuerpo del pedido, filas, URL de destino, cantidad de pedidos y anuncio.
Ninguna se queda solo en un código de estado.

## Spec blocks
- ✅ Block 1 — 7/7 tareas, 11/11 pruebas requeridas. `ManejadorErroresNoControlados.cs` no hizo falta: un pedido cancelado no deja error (D-16, `BusquedaCancelada_NoRegistraErrorNoControlado`)
- ✅ Block 2 — 2/2 tareas, 7/7 pruebas requeridas (D-19)
- ✅ Block 3 — 3/3 tareas, 6/6 pruebas requeridas, con `CampoFiltro` (D-12) y la ruta real de facturas (D-20); D-21 a D-23
- ✅ Block 4 — 3/3 tareas, 3/3 pruebas requeridas. `configuracion/reglas.ts` no hizo falta; se quitaron las reglas `.campo select` (D-24)
- ✅ Block 5 — 4/4 tareas, 9/9 pruebas requeridas. Se actualizaron 10 specs, `medidas.ts`, el README y la prueba de volumen; `estilo-uniforme.spec.ts` no necesitó cambios (D-25)
- ✅ Final verification — suite completa en verde, los 14 AC con prueba, ningún `package.json`, `package-lock.json` ni `.csproj` cambiado, sin cambios de esquema ni de migraciones; los triggers no se tocaron

Las 36 pruebas requeridas de la spec existen con su nombre exacto y pasan. No queda ningún `<select>` ni `<option>` en
`web/src`, ni `selectOption` en la e2e.

## Tests
- ✅ Sad-path tests (F-VER-04): cada función o pantalla que recibe entrada tiene su prueba con entrada inválida o con falla:
  - cliente: pedido cancelado;
  - hook: error de red y `buscar` que rechaza;
  - `ListaSugerencias`: HTML dentro de una sugerencia;
  - Artículos: 1 carácter, texto inválido y error de red;
  - buscador del presupuesto: precio negativo;
  - Buscar presupuestos: rango inválido en vivo, rango inválido durante la pausa, 1 carácter, 400 en vivo y sin sesión;
  - Facturas: error de búsqueda y 400 en vivo;
  - Configuración: condición inexistente, alícuota inexistente y alícuota fuera de la lista;
  - e2e: inyección de HTML con verificación de CSP.
- ✅ Lint y tipos (F-VER-05): oxlint, `tsc`, build y `test:lint` limpios.
- ✅ Código muerto (W-VER-01): sin imports sin uso (`noUnusedLocals` y `noUnusedParameters` activos), sin código comentado y sin exports sin uso.
- ⚠️ Pruebas frágiles (W-VER-03): ver H-1 y H-2, aceptados en D-25.

## Hallazgos

H-3 bloquea: AC-14 no se cumple completo. El resto no bloquea.

- **H-1 (W-VER-03, aceptado en D-25):** `busqueda-en-vivo.spec.ts` depende del orden y lo declara con modo serie.
  - Necesita la base vacía.
  - El artículo 7 y el presupuesto 155 los fijan los AC; al 155 se llega con Borradores de relleno.
- **H-2 (W-VER-03, aceptado en D-25):** dos pruebas dependen de los tiempos de la máquina.
  - La e2e de AC-13 exige entre 300 y 1000 ms, con teclas cada 30 ms.
  - La prueba de volumen mide tiempo.
  - Además, los specs viejos de búsqueda manual pueden coincidir con un pedido en vivo en PC lentas.
- **H-3 (AC-14, FAIL):** AC-09 (respuesta vieja) y AC-10 (falla de la búsqueda) se prueban solo en Vitest con
  jsdom, no en Chrome ni Edge. AC-02 se cubre en la e2e solo de forma indirecta. El Block 5 aprobado en PLAN ya los
  dejaba afuera, pero el PRD los incluye. Se corrigen en CODE con dos e2e que usen `page.route`.
- **H-4 (evidencia TDD):** no está en los commits ni en los documentos versionados. Queda en las revisiones por bloque
  de CODE (`.ddw-work/review-block-1.md` a `-5.md`, fuera de git), donde el module-verifier de cada bloque la
  reprodujo:
  - Block 3: rojo contra la ronda anterior reconstruida y mutante sin sobrevivientes.
  - Block 4: rojo contra HEAD, letra por letra, y 7 mutaciones detectadas.
  - Block 5: mutaciones A, B, C y límite de 1 ms.
- **H-5 (documento):** `sast-FEAT-013.md` ubica `CampoBusqueda` en `Campo.tsx`; en realidad está en
  `web/src/componentes/CampoBusqueda.tsx`. No cambia ningún veredicto del SAST.
- **H-6 (cobertura menor):** `BuscarFacturas.tsx:281`, la rama de un 400 con un solo error de campo en el envío
  manual, no se ejerce. La lógica es anterior al ticket y la misma rama de `BuscarPresupuestos.tsx` sí está cubierta.
- **H-7 (aceptado en D-21):** unas 130 líneas de lógica en vivo duplicadas entre `BuscarPresupuestos.tsx` y
  `BuscarFacturas.tsx`.
- **H-8 (datos personales, informativo, ya informado al usuario):** no es una vulnerabilidad nueva.
  - Vaciar el último campo de texto busca sola la primera página (D-07).
  - Las listas ocultas de los campos sin foco conservan apellido y nombre en el DOM, nunca el DNI.
  - Lo que se verificó:
    - Ni las sugerencias, ni los ids, ni el anuncio muestran el DNI.
    - Los filtros de presupuestos y facturas viajan en el cuerpo, no en la URL.
    - Los campos tienen `autoComplete="off"` y no se escribe nada en la consola.
    - La e2e usa clientes ficticios.
    - El log de la Api durante la e2e no muestra apellidos, DNI ni "Error no controlado".
- **H-9 (cosmético):** la spec y D-05 dicen `señal`; el código usa `senal`.

Result: BLOCKED — vuelta a CODE para sumar AC-09 y AC-10 a la e2e en Chrome y Edge (AC-14).
