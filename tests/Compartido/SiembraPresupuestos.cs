using System.Globalization;
using Microsoft.Data.Sqlite;
using Optica.Domain.Catalogo;
using Optica.Domain.Presupuestos;

namespace Optica.Pruebas.Compartido;

/// <summary>
/// Siembra de presupuestos para las pruebas de volumen (NFR-03 de FEAT-003d). Optica.Api.Tests y
/// Optica.Infrastructure.Tests enlazan este archivo y la usan sobre su base temporal. Determinista, en este orden:
/// (1) valida los parámetros sin abrir la conexión; (2) comprueba que no haya presupuestos; (3) siembra 200 artículos
/// activos con <see cref="SiembraCatalogo"/>; (4) lee sus códigos y entidades de la base; (5) arma y valida con el dominio
/// los presupuestos 1..n; (6) recién entonces los inserta en una sola transacción, con SQL parametrizado y preparado,
/// todos en Borrador, y pasa a Final los que corresponden con <c>Version = Version + 1</c>, como la aplicación
/// (AGENTS.md: un Final se graba en Borrador y después pasa a Final; los triggers de cierre no se tocan).
/// </summary>
/// <remarks>
/// Los datos del cliente son ficticios (DNI de la serie 99.999.9xx, email de example.com, teléfono 00 0000-0000). Los
/// mensajes de error son fijos, sin valores, y no adjuntan la excepción del dominio como causa (a diferencia de
/// <see cref="SiembraCatalogo"/>): el dato del cliente podría viajar en ella.
/// </remarks>
public static class SiembraPresupuestos
{
    /// <summary>El volumen de NFR-03: 10.000 presupuestos.</summary>
    public const int Volumen = 10_000;

    /// <summary>Los Finales del volumen: 1.000 (los 9.000 restantes quedan en Borrador).</summary>
    public const int FinalesDelVolumen = 1_000;

    /// <summary>Los artículos activos que se siembran para las líneas.</summary>
    public const int ArticulosSembrados = 200;

    /// <summary>El máximo de líneas por presupuesto que admite la siembra.</summary>
    public const int LineasMaximas = 5;

    private const string Prefijo = "Siembra de presupuestos: ";
    private const string NadaInsertado = " No se insertó ningún presupuesto.";

    /// <summary>Algún parámetro está fuera de rango; falla antes de abrir la conexión.</summary>
    public const string MensajeParametros = Prefijo + "los parámetros no son válidos." + NadaInsertado;

    /// <summary>La base ya tiene presupuestos; falla antes de sembrar artículos.</summary>
    public const string MensajeBaseConPresupuestos = Prefijo + "la base ya tiene presupuestos." + NadaInsertado;

    /// <summary>Los artículos sembrados no se pudieron leer de la base.</summary>
    public const string MensajeArticulos = Prefijo + "no se leyeron los artículos sembrados." + NadaInsertado;

    /// <summary>El dominio rechazó un presupuesto o una línea; falla antes de insertar presupuestos.</summary>
    public const string MensajeDatoRechazado = Prefijo + "el dominio rechazó un presupuesto o una línea." + NadaInsertado;

    /// <summary>El paso a Final no cambió exactamente una fila; la transacción se revierte.</summary>
    public const string MensajeFinalNoAplicado = Prefijo + "un paso a Final no cambió una sola fila." + NadaInsertado;

    private const string TelefonoFicticio = "00 0000-0000";

    /// <summary>La fecha de los presupuestos sembrados, si no se pide otra.</summary>
    public static readonly DateOnly FechaSembrada = new(2026, 10, 1);

    /// <summary>
    /// Si el presupuesto <paramref name="numero"/> de una siembra de <paramref name="cantidad"/> con
    /// <paramref name="finales"/> Finales queda en Final: repartidos parejo (con 10.000 y 1.000, los múltiplos de 10).
    /// </summary>
    public static bool EsFinal(long numero, int cantidad, int finales) =>
        cantidad > 0 && numero * finales / cantidad > (numero - 1) * finales / cantidad;

