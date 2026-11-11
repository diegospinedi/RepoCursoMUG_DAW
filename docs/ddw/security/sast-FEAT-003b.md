# SAST FEAT-003b

| Field | Value |
|-------|-------|
| Ticket | FEAT-003b |
| Alcance | `git diff master..2fff802`: 22 archivos de `src/`, `web/src` y `e2e/` (sin migraciones: este ticket no agrega ninguna), más las pruebas .NET |
| Tools | revisión manual del diff con búsquedas de patrones (grep), `npm audit` (web y e2e), `dotnet list Optica.sln package --vulnerable` |
| Date | 2026-11-11 |

| Rule | Verdict | Notes |
|---|---|---|
| F-SAST-01 | ✅ | sin secretos embebidos en el diff; la única contraseña es la de prueba del e2e (`e2e-presupuestos-calculos-1`, `e2e/tests/presupuestos-calculos.spec.ts:22`), válida solo en la base temporal del harness; `.env` y `.env.*` en `.gitignore` |
| F-SAST-02 | ✅ | sin consultas nuevas: el alta y la lectura siguen por EF Core/LINQ (parametrizadas); sin `FromSqlRaw`/`ExecuteSqlRaw` ni SQL interpolado en el diff |
| F-SAST-03 | ✅ | ninguna entrada llega a `Process.Start` ni a comandos |
| F-SAST-04 | ✅ | sin `eval`, `new Function` ni deserialización insegura; `LectorCantidadJson` (`src/Optica.Api/Controllers/LectorCantidadJson.cs`) lee solo tokens numéricos y lanza `JsonException` sin mensaje ante cualquier otro; System.Text.Json con tipos concretos y números estrictos |
| F-SAST-05 | ✅ | ninguna entrada del usuario forma rutas de archivo; sin `File.`/`Path.Combine` en el diff |
| F-SAST-06 | ✅ | React escapa todo el texto; sin `dangerouslySetInnerHTML` ni `innerHTML` en `web/src` |
| F-SAST-07 | ✅ | sin pedidos salientes guiados por la entrada (sin `HttpClient` ni `fetch` nuevos fuera del cliente de la Api) |
| F-SAST-08 | ✅ | sin criptografía nueva |
| F-SAST-09 | ✅ | sin modo debug ni `EnableSensitiveDataLogging` nuevos |
| F-SAST-10 | ✅ | sin registro nuevo: ningún `ILogger` ni `console` en el diff; `DatosLinea`, `LineaPedida` y `SolicitudLinea` con `PrintMembers` falso (prueba `Solicitudes_ToString_SinValores`); mensajes de error con texto fijo; nada en `localStorage` ni `sessionStorage` |
| F-SAST-11 | ✅ | no hay superficie de carga de archivos |
| F-SAST-12 | ✅ | sin endpoints nuevos: el POST de presupuestos sigue con cookie `SameSite=Strict`, sin CORS, JSON obligatorio (415) y sesión obligatoria (401) |
| F-SAST-13 | ✅ | `npm audit`: 0 vulnerabilidades en web y en e2e; `dotnet list package --vulnerable`: ningún paquete vulnerable en los 8 proyectos |
| F-SAST-14 | ✅ | validación en el borde: cuerpo hasta 16 KB (`RequestSizeLimit`, 413), hasta 100 líneas, cantidad entera 1..9.999 y descuento 0..100 con 2 decimales (`ValidacionPresupuesto`), entero fuera de `int` llevado al rechazo del máximo (D-22), tope del contrato para línea y total (`ControlarTopes`, D-04); el dominio vuelve a validar (`CalculoLinea`, `LineaPresupuesto`, `Presupuesto`); en la web, regex anclada y lineal para la cantidad (D-29, sin ReDoS) |
| F-SAST-15 | ✅ | errores 400 por campo con texto fijo que no repite valores (pruebas `Post_Errores400_NoRepitenElValor`, `Post_CantidadFueraDeInt_400`, `Excepciones_NoRepitenElValor`); un importe excedido ya no termina en 500 (D-17); las excepciones internas no se serializan |
| F-SAST-16 | ✅ | sin dependencias nuevas en .NET, web ni e2e |
| F-SAST-17 | ✅ | sin rutas de código dinámico |

## Observaciones (informativas, no bloquean; documentadas en D-31 de `docs/ddw/specs/decisions-FEAT-003b.md`)
- Datos de prueba preexistentes con formato real (DNI "23.456.789", "30111222", "20333444"; email "ana@mail.com")
  en pruebas .NET, web y e2e anteriores a este ticket. No son datos de producción, pero podrían coincidir con
  personas reales; se recomienda un ticket aparte para reemplazarlos por valores ficticios (99.999.999,
  `@example.com`). Las pruebas nuevas de FEAT-003b usan 99.999.999.
- `e2e/servidor.mjs` levanta la Api sin fijar `Arca__Entorno=Simulado` de forma explícita; hoy no hay módulo ARCA,
  pero conviene fijarlo antes de la facturación.

## Suppressions
None.

Total: 17 clean, 0 vulnerabilities (0 critical, 0 high)
Result: PASSED
