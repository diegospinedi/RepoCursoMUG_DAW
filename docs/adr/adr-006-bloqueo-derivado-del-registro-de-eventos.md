# ADR-006: Bloqueo por intentos fallidos derivado del registro de eventos

| Field | Value |
|-------|-------|
| Date | 2026-11-03 |
| Ticket | FEAT-001b |
| Status | Accepted |

## Context
FEAT-001b (FR-01, FR-02, NFR-01) pidió bloquear el ingreso 5 minutos después de 5 intentos fallidos
consecutivos y liberarlo solo. FEAT-001a ya guardaba cada intento en `EventoAcceso` (tipo, resultado
y fecha, sin contraseña ni IP). Hacía falta decidir dónde vive el estado del bloqueo.

## Options considered

### Option 1: contador y fecha de bloqueo en una tabla o en la fila de credencial
- **Pros:** consulta trivial; el estado se lee de una sola fila.
- **Cons:** requería una migración; dos fuentes de verdad (el contador y los eventos) que podían
  discrepar; el restablecimiento y el cambio de contraseña tenían que acordarse de reiniciarlo.

### Option 2: contador en memoria de la Api
- **Pros:** sin cambios en la base; rápido.
- **Cons:** un reinicio de la Api borraba el bloqueo, y reiniciarla quedaba como forma de saltearlo.

### Option 3: derivarlo de `EventoAcceso`
- **Pros:** sin migración ni tabla nueva; una sola fuente de verdad que ya era el registro de
  auditoría; sobrevive a un reinicio; los eventos de éxito, definición, cambio y restablecimiento
  reinician la cuenta sin código adicional.
- **Cons:** cada ingreso agrega una consulta al registro; la consulta y el registro del rechazo no
  son atómicos, así que con 2 pedidos concurrentes la cuenta podía saltar de 4 a 6.

## Decision
Se eligió la opción 3. `PoliticaBloqueo` (dominio, sin base) cuenta los `ingreso/rechazo` posteriores
al último evento que reinicia la cuenta, ordenados por `Id` y no por fecha. Con `k` rechazos y
`n = floor(k/5) ≥ 1`, el ingreso quedó bloqueado 5 minutos desde el rechazo número `5·n`: anclar el
bloqueo en el rechazo que cruzó el múltiplo, en vez de exigir que `k` fuera múltiplo de 5, evitó que
una carrera concurrente lo salteara (hallazgo de la auditoría de arquitectura de PLAN). Durante el
bloqueo se registra `ingreso/bloqueado`, que no cuenta, y no se calcula el hash. Los textos nuevos
(`bloqueado`, `cambio_contrasena`, `restablecimiento`) entraron sin cambiar columnas porque eran
texto sin CHECK; una migración aditiva sumó el índice `IX_EventoAcceso_Tipo_Resultado` para que la
consulta no recorriera la tabla.

## Consequences
- Afecta `EventoAcceso.cs`, `PoliticaBloqueo.cs`, `IRegistroEventosAcceso`, `RegistroEventosAccesoEf`,
  `OpticaDbContext` e `IngresarConContrasena`; la Api responde 423 durante el bloqueo y en el quinto
  rechazo.
- Por la concurrencia que admite el limitador (2 pedidos), una ventana puede registrar hasta 6
  rechazos, pero el bloqueo no se pierde.
- Ordenar por `Id` hizo que un cambio de hora de la PC no reiniciara la cuenta; atrasar la hora antes
  del ancla anula el bloqueo vigente (acceso a la PC o sincronización horaria) y libera como máximo
  4 intentos antes del próximo bloqueo.