    /// <summary>Las líneas del presupuesto <paramref name="numero"/>: de 1 a <paramref name="lineasMaximas"/>, en ciclo.</summary>
    public static int LineasDe(long numero, int lineasMaximas) => 1 + (int)((numero - 1) % lineasMaximas);

    /// <summary>
    /// Siembra <paramref name="cantidad"/> presupuestos numerados 1..n, <paramref name="finales"/> de ellos en Final, con
    /// 1 a <paramref name="lineasMaximas"/> líneas de los 200 artículos activos que siembra antes.
    /// </summary>
    /// <param name="fecha">La fecha de los presupuestos; null es <see cref="FechaSembrada"/>. La valida el dominio.</param>
    /// <exception cref="InvalidOperationException">
    /// Un parámetro fuera de rango, una base con presupuestos, un dato que el dominio rechaza o un paso a Final que no
    /// cambia una fila. El mensaje es fijo y no se inserta ningún presupuesto.
    /// </exception>
    public static void Sembrar(
        string cadenaConexion, int cantidad = Volumen, int finales = FinalesDelVolumen, int lineasMaximas = LineasMaximas,
        DateOnly? fecha = null)
    {
        // (1) Los parámetros, sin abrir la conexión.
        ArgumentNullException.ThrowIfNull(cadenaConexion);
        if (cantidad is < 1 or > Volumen || finales < 0 || finales > cantidad || lineasMaximas is < 1 or > LineasMaximas)
        {
            throw new InvalidOperationException(MensajeParametros);
        }

        using var conexion = new SqliteConnection(cadenaConexion);
        conexion.Open();

        // (2) La base de presupuestos vacía, antes de sembrar artículos.
        using (var contar = conexion.CreateCommand())
        {
            contar.CommandText = "SELECT EXISTS (SELECT 1 FROM Presupuesto)";
            if ((long)contar.ExecuteScalar()! != 0)
            {
                throw new InvalidOperationException(MensajeBaseConPresupuestos);
            }
        }

        // (3) Los artículos, todos activos: Generar deja inactivo uno de cada diez.
        var sembrados = SiembraCatalogo.Generar(ArticulosSembrados).Select(a => a with { Activo = true }).ToList();
        SiembraCatalogo.Sembrar(cadenaConexion, sembrados);

        // (4) Sus códigos y entidades, leídos de la base: no se suponen.
        var articulos = LeerArticulos(conexion, sembrados);

        // (5) Los presupuestos armados y validados por el dominio, antes de insertar ninguno.
        var presupuestos = Armar(articulos, cantidad, finales, lineasMaximas, fecha ?? FechaSembrada);

        // (6) La inserción, en una sola transacción: todos en Borrador y después el paso a Final.
        Insertar(conexion, presupuestos, cantidad, finales);
    }

    private static List<Articulo> LeerArticulos(SqliteConnection conexion, IReadOnlyList<ArticuloSembrado> sembrados)
    {
        using var comando = conexion.CreateCommand();
        comando.CommandText =
            "SELECT Id, CodigoProveedor, Descripcion, PrecioCostoCentavos, MargenCentesimos, PrecioVentaCentavos, Activo, " +
            "Version FROM Articulo WHERE CodigoProveedorClave = $clave";
        var clave = comando.Parameters.Add("$clave", SqliteType.Text);
        comando.Prepare();

        var articulos = new List<Articulo>(sembrados.Count);
        foreach (var sembrado in sembrados)
        {
            clave.Value = NormalizacionCodigo.Clave(sembrado.CodigoProveedor);
            using var lector = comando.ExecuteReader();
            if (!lector.Read())
            {
                throw new InvalidOperationException(MensajeArticulos);
            }

            try
            {
                articulos.Add(new Articulo(
                    lector.GetInt64(0), lector.GetString(1), lector.GetString(2), lector.GetInt64(3) / 100m,
                    lector.GetInt64(4) / 100m, lector.GetInt64(5) / 100m, lector.GetInt64(6) != 0, lector.GetInt64(7)));
            }
            catch (ArgumentException)
            {
                // Sin la causa, como los demás rechazos del dominio.
                throw new InvalidOperationException(MensajeDatoRechazado);
            }
        }

        return articulos;
    }

