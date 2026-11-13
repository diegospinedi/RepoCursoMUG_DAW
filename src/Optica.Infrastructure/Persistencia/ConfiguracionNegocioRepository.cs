using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Optica.Domain.Configuracion;

namespace Optica.Infrastructure.Persistencia;

/// <summary>
/// Configuración de negocio sobre la tabla <c>ConfiguracionNegocio</c> (fila única con <c>Id = 1</c>, insertada por la
/// migración). Mapea la fila a la entidad de dominio sin aritmética: la conversión a centésimos vive en
/// <see cref="OpticaDbContext"/> (ADR-008). <see cref="Reemplazar"/> solo deja el cambio en el contexto compartido, que
/// confirma <see cref="UnidadDeTrabajoEf"/>.
/// </summary>
public sealed class ConfiguracionNegocioRepository(OpticaDbContext contexto) : IConfiguracionNegocioRepository
{
    /// <summary>Clave de la única fila permitida por <c>CHECK (Id = 1)</c>.</summary>
    public const int IdUnico = 1;

    private const string MensajeSinFila = "No hay configuración de negocio.";
    private const string MensajeFilaInvalida = "La configuración de negocio guardada no es válida.";
    private const string MensajeAlicuotaNoAdmitida = "La alícuota de IVA a grabar no es una de las que acepta ARCA.";

    /// <summary>Lee la fila sin seguimiento en cada llamada, sin caché (FR-06).</summary>
    /// <exception cref="InvalidOperationException">
    /// No hay fila, o la fila no cumple la política (editada a mano con los CHECK desactivados): estado corrupto (D-04).
    /// </exception>
    public async Task<ConfiguracionNegocio> ObtenerAsync(CancellationToken cancelacion = default)
    {
        var fila = await contexto.ConfiguracionesNegocio.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == IdUnico, cancelacion);
        return fila is null ? throw new InvalidOperationException(MensajeSinFila) : AEntidad(fila);
    }

    /// <summary>
    /// Lee la fila con seguimiento (o reutiliza la entrada que ya esté en el contexto) y le asigna los cinco valores,
    /// marcados siempre como modificados: el <c>UPDATE</c> lleva los cinco aunque la entrada reutilizada ya tuviera esos
    /// valores y otro alcance haya grabado otros en el medio. Lo emite <see cref="UnidadDeTrabajoEf"/> dentro de su
    /// transacción. Sin <c>ExecuteUpdate</c>, que se ejecutaría fuera de ella, y sin token de concurrencia: la última
    /// grabación gana (D-03).
    /// </summary>
    /// <remarks>
    /// La regla de las 6 alícuotas de ARCA es invariante de escritura por cualquier camino (D-33 de FEAT-007): una
    /// configuración de lectura con otra alícuota (D-06) no se graba, aunque no haya pasado por la validación del caso de
    /// uso. Se controla antes que la fila faltante.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// La alícuota no es una de las que acepta ARCA. El mensaje no incluye el valor; no se registra ningún cambio.
    /// </exception>
    /// <exception cref="InvalidOperationException">No hay fila: estado corrupto (D-04).</exception>
    public void Reemplazar(ConfiguracionNegocio nueva)
    {
        ArgumentNullException.ThrowIfNull(nueva);
        if (!nueva.AlicuotaAdmitidaPorArca)
        {
            throw new ArgumentException(MensajeAlicuotaNoAdmitida, nameof(nueva));
        }

        var fila = contexto.ConfiguracionesNegocio.Local.FindEntry(IdUnico)?.Entity
            ?? contexto.ConfiguracionesNegocio.SingleOrDefault(c => c.Id == IdUnico)
            ?? throw new InvalidOperationException(MensajeSinFila);

        var entrada = contexto.Entry(fila);
        Asignar(entrada.Property(c => c.AlicuotaIva), nueva.AlicuotaIva);
        Asignar(entrada.Property(c => c.CondicionFiscal), PoliticaConfiguracionNegocio.TextoDe(nueva.CondicionFiscal));
        Asignar(entrada.Property(c => c.MultiploRedondeo), nueva.MultiploRedondeo);
        Asignar(entrada.Property(c => c.TopeIdentificacion), nueva.TopeIdentificacion);
        Asignar(entrada.Property(c => c.PuntoVenta), nueva.PuntoVenta);
    }

    private static void Asignar<T>(PropertyEntry<ConfiguracionNegocioFila, T> propiedad, T valor)
    {
        propiedad.CurrentValue = valor;
        propiedad.IsModified = true;
    }

    /// <summary>
    /// La entidad de una fila, con las invariantes de lectura: una alícuota de 0 a 100 que ARCA no acepta se lee igual
    /// (D-06 de FEAT-007). Una fila que eludió los CHECK no puede representarse: se informa como estado corrupto y no
    /// con el <see cref="ArgumentException"/> del dominio, que significa entrada inválida. El mensaje no lleva valores.
    /// </summary>
    private static ConfiguracionNegocio AEntidad(ConfiguracionNegocioFila fila)
    {
        var condicion = PoliticaConfiguracionNegocio.LeerCondicion(fila.CondicionFiscal)
            ?? throw new InvalidOperationException(MensajeFilaInvalida);
        try
        {
            return new ConfiguracionNegocio(
                fila.AlicuotaIva, condicion, fila.MultiploRedondeo, fila.TopeIdentificacion, fila.PuntoVenta);
        }
        catch (ArgumentException error)
        {
            // La causa nombra el campo y no el valor (invariante del dominio), así que se puede adjuntar.
            throw new InvalidOperationException(MensajeFilaInvalida, error);
        }
    }
}
