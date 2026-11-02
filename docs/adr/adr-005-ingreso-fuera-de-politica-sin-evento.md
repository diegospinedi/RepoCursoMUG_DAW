# ADR-005: Ingresar con una contraseña fuera de política responde 400 sin registrar evento

| Field | Value |
|-------|-------|
| Date | 2026-10-31 |
| Ticket | FEAT-001a |
| Status | Accepted |

## Context
ADR-004 dejó que `ingresar` tratara una contraseña fuera de política (menos de 8, más de 128 o
caracteres no válidos) como un rechazo con evento en `EventoAcceso`, igual que una contraseña
incorrecta. Al construir la Api en el Block 3, la spec pedía 400 con campo y acción para una
contraseña inválida en `ingresar`, y la revisión contra la spec marcó la contradicción con ADR-004 y
con R-06 (registro de intentos de ingreso). El usuario lo decidió el 2026-10-31.

## Options considered

### Option 1: 401 con evento de rechazo, como decía ADR-004
- **Pros:** todo intento de ingreso queda en `EventoAcceso`; la respuesta no distingue una
  contraseña mal formada de una incorrecta.
- **Cons:** contradice el 400 con campo y acción de la spec; la pantalla no puede explicar qué
  corregir; llena el registro con errores de tipeo que nunca podrían ser la contraseña.

### Option 2: 400 con campo y acción, sin evento
- **Pros:** coincide con el contrato de la spec y con el de `definir`; la persona sabe qué corregir;
  el registro guarda solo intentos que llegaron a verificarse contra el hash.
- **Cons:** un intento con texto fuera de política no deja rastro; la respuesta revela la regla de
  largo, que ya es pública por AC-04.

## Decision
Se eligió la opción 2. La validación quedó en Application: `IngresarConContrasena` devuelve un
resultado de validación con el campo `contrasena` y la acción, sin calcular el hash ni registrar
evento, y la Api lo traduce a 400. Este punto reemplaza lo que ADR-004 decía sobre `ingresar`; el
resto de ADR-004 (NFC, pretope, rango de iteraciones) sigue vigente.

## Consequences
- Afecta `IngresarConContrasena` y sus pruebas, `AccesoController` y `RespuestasValidacion`.
- `EventoAcceso` registra solo ingresos aceptados y contraseñas incorrectas bien formadas; R-06
  queda cubierto para los intentos que pueden ser la contraseña real.
- El bloqueo por intentos fallidos de FEAT-001b (R-03) cuenta solo esos intentos.
