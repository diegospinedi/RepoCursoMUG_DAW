namespace Optica.Application.Comun;

/// <summary>Error de validación de una entrada: el campo afectado y la acción que lo corrige.</summary>
public sealed record ErrorValidacion(string Campo, string Accion);
