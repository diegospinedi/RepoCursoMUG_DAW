/**
 * Encabezado de todas las pantallas con el logo de la marca. El logo se pide con `<img>`, nunca se incrusta: así un
 * SVG no ejecuta scripts en la página (R-10).
 */
export function Encabezado() {
  return (
    <header className="encabezado">
      <img className="encabezado__logo" src="/api/marca/logo" alt="Logo de la óptica" />
    </header>
  );
}
