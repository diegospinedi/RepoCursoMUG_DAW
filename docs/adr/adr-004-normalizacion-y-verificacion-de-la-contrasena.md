# ADR-004: Normalización Unicode de la contraseña y rango de iteraciones al verificar

| Field | Value |
|-------|-------|
| Date | 2026-10-31 |
| Ticket | FEAT-001a |
| Status | Accepted |

## Context
La spec de FEAT-001a (Block 2) fijó 8 a 128 caracteres "contados sin recortar espacios" y ADR-002
fijó PBKDF2-SHA256 con 600.000 iteraciones, pero ninguno de los dos definía qué se cuenta ni qué se
hashea cuando el mismo texto llega con codificaciones Unicode distintas (una "ñ" compuesta o como
"n" + tilde combinante), qué pasa con texto UTF-16 mal formado, ni qué hashes se aceptan al
verificar. Las revisiones del Block 2 encontraron además que un sustituto suelto hacía que el hash
lanzara una excepción con el carácter y su posición dentro de la contraseña. Las dueñas del producto
lo decidieron el 2026-10-31.

## Options considered

### Option 1: usar el texto tal cual llega
- **Pros:** sin código adicional; el largo es `string.Length`, igual que en el navegador.
- **Cons:** una contraseña con tildes definida desde un navegador puede no verificar desde otro; el
  texto mal formado llega al hash y lo hace fallar con datos de la contraseña en el mensaje; se
  acepta cualquier hash, incluso uno degradado a 1 iteración.

### Option 2: normalizar a NFC, rechazar texto inválido y acotar las iteraciones
- **Pros:** la misma contraseña siempre verifica; nunca se guarda una contraseña imposible de
  verificar; un hash manipulado con menos costo no se acepta; ningún error expone la contraseña.
- **Cons:** un mensaje de error nuevo que la spec no tenía; el navegador tiene que normalizar igual
  para no discrepar con el servidor.

## Decision
Se eligió la opción 2. La política de dominio (`PoliticaContrasena`) corta primero por un pretope de
512 unidades sin recorrer el texto, después rechaza el UTF-16 mal formado como `CaracteresInvalidos`
(al definir, acción "La contraseña tiene caracteres no válidos. Escribila de nuevo."; al ingresar,
rechazo con evento y sin calcular el hash) y por último cuenta 8 a 128 unidades UTF-16 de la forma
NFC, de modo que un emoji cuenta como 2. El hash se calcula y se verifica sobre la forma NFC. Al
verificar se aceptan de 600.000 a 10.000.000 iteraciones; el piso es un literal independiente del
costo actual, para que subirlo más adelante no invalide la contraseña ya guardada.

## Consequences
- Afecta `src/Optica.Domain/Acceso/PoliticaContrasena.cs`, los casos de uso `DefinirContrasena` e
  `IngresarConContrasena` y `src/Optica.Infrastructure/Seguridad/HasherContrasenaPbkdf2.cs`.
- El hasher quedó sin normalizar por su cuenta: la normalización vive en un solo lugar, la política,
  y un llamador que la saltee obtiene hashes que no coinciden con los guardados.
- La validación del navegador (Block 5) discrepa del servidor si cuenta sin normalizar; eso quedó
  anotado como pendiente de ese bloque.
- Un hash con menos de 600.000 iteraciones deja de verificar; hoy no existe ninguno.
