using System.Globalization;
using Microsoft.Data.Sqlite;
using Optica.Domain.Catalogo;
using Optica.Domain.Configuracion;

namespace Optica.Pruebas.Compartido;

/// <summary>Un artículo a sembrar, con sus importes en pesos y su estado.</summary>
public sealed record ArticuloSembrado(
    string CodigoProveedor, string Descripcion, decimal PrecioCosto, decimal MargenUtilidad, decimal PrecioVenta,
    bool Activo);

/// <summary>
/// Siembra del catálogo para las pruebas de volumen (NFR-01, D-15). Optica.Api.Tests y Optica.Infrastructure.Tests
/// enlazan este archivo y la usan sobre su base temporal. Inserta con SQL parametrizado, en una sola transacción, con los
/// importes y el margen en centésimos (ADR-008) y la clave que calcula el dominio
/// (<see cref="Articulo.CodigoProveedorClave"/>, con <see cref="NormalizacionCodigo.Clave"/>): así ninguna fila se lee
/// como estado corrupto. Los datos son ficticios.
/// </summary>
public static class SiembraCatalogo
{
    /// <summary>El volumen de NFR-01.</summary>
    public const int Volumen = 10_000;

    private const string Prefijo = "Siembra del catálogo: ";
    private const string NadaInsertado = " No se insertó ningún artículo.";

    /// <summary>Uno de cada cuatro artículos es de cada tipo, en este orden: el 1, 5, 9… son "Armazón acetato".</summary>
    private static readonly string[] Tipos = ["Armazón acetato", "Cristal orgánico", "Estuche rígido", "Lente de contacto"];

    /// <summary>
    /// <paramref name="cantidad"/> artículos válidos y deterministas: el n-ésimo tiene código en el proveedor
    /// <c>VOL-nnnnn</c> y descripción "<c>tipo nnnnn</c>"; uno de cada diez (los múltiplos de 10) está inactivo. El
    /// primero tiene costo 1.210 y margen 50 (AC-02); los demás, costo n + 100,37 y margen 10 + n mod 90, con el costo
    /// corrido de a un centavo si el precio cayera justo en un múltiplo de 50 (así el recálculo con múltiplo 50 cambia
    /// los 10.000). El precio de venta es el de la configuración inicial de la base
    /// (alícuota 21, Responsable Inscripto, múltiplo 0,01).
    /// </summary>
    public static IReadOnlyList<ArticuloSembrado> Generar(int cantidad = Volumen)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cantidad);
        var inicial = new ConfiguracionNegocio(21m, CondicionFiscal.ResponsableInscripto, 0.01m, null, puntoVenta: null);
        var articulos = new List<ArticuloSembrado>(cantidad);
        for (var i = 1; i <= cantidad; i++)
        {
            var costo = i == 1 ? 1210m : 100.37m + i;
            var margen = i == 1 ? 50m : 10m + (i % 90);
            var venta = CalculadoraPrecioVenta.Calcular(costo, margen, inicial).Precio!.PrecioVenta;
            while (venta % 50m == 0m)
            {
                // Con múltiplo 50 todos los precios tienen que cambiar: se corre el costo un centavo.
                costo += 0.01m;
                venta = CalculadoraPrecioVenta.Calcular(costo, margen, inicial).Precio!.PrecioVenta;
            }

            articulos.Add(new ArticuloSembrado(
                string.Create(CultureInfo.InvariantCulture, $"VOL-{i:D5}"),
                string.Create(CultureInfo.InvariantCulture, $"{Tipos[(i - 1) % Tipos.Length]} {i:D5}"),
                costo, margen, venta, Activo: i % 10 != 0));
        }

        return articulos;
    }

    /// <summary>
    /// Valida todos los artículos con el dominio y después los inserta en una sola transacción. Un dato inválido o una
    /// clave repetida lanzan antes de abrir la conexión, con un mensaje propio que nombra la posición (desde 1) y no los
    /// valores: la prueba falla en la siembra, antes de medir.
    /// </summary>
    /// <exception cref="InvalidOperationException">Algún artículo no es válido o repite la clave de otro.</exception>
    public static void Sembrar(string cadenaConexion, IReadOnlyList<ArticuloSembrado> articulos)
    {
        ArgumentNullException.ThrowIfNull(cadenaConexion);
        ArgumentNullException.ThrowIfNull(articulos);

        var validados = new List<Articulo>(articulos.Count);
        var claves = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < articulos.Count; i++)
        {
            var articulo = Validar(articulos[i], i + 1);
            if (!claves.Add(articulo.CodigoProveedorClave))
            {
                throw new InvalidOperationException(
                    string.Create(CultureInfo.InvariantCulture, $"{Prefijo}el artículo {i + 1} repite la clave de otro.")
                    + NadaInsertado);
            }

            validados.Add(articulo);
        }

        using var conexion = new SqliteConnection(cadenaConexion);
        conexion.Open();
        using var transaccion = conexion.BeginTransaction();
        using var comando = conexion.CreateCommand();
        comando.Transaction = transaccion;
        comando.CommandText =
            "INSERT INTO Articulo (CodigoProveedor, CodigoProveedorClave, Descripcion, PrecioCostoCentavos, " +
            "MargenCentesimos, PrecioVentaCentavos, Activo, Version) " +
            "VALUES ($codigo, $clave, $descripcion, $costo, $margen, $venta, $activo, 0)";
        var codigo = comando.Parameters.Add("$codigo", SqliteType.Text);
        var clave = comando.Parameters.Add("$clave", SqliteType.Text);
        var descripcion = comando.Parameters.Add("$descripcion", SqliteType.Text);
        var costo = comando.Parameters.Add("$costo", SqliteType.Integer);
        var margen = comando.Parameters.Add("$margen", SqliteType.Integer);
        var venta = comando.Parameters.Add("$venta", SqliteType.Integer);
        var activo = comando.Parameters.Add("$activo", SqliteType.Integer);
        comando.Prepare();

        foreach (var articulo in validados)
        {
            codigo.Value = articulo.CodigoProveedor;
            // Una sola fuente para la clave: la que calculó el dominio al validar (D-15).
            clave.Value = articulo.CodigoProveedorClave;
            descripcion.Value = articulo.Descripcion;
            costo.Value = Centesimos(articulo.PrecioCosto);
            margen.Value = Centesimos(articulo.MargenUtilidad);
            venta.Value = Centesimos(articulo.PrecioVenta);
            activo.Value = articulo.Activo ? 1L : 0L;
            comando.ExecuteNonQuery();
        }

        transaccion.Commit();
    }

    /// <summary>
    /// Un importe o un margen en centésimos, como se guarda en la tabla (ADR-008). Precondición: hasta 2 decimales, que el
    /// dominio ya garantiza para los datos de un artículo.
    /// </summary>
    public static long Centesimos(decimal valor) => decimal.ToInt64(valor * 100m);

    /// <summary>La entidad del dominio, que aplica la política; si la rechaza, el error nombra el campo, no el valor.</summary>
    private static Articulo Validar(ArticuloSembrado sembrado, int posicion)
    {
        ArgumentNullException.ThrowIfNull(sembrado);
        try
        {
            return new Articulo(
                null, sembrado.CodigoProveedor, sembrado.Descripcion, sembrado.PrecioCosto, sembrado.MargenUtilidad,
                sembrado.PrecioVenta, sembrado.Activo, version: 0);
        }
        catch (ArgumentException error)
        {
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture, $"{Prefijo}el artículo {posicion} no es válido ({error.ParamName}).")
                + NadaInsertado,
                error);
        }
    }
}
