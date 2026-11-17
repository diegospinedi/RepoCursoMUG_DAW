# Threat model FEAT-012: Menú de Inicio moderno

| Field | Value |
|-------|-------|
| Ticket | FEAT-012 |
| Spec | docs/ddw/specs/spec-FEAT-012.md |
| Tier | FEATURE |
| Date | 2026-11-17 |

El cambio es solo de presentación en la web: cambia la forma en que se dibujan las siete opciones de Inicio y, con la
ampliación, el aspecto de las 15 pantallas desde la hoja de estilos global. No hay
endpoints, datos ni permisos nuevos. Los modelos de FEAT-001a (sesión, CSP) y FEAT-010 (aviso de backup) siguen
vigentes.

## Components
| Component | Source in the spec |
|---|---|
| `web/src/paginas/MenuInicio.tsx` | Block 1 |
| `web/src/componentes/IconoMenu.tsx` | Block 1 |
| `web/src/paginas/Inicio.tsx` | Block 1 |
| `web/src/estilos.css` | Block 1, Block 2 |
| `e2e/tests/estilo-uniforme.spec.ts` | Block 3 |

## Trust boundaries
- Api → navegador: `GET /api/marca` entrega los colores de la marca instalada. Los aplica `MarcaProvider` como
  variables CSS y el menú los usa en íconos, franja y foco.
- Navegador → Api: las tarjetas solo navegan a rutas de la propia SPA. No envían datos.

## STRIDE analysis
### `web/src/paginas/MenuInicio.tsx`
- **Spoofing:** la pantalla sigue detrás de la sesión. Las rutas de destino son fijas en el código y no salen de datos
  externos.
- **Tampering:** el menú se arma desde una constante del código (`SECCIONES_MENU`), sin entrada del usuario ni de la
  Api.
- **Repudiation:** no aplica, porque navegar entre pantallas no es una acción que se registre.
- **Information Disclosure:** las descripciones no muestran datos de clientes, de ARCA (punto de venta, certificado)
  ni de la empresa. Lo vigila `App.test.tsx` (R-01).
- **Denial of Service:** no aplica: son siete tarjetas estáticas.
- **Elevation of Privilege:** no hay roles. El menú no agrega destinos que la sesión no tuviera.

### `web/src/componentes/IconoMenu.tsx`
- **Spoofing:** no aplica.
- **Tampering:** los SVG son JSX estático, sin `dangerouslySetInnerHTML` ni contenido que llegue de afuera.
- **Repudiation:** no aplica.
- **Information Disclosure:** no aplica: los íconos son decorativos y llevan `aria-hidden`.
- **Denial of Service:** no aplica.
- **Elevation of Privilege:** no aplica.

### `web/src/paginas/Inicio.tsx`
- **Spoofing:** sin cambios en la verificación de la sesión ni en "Salir".
- **Tampering:** sin cambios.
- **Repudiation:** sin cambios.
- **Information Disclosure:** los avisos siguen con los mismos textos y van antes del menú (FR-07).
- **Denial of Service:** sin cambios.
- **Elevation of Privilege:** sin cambios.

### `web/src/estilos.css`
- **Spoofing:** no aplica.
- **Tampering:** la CSP `default-src 'self'` sigue vigente. El estilo va solo por clases en la hoja propia, sin
  atributos `style`, `<style>` en línea ni `data:`. Un estilo en línea quedaría bloqueado en producción (R-02).
- **Repudiation:** no aplica.
- **Information Disclosure:** no aplica.
- **Denial of Service:** no aplica.
- **Elevation of Privilege:** no aplica.

### `e2e/tests/estilo-uniforme.spec.ts`
- **Spoofing:** usa una Api y una base temporales del harness, con una contraseña de prueba.
- **Tampering:** siembra sus propios datos ficticios por la Api y no toca datos reales.
- **Repudiation:** no aplica.
- **Information Disclosure:** las capturas de pantalla quedan en `test-results/`, que git ignora, y solo muestran datos
  ficticios (R-04).
- **Denial of Service:** no aplica.
- **Elevation of Privilege:** no aplica.

## Data classification
| Data | Class | At rest | In transit |
|---|---|---|---|
| Colores de la marca (primario y fondo) | public | `branding.json` en la carpeta de la marca, sin cambios | TLS, `GET /api/marca` |
| Nombres y descripciones del menú | public | en el código de la web | TLS, dentro del bundle |

## Risks and mitigations
| ID | Risk | STRIDE | Likelihood | Impact | Mitigation |
|---|---|---|---|---|---|
| R-01 | Una descripción nombra datos de ARCA o de clientes en la pantalla de Inicio | I | L | L | Descripciones fijas revisadas (D-09) y prueba `Pantallas_NingunaMuestraDatosArca` |
| R-02 | Un estilo en línea relaja la CSP o falla en producción sin que se note en desarrollo | T | L | M | Solo clases en `estilos.css`; la e2e falla ante cualquier violación de CSP |
| R-04 | Una captura de la e2e muestra datos de clientes reales | I | L | M | La e2e siembra solo datos ficticios en una base temporal; `test-results/` está fuera de git |
| R-03 | Una marca con un primario claro deja el contorno del foco con poco contraste | I | L | L | Texto en `--color-texto` (D-04); NFR-01 verificado con las dos marcas conocidas (D-10); riesgo residual documentado para marcas nuevas |

## Supply chain
Sin dependencias nuevas (NFR-03): los íconos son SVG propios en JSX.

## Availability
No aplica: el cambio no agrega trabajo de servidor ni pedidos. La pantalla hace los mismos dos pedidos de antes
(emisiones pendientes y estado del backup).
