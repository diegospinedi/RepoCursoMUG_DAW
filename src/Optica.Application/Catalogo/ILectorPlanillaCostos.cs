using Optica.Domain.Catalogo;

namespace Optica.Application.Catalogo;

/// <summary>
/// Lee la planilla de costos .xlsx (FR-02 y FR-08 de FEAT-011). La implementación vive en Infrastructure (ADR-031); la
/// aplicación no depende de la biblioteca de planillas. El lector solo decide "no es .xlsx" y los límites técnicos; el
/// formato, los precios y los códigos los decide <see cref="PlanillaCostos"/> (D-07).
/// </summary>
public interface ILectorPlanillaCostos
{
    /// <summary>
    /// Lee la primera hoja del libro (D-05). Nunca lanza por el contenido del archivo: un archivo que no es un .xlsx
    /// legible devuelve <see cref="LecturaPlanilla.NoEsXlsx"/> y uno que pasa los límites de D-14,
    /// <see cref="LecturaPlanilla.ExcedeLimites"/>.
    /// </summary>
    /// <param name="archivo">Los bytes del archivo tal como llegaron.</param>
    LecturaPlanilla Leer(ReadOnlyMemory<byte> archivo);
}

/// <summary>
/// El resultado de <see cref="ILectorPlanillaCostos.Leer"/>: <see cref="Leida"/> con el contenido tipado,
/// <see cref="NoEsXlsx"/> o <see cref="ExcedeLimites"/>. Los rechazos no llevan mensaje ni causa (D-17, R-05): nada del
/// archivo puede llegar al log por ellos.
/// </summary>
public abstract record LecturaPlanilla
{
    private LecturaPlanilla()
    {
    }

    /// <summary>El archivo es un .xlsx y se leyó dentro de los límites.</summary>
    /// <param name="Contenido">Los encabezados, la marca de columnas de más y las filas con datos.</param>
    public sealed record Leida(ContenidoPlanilla Contenido) : LecturaPlanilla
    {
        /// <summary>Lo que el lector encontró en la primera hoja; nunca null.</summary>
        public ContenidoPlanilla Contenido { get; } =
            Contenido ?? throw new ArgumentNullException(nameof(Contenido));
    }

    /// <summary>
    /// El archivo no es un .xlsx legible: otro formato (.xls, .csv, PDF), un ZIP sin libro o sin hojas, XML mal formado
    /// o con DTD, o una parte más grande que el tope de caracteres (D-17, R-02, R-03, R-09).
    /// </summary>
    public sealed record NoEsXlsx : LecturaPlanilla
    {
        /// <summary>La única instancia: el rechazo no lleva datos.</summary>
        public static NoEsXlsx Instancia { get; } = new();
    }

    /// <summary>
    /// La planilla pasa un límite de D-14: más de 50.000 filas con datos, una fila con datos después de la 50.001 o una
    /// celda de más de 65.536 caracteres. Se rechaza completa (R-02).
    /// </summary>
    public sealed record ExcedeLimites : LecturaPlanilla
    {
        /// <summary>La única instancia: el rechazo no lleva datos.</summary>
        public static ExcedeLimites Instancia { get; } = new();
    }
}