    /// <summary>
    /// Los presupuestos 1..n en Borrador, deterministas: cliente ficticio y de 1 a <paramref name="lineasMaximas"/> líneas
    /// con artículos distintos, cantidad de 1 a 3 y descuento de 0, 5, 10 o 15 %. Los que van a Final pasan además por
    /// <see cref="Presupuesto.PasarAFinal"/>.
    /// </summary>
    private static List<Presupuesto> Armar(
        IReadOnlyList<Articulo> articulos, int cantidad, int finales, int lineasMaximas, DateOnly fecha)
    {
        var presupuestos = new List<Presupuesto>(cantidad);
        try
        {
            for (var numero = 1L; numero <= cantidad; numero++)
            {
                var cliente = new ClientePresupuesto(
                    "Ficticio",
                    string.Create(CultureInfo.InvariantCulture, $"Sembrado {numero:D5}"),
                    string.Create(CultureInfo.InvariantCulture, $"999999{numero % 100:D2}"),
                    null,
                    string.Create(CultureInfo.InvariantCulture, $"cliente{numero:D5}@example.com"),
                    TelefonoFicticio);

                var lineas = new List<LineaPresupuesto>(lineasMaximas);
                for (var j = 0; j < LineasDe(numero, lineasMaximas); j++)
                {
                    var articulo = articulos[(int)(((numero - 1) * 7 + (j * 13)) % articulos.Count)];
                    var resultado = LineaPresupuesto.DesdeArticulo(
                        articulo, cantidad: 1 + (int)((numero + j) % 3), porcentajeDescuento: (numero + j) % 4 * 5m);
                    lineas.Add(resultado.Linea ?? throw new InvalidOperationException(MensajeDatoRechazado));
                }

                var presupuesto = Presupuesto.Nuevo(numero, fecha, cliente, lineas);
                if (EsFinal(numero, cantidad, finales))
                {
                    // El dominio acepta el paso a Final; la inserción lo repite como la app, sobre el Borrador grabado.
                    _ = presupuesto.PasarAFinal();
                }

                presupuestos.Add(presupuesto);
            }
        }
        catch (ArgumentException)
        {
            // Sin la causa: el dato del cliente podría viajar en ella.
            throw new InvalidOperationException(MensajeDatoRechazado);
        }

        return presupuestos;
    }

