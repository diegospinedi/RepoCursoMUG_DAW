# ADR-020: Publicación de la Api restringida a win-x64

| Field | Value |
|-------|-------|
| Date | 2026-11-13 |
| Ticket | FEAT-006 |
| Status | Accepted |

## Context
ADR-018 eligió QuestPDF para el PDF del presupuesto y estimó "unos 10 MB de binarios nativos". En CODE se midió que
QuestPDF 2026.9.1 trae sus binarios (`QuestPdfSkia`, `qpdf`) para 8 plataformas: unos 12 MB para win-x64 y unos 85 MB
en una publicación sin identificador de plataforma, que copia todas. La guía de instalación publicaba sin `-r`, salvo
desde Linux o WSL. La PC del local es Windows x64, y la CI corre las pruebas en Linux.

## Options considered

### Option 1: publicar siempre con `-r win-x64 --self-contained false`
- **Pros:** la instalación lleva solo los binarios de Windows (unos 12 MB); el comando es el mismo desde Windows y
  desde Linux o WSL; no cambia la compilación ni las pruebas.
- **Cons:** depende de que quien publica use el comando de la guía.

### Option 2: fijar `RuntimeIdentifier` en `Optica.Api.csproj`
- **Pros:** no depende del comando.
- **Cons:** la compilación y las pruebas de la CI en Linux quedarían sin el binario nativo de Linux y el generador no
  funcionaría ahí.

### Option 3: publicar sin plataforma, como antes
- **Pros:** ningún cambio.
- **Cons:** unos 70 MB de binarios que la PC del local nunca usa.

## Decision
Se eligió la opción 1: el comando de `docs/instalacion.md` pasó a llevar `-r win-x64 --self-contained false`, y la guía
explica por qué no se fija la plataforma en el `.csproj`. Complementa la consecuencia de tamaño de ADR-018 sin cambiar
su decisión.

## Consequences
- Archivo: `docs/instalacion.md` (sección 2).
- La publicación produce `Optica.Api.exe` también desde Linux o WSL.
- Una publicación hecha sin `-r` sigue funcionando en Windows, solo que más pesada.
