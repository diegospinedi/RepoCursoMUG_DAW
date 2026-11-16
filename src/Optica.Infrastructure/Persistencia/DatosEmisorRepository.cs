using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Optica.Domain.Configuracion;

namespace Optica.Infrastructure.Persistencia;

/// <summary>
/// Datos del emisor sobre la tabla <c>DatosEmisor</c> (fila única con <c>Id = 1</c>, ADR-028). La migración no inserta
/// fila: hasta la primera grabación no hay datos. <see cref="Reemplazar"/> solo deja el cambio en el contexto
/// compartido, que confirma <see cref="UnidadDeTrabajoEf"/>.
/// </summary>
public sealed class DatosEmisorRepository(OpticaDbContext contexto) : IDatosEmisorRepository
{
    /// <summary>Clave de la única fila permitida por <c>CHECK (Id = 1)</c>.</summary>
    public const int IdUnico = 1;

    private const string MensajeFilaInvalida = "Los datos del emisor guardados no son válidos.";

    /// <summary>Lee la fila sin seguimiento en cada llamada, sin caché; null si no hay fila.</summary>
    /// <exception cref="InvalidOperationException">
    /// La fila no cumple las reglas de lectura (editada a mano con los CHECK desactivados, o con un dígito verificador
    /// que el CHECK no controla): estado corrupto, con texto fijo y sin valores.
    /// </exception>
    public async Task<DatosEmisor?> ObtenerAsync(CancellationToken cancelacion = default)
    {
        var fila = await contexto.DatosEmisor.AsNoTracking().SingleOrDefaultAsync(e => e.Id == IdUnico, cancelacion);
        return fila is null ? null : AEntidad(fila);
    }

    /// <summary>
    /// Inserta la fila si no existe o le asigna los cinco valores, sobre la entrada que ya esté en el contexto o leída
    /// con seguimiento. Sobre una fila existente, los cinco quedan marcados siempre como modificados (como en
    /// <see cref="ConfiguracionNegocioRepository"/>): el <c>UPDATE</c> los lleva aunque la entrada reutilizada ya tuviera
    /// esos valores y otro alcance haya grabado otros en el medio. Lo emite <see cref="UnidadDeTrabajoEf"/> dentro de su
    /// transacción; sin token de concurrencia: la última grabación gana.
    /// </summary>
    public void Reemplazar(DatosEmisor datos)
    {
        ArgumentNullException.ThrowIfNull(datos);

        var fila = contexto.DatosEmisor.Local.FindEntry(IdUnico)?.Entity
            ?? contexto.DatosEmisor.SingleOrDefault(e => e.Id == IdUnico);
        var inicio = datos.InicioActividades.ToString(DatosEmisor.FormatoFecha, CultureInfo.InvariantCulture);
        if (fila is null)
        {
            contexto.DatosEmisor.Add(new DatosEmisorFila
            {
                Id = IdUnico,
                RazonSocial = datos.RazonSocial,
                DomicilioComercial = datos.DomicilioComercial,
                Cuit = datos.Cuit.Valor,
                IngresosBrutos = datos.IngresosBrutos,
                InicioActividades = inicio,
            });
            return;
        }

        var entrada = contexto.Entry(fila);
        Asignar(entrada.Property(e => e.RazonSocial), datos.RazonSocial);
        Asignar(entrada.Property(e => e.DomicilioComercial), datos.DomicilioComercial);
        Asignar(entrada.Property(e => e.Cuit), datos.Cuit.Valor);
        Asignar(entrada.Property(e => e.IngresosBrutos), datos.IngresosBrutos);
        Asignar(entrada.Property(e => e.InicioActividades), inicio);
    }

    private static void Asignar(PropertyEntry<DatosEmisorFila, string> propiedad, string valor)
    {
        propiedad.CurrentValue = valor;
        propiedad.IsModified = true;
    }

    /// <summary>
    /// La entidad de una fila, con las reglas de lectura (sin la de la fecha posterior a hoy, D-19). Una fila que no
    /// puede representarse se informa como estado corrupto y no con el <see cref="ArgumentException"/> del dominio, que
    /// significa entrada inválida. Ni el mensaje ni la causa llevan valores.
    /// </summary>
    private static DatosEmisor AEntidad(DatosEmisorFila fila)
    {
        if (!DateOnly.TryParseExact(
                fila.InicioActividades, DatosEmisor.FormatoFecha, CultureInfo.InvariantCulture, DateTimeStyles.None,
                out var inicio))
        {
            throw new InvalidOperationException(MensajeFilaInvalida);
        }

        try
        {
            return DatosEmisor.Leer(fila.RazonSocial, fila.DomicilioComercial, fila.Cuit, fila.IngresosBrutos, inicio);
        }
        catch (ArgumentException error)
        {
            // La causa nombra el campo y no el valor (invariante del dominio), así que se puede adjuntar.
            throw new InvalidOperationException(MensajeFilaInvalida, error);
        }
    }
}