    private static void Insertar(SqliteConnection conexion, IReadOnlyList<Presupuesto> presupuestos, int cantidad, int finales)
    {
        using var transaccion = conexion.BeginTransaction();

        using var encabezado = conexion.CreateCommand();
        encabezado.Transaction = transaccion;
        encabezado.CommandText =
            "INSERT INTO Presupuesto (Numero, Fecha, Estado, Apellido, Nombre, Dni, Domicilio, Email, Telefono, Version) " +
            "VALUES ($numero, $fecha, 'borrador', $apellido, $nombre, $dni, $domicilio, $email, $telefono, $version)";
        var numero = encabezado.Parameters.Add("$numero", SqliteType.Integer);
        var fecha = encabezado.Parameters.Add("$fecha", SqliteType.Text);
        var apellido = encabezado.Parameters.Add("$apellido", SqliteType.Text);
        var nombre = encabezado.Parameters.Add("$nombre", SqliteType.Text);
        var dni = encabezado.Parameters.Add("$dni", SqliteType.Text);
        var domicilio = encabezado.Parameters.Add("$domicilio", SqliteType.Text);
        var email = encabezado.Parameters.Add("$email", SqliteType.Text);
        var telefono = encabezado.Parameters.Add("$telefono", SqliteType.Text);
        var version = encabezado.Parameters.Add("$version", SqliteType.Integer);
        encabezado.Prepare();

        using var linea = conexion.CreateCommand();
        linea.Transaction = transaccion;
        linea.CommandText =
            "INSERT INTO LineaPresupuesto (PresupuestoNumero, Orden, CodigoArticulo, Descripcion, PrecioUnitarioCentavos, " +
            "PrecioConDescuentoCentavos, PrecioFinalCentavos, Cantidad, DescuentoCentesimos) " +
            "VALUES ($numero, $orden, $codigo, $descripcion, $unitario, $conDescuento, $final, $cantidad, $descuento)";
        var lineaNumero = linea.Parameters.Add("$numero", SqliteType.Integer);
        var orden = linea.Parameters.Add("$orden", SqliteType.Integer);
        var codigo = linea.Parameters.Add("$codigo", SqliteType.Integer);
        var descripcion = linea.Parameters.Add("$descripcion", SqliteType.Text);
        var unitario = linea.Parameters.Add("$unitario", SqliteType.Integer);
        var conDescuento = linea.Parameters.Add("$conDescuento", SqliteType.Integer);
        var precioFinal = linea.Parameters.Add("$final", SqliteType.Integer);
        var cantidadLinea = linea.Parameters.Add("$cantidad", SqliteType.Integer);
        var descuento = linea.Parameters.Add("$descuento", SqliteType.Integer);
        linea.Prepare();

        // Como la aplicación (PresupuestoRepository.CambiarEstado): solo Estado y Version, sobre un Borrador.
        using var aFinal = conexion.CreateCommand();
        aFinal.Transaction = transaccion;
        aFinal.CommandText =
            "UPDATE Presupuesto SET Estado = 'final', Version = Version + 1 WHERE Numero = $numero AND Estado = 'borrador'";
        var finalNumero = aFinal.Parameters.Add("$numero", SqliteType.Integer);
        aFinal.Prepare();

        foreach (var presupuesto in presupuestos)
        {
            var cliente = presupuesto.Cliente;
            numero.Value = presupuesto.Numero;
            fecha.Value = presupuesto.Fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            apellido.Value = cliente.Apellido;
            nombre.Value = cliente.Nombre;
            dni.Value = cliente.Dni;
            domicilio.Value = (object?)cliente.Domicilio ?? DBNull.Value;
            email.Value = (object?)cliente.Email ?? DBNull.Value;
            telefono.Value = (object?)cliente.Telefono ?? DBNull.Value;
            version.Value = presupuesto.Version;
            encabezado.ExecuteNonQuery();

            for (var i = 0; i < presupuesto.Lineas.Count; i++)
            {
                var datos = presupuesto.Lineas[i];
                lineaNumero.Value = presupuesto.Numero;
                orden.Value = i + 1L;
                codigo.Value = datos.CodigoArticulo;
                descripcion.Value = datos.Descripcion;
                unitario.Value = SiembraCatalogo.Centesimos(datos.PrecioUnitario);
                conDescuento.Value = SiembraCatalogo.Centesimos(datos.PrecioConDescuento);
                precioFinal.Value = SiembraCatalogo.Centesimos(datos.PrecioFinal);
                cantidadLinea.Value = (long)datos.Cantidad;
                descuento.Value = SiembraCatalogo.Centesimos(datos.PorcentajeDescuento);
                linea.ExecuteNonQuery();
            }
        }

        foreach (var presupuesto in presupuestos.Where(p => EsFinal(p.Numero, cantidad, finales)))
        {
            finalNumero.Value = presupuesto.Numero;
            if (aFinal.ExecuteNonQuery() != 1)
            {
                // Sin confirmar: la transacción se revierte al descartarse.
                throw new InvalidOperationException(MensajeFinalNoAplicado);
            }
        }

        transaccion.Commit();
    }
}
