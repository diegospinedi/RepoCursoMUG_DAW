namespace Optica.Domain.Facturacion;

/// <summary>
/// El filtro de la búsqueda de facturas no cumple una regla del dominio (FR-21 de FEAT-009): Desde es posterior a Hasta.
/// Es propia de Facturación, distinta de la de Presupuestos. El mensaje es fijo: no copia las fechas ni los textos
/// recibidos.
/// </summary>
public sealed class FiltroFacturasInvalidoException(string mensaje) : Exception(mensaje);
