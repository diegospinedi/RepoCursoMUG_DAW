# ADR-003: Marca de ejemplo en el repositorio y marca real instalada fuera de él

| Field | Value |
|-------|-------|
| Date | 2026-10-30 |
| Ticket | FEAT-001a |
| Status | Accepted |

## Context
La app muestra el logo y los colores de la óptica (`docs/ddw/prd/prd-FEAT-001a.md`, FR-06), pero el
repositorio es público y las dueñas decidieron no publicar la marca real (FR-07). La marca real
estaba en una carpeta local `Marca/` sin versionar, con `branding.json`, `logo.png` y el original
`Marca.jpg`.

## Options considered

### Option 1: marca de ejemplo versionada y carpeta de marca configurable
- **Pros:** el repo no contiene la marca real; la app funciona recién clonada; cambiar la marca no
  requiere recompilar.
- **Cons:** la instalación en la PC del local tiene que copiar la marca; si se olvida, la app
  muestra la de ejemplo.

### Option 2: marca real compilada dentro de la app
- **Pros:** imposible olvidarse de instalarla.
- **Cons:** la marca quedaría en el repositorio público; las dueñas lo descartaron.

### Option 3: marca real guardada en la base
- **Pros:** viaja con el backup.
- **Cons:** requiere una pantalla para cargarla que el PRD no pide.

## Decision
Se eligió la opción 1: `marca-ejemplo/` va al repo, la marca real se lee de
`Optica:Marca:Directorio`, y `/Marca/` (con barra inicial, para no ignorar carpetas de código del
mismo nombre) va a `.gitignore`.

## Consequences
- Archivos: `src/Optica.Infrastructure/Marca/ProveedorMarcaArchivo.cs`, `marca-ejemplo/`,
  `.gitignore`.
- El procedimiento de instalación incluye copiar la marca y configurar su carpeta.
- `branding.json` conserva el esquema de la marca real; del campo `logo` se usa solo el nombre del
  archivo, dentro de la carpeta de marca.
