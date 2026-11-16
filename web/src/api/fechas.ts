/**
 * Las fechas `yyyy-MM-dd` del contrato de la Api: una sola expresión y una sola validación, para los lectores de
 * `cliente.ts` y para las reglas de los formularios. No importa nada, así lo usan los dos sin un ciclo de módulos.
 */

/** Cuatro dígitos de año, dos de mes y dos de día, separados por guiones; solo dígitos ASCII. */
export const FORMATO_FECHA = /^(\d{4})-(\d{2})-(\d{2})$/;

/**
 * Una fecha `yyyy-MM-dd` que existe en el calendario ("2026-02-30" no), como `DateOnly.TryParseExact` del servidor: los
 * años 1 a 99 se toman tal cual (no como 19xx) y el año 0 no existe. Se compara en UTC: sin zona horaria.
 */
export function esFecha(valor: unknown): valor is string {
  if (typeof valor !== 'string') {
    return false;
  }
  const partes = FORMATO_FECHA.exec(valor);
  if (partes === null) {
    return false;
  }
  const [anio, mes, dia] = [Number(partes[1]), Number(partes[2]), Number(partes[3])];
  if (anio < 1) {
    return false;
  }
  // `Date.UTC` lleva los años 0 a 99 a 1900–1999; `setUTCFullYear` no.
  const fecha = new Date(0);
  fecha.setUTCFullYear(anio, mes - 1, dia);
  return fecha.getUTCFullYear() === anio && fecha.getUTCMonth() === mes - 1 && fecha.getUTCDate() === dia;
}
