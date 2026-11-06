# SAST FIX-001

| Field | Value |
|-------|-------|
| Ticket | FIX-001 |
| Alcance | `git diff db374ed...307fc07` de `src/`: `src/Optica.Domain/Acceso/PoliticaBloqueo.cs` y `src/Optica.Application/Acceso/IngresarConContrasena.cs` (solo comentarios); pruebas de Domain, Application y Api; `docs/instalacion.md` |
| Tools | revisión manual del diff, `npm audit` (web y e2e), `dotnet list Optica.sln package --vulnerable --include-transitive` |
| Date | 2026-11-06 |

| Rule | Verdict | Notes |
|---|---|---|
| F-SAST-01 | ✅ | sin secretos en el diff; las pruebas usan las contraseñas de prueba existentes (`Http.Contrasena`, "otra-clave-mala") |
| F-SAST-02 | ✅ | el arreglo no toca consultas; la lectura de rechazos sigue en EF Core parametrizado |
| F-SAST-03 | ✅ | nada llega a un ejecutor de comandos |
| F-SAST-04 | ✅ | sin eval ni deserialización insegura |
| F-SAST-05 | ✅ | sin rutas de archivo construidas con entrada del usuario |
| F-SAST-06 | ✅ | sin cambios en la web |
| F-SAST-07 | ✅ | sin pedidos salientes |
| F-SAST-08 | ✅ | el hash PBKDF2-SHA256 no cambia; durante el bloqueo sigue sin calcularse |
| F-SAST-09 | ✅ | sin cambios de modo de depuración |
| F-SAST-10 | ✅ | el arreglo no agrega registros al log; la respuesta 423 no expone la hora de fin del bloqueo |
| F-SAST-11 | ✅ | no hay superficie de subida |
| F-SAST-12 | ✅ | `POST /api/acceso/ingresar` sin cambios: cookie `SameSite=Strict`, sin CORS, JSON obligatorio |
| F-SAST-13 | ✅ | `npm audit` en web/ y e2e/: 0 vulnerabilidades; sin dependencias nuevas |
| F-SAST-14 | ✅ | `PoliticaBloqueo.Evaluar` sigue exigiendo fechas UTC (`ExigirUtc`); la cota inferior nueva es `−ToleranciaRetroceso` (`src/Optica.Domain/Acceso/PoliticaBloqueo.cs:82`) |
| F-SAST-15 | ✅ | las excepciones de la política siguen sin valores; la Api responde el 500 genérico |
| F-SAST-16 | ✅ | `dotnet list package --vulnerable --include-transitive`: los 8 proyectos sin paquetes vulnerables |
| F-SAST-17 | ✅ | sin rutas de código dinámicas |

## Suppressions
None.

## Notas
- El arreglo cierra la falla de seguridad R-01 de `docs/ddw/security/threat-FIX-001.md` (un retroceso del reloj de
  hasta 5 minutos levantaba el bloqueo por intentos fallidos). Queda R-04, aceptado por el usuario: una corrección
  automática del reloj de más de 5 minutos hacia atrás sigue liberando el bloqueo.

Total: 17 clean, 0 vulnerabilities (0 critical, 0 high)
Result: PASSED
