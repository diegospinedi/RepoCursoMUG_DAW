# SAST FEAT-007

| Field | Value |
|-------|-------|
| Ticket | FEAT-007 |
| Tools | revisión del diff contra `master` (66 archivos de producción de `src/`, `web/src/` y `e2e/`) con grep de patrones; `dotnet list Optica.sln package --vulnerable --include-transitive`; `npm audit` en `web/` y en `e2e/` (con y sin dependencias de desarrollo); revisiones de seguridad por bloque de ddw-module-verifier y ddw-arch-auditor |
| Date | 2026-11-13 |

| Rule | Verdict | Notes |
|---|---|---|
| F-SAST-01 | ✅ | sin secretos embebidos: `appsettings.json` solo trae `Arca:Entorno = Simulado`; CUIT, ruta y clave del certificado van por variables de entorno o configuración local no versionada (D-13); `.gitignore` excluye `.env`, `*.pfx`, `*.p12` y `*.key`; `InstalacionTests` falla si una clave `Clave` tiene valor en `appsettings.json` (D-26); `ClaveCertificadoClave` es el nombre de la clave, no su valor; el certificado de las pruebas se genera en memoria |
| F-SAST-02 | ✅ | sin SQL armado: repositorios con EF Core parametrizado, sin `FromSqlRaw` ni `ExecuteSqlRaw`; las migraciones escritas a mano (`ConfiguracionPuntoVenta`, `Facturas`) son SQL estático sin datos de entrada |
| F-SAST-03 | ✅ | nada de entrada del usuario llega a exec/spawn: el único `spawn` es el del harness de e2e (`e2e/servidor.mjs:100`), con `dotnet` y una ruta fija, y un entorno filtrado de claves ARCA (D-65) |
| F-SAST-04 | ✅ | sin `eval`, `new Function` ni deserialización insegura; los JSON del simulador y del ticket se leen con `JsonDocument` con `MaxDepth = 4` y tope de 64 KB |
| F-SAST-05 | ✅ | sin rutas armadas con entrada del usuario: `arca-simulado.json` y `arca-ta-<entorno>.json` se combinan con el directorio de datos y un nombre fijo por entorno (enum); la ruta del PFX sale de la configuración del administrador, no de la web |
| F-SAST-06 | ✅ | sin `innerHTML` ni `dangerouslySetInnerHTML`; la descripción de un rechazo de ARCA se muestra como texto de React, saneada y recortada a 500 en la Api (D-57) y validada en la web; prueba con `<img onerror>` |
| F-SAST-07 | ✅ | las URL de ARCA son constantes por entorno (`ServiciosArca.cs`), nunca configurables desde la web ni con entrada del usuario; `AllowAutoRedirect = false` para que el token no viaje a otro host; validación TLS por defecto, sin `ServerCertificateCustomValidationCallback` |
| F-SAST-08 | ✅ | firma CMS con SHA-256 y la clave RSA del certificado (`SignedCms`); el ticket en disco se cifra con ASP.NET Data Protection (D-30); el CAE simulado sale de `RandomNumberGenerator`; sin MD5, SHA1, DES ni ECB |
| F-SAST-09 | ✅ | sin modo debug nuevo; producción y homologación se advierten en el log al arrancar; el entorno por defecto es Simulado (R-14) |
| F-SAST-10 | ✅ | el log de ARCA y de la emisión lleva solo entorno, tipo, punto de venta, número, CAE, códigos de ARCA, nombre de archivo, segundos y el tipo de la excepción; nunca el cuerpo SOAP, token, firma, CMS, CUIT, ruta o clave del PFX, DNI ni la descripción de un rechazo; las excepciones no se adjuntan (`{Causa}` es un texto fijo de `CertificadoArcaException`); pruebas con centinelas en Infrastructure, Application y la Api, incluido el DNI ficticio dentro del texto de una excepción |
| F-SAST-11 | ✅ | sin superficie de carga de archivos nueva |
| F-SAST-12 | ✅ | la ruta nueva `POST /api/presupuestos/{numero}/factura` usa la misma sesión con cookie y la protección existente de la Api (`ProteccionTests` la incluye: 401 sin sesión, 415, 413); `no-store` en todas sus respuestas (`SinCacheTests`) |
| F-SAST-13 | ✅ | `dotnet list package --vulnerable --include-transitive`: los 8 proyectos sin paquetes vulnerables; `npm audit` en `web/` y `e2e/`: 0 vulnerabilidades |
| F-SAST-14 | ✅ | validación en el borde: concepto exacto de dos valores con JSON estricto y cuerpo de 1 KB; punto de venta entero estricto de 1 a 99999; alícuota de las 6 de ARCA al grabar (D-35); respuestas de ARCA leídas con `DtdProcessing.Prohibit`, `XmlResolver = null` y tope de 1 MB; lectores estrictos en la web para el GET, el 201 y los 409 |
| F-SAST-15 | ✅ | los 409 de la emisión llevan título y detalle fijos por motivo (D-61); un 500 no muestra el texto de la excepción (prueba `Emitir_AutorizadorLanza_500ConLaPendienteGuardada`); las excepciones de ARCA se relanzan con texto fijo y sin causa |
| F-SAST-16 | ✅ | dependencia nueva `System.Security.Cryptography.Pkcs` 10.0.13: la 10.0.12 que fijaba la spec tenía GHSA-846f-f764-76mx (alto) y GHSA-2fmv-rv2c-59x7 (moderado), corregidos en 10.0.13 (D-56, ADR-024); `Microsoft.Extensions.TimeProvider.Testing` 10.0.0 solo en pruebas; restauración en modo bloqueado verificada |
| F-SAST-17 | ✅ | sin rutas de código dinámico |

## Suppressions
None.

## Notas de datos personales y de la empresa
- La factura guarda el DNI del cliente sobre el tope (FR-11, FR-18) sin cifrar en `optica.db` y en los backups: riesgo
  R-13 ya aceptado por el usuario (FEAT-003a); `docs/instalacion.md` §11 pide una carpeta de datos con permisos
  restringidos y nunca compartida.
- R-10 (factura simulada sin aviso en pantalla) aceptado por el usuario hasta el PRD 08 (D-63).
- La clave del certificado como variable de entorno de alcance "User" queda sin cifrar en `HKCU\Environment`; la guía
  lo advierte y pide una cuenta de uso exclusivo para la Api.

Total: 17 clean, 0 vulnerabilities (0 critical, 0 high)
Result: PASSED
