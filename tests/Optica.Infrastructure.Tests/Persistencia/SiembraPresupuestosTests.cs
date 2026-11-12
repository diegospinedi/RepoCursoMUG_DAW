using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Optica.Domain.Presupuestos;
using Optica.Infrastructure.Persistencia;
using Optica.Pruebas.Compartido;

namespace Optica.Infrastructure.Tests.Persistencia;

/// <summary>
/// El sembrador de presupuestos de las pruebas de volumen (NFR-03 de FEAT-003d): numera 1..n, graba todo en Borrador y
/// pasa a Final con la versión siguiente, como la aplicación, sin tocar los triggers de cierre (AGENTS.md). Una siembra
/// inválida falla con un mensaje fijo y sin insertar ningún presupuesto.
/// </summary>
public class SiembraPresupuestosTests
{
    /// <summary>Los seis triggers de cierre de la migración CierrePresupuestos.</summary>
    private static readonly string[] TriggersDeCierre =
    [
        "TR_LineaPresupuesto_FinalDelete",
        "TR_LineaPresupuesto_FinalInsert",
        "TR_LineaPresupuesto_FinalUpdate",
        "TR_Presupuesto_FechaYNumeroFijos",
        "TR_Presupuesto_FinalInmutable",
        "TR_Presupuesto_FinalNoSeBorra",
    ];

    /// <summary>
    /// Un parámetro fuera de rango falla antes de abrir la conexión (la cadena apunta a una base que no se puede abrir);
    /// una base que ya tiene presupuestos falla antes de sembrar artículos; un dato que el dominio rechaza (la fecha por
    /// defecto) falla antes de insertar presupuestos. Siempre con un mensaje fijo, sin valores ni excepción adjunta.
    /// </summary>
    [Theory]
    [InlineData("cantidad0")]
    [InlineData("cantidad10001")]
    [InlineData("finalesNegativos")]
    [InlineData("finalesMasQueCantidad")]
    [InlineData("lineas0")]
    [InlineData("lineas6")]
    [InlineData("baseConPresupuestos")]
    [InlineData("datoRechazadoPorElDominio")]
    public void Siembra_ParametroInvalido_FallaSinInsertar(string caso)
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);

        // Una base que no se puede abrir: si el sembrador abriera la conexión, fallaría con SqliteException.
        var inaccesible = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(baseTemporal.Directorio, "no-existe", "ninguna.db"),
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString();

        string esperado;
        InvalidOperationException error;
        switch (caso)
        {
            case "baseConPresupuestos":
                SiembraPresupuestos.Sembrar(cadena, cantidad: 3, finales: 1, lineasMaximas: 2);
                var articulosAntes = Contar(cadena, "SELECT count(*) FROM Articulo");
                error = Assert.Throws<InvalidOperationException>(
                    () => SiembraPresupuestos.Sembrar(cadena, cantidad: 5, finales: 1, lineasMaximas: 2));

                // Ni artículos nuevos ni presupuestos nuevos: falló antes de sembrar el catálogo.
                Assert.Equal(articulosAntes, Contar(cadena, "SELECT count(*) FROM Articulo"));
                Assert.Equal(3L, Contar(cadena, "SELECT count(*) FROM Presupuesto"));
                esperado = SiembraPresupuestos.MensajeBaseConPresupuestos;
                break;

            case "datoRechazadoPorElDominio":
                error = Assert.Throws<InvalidOperationException>(
                    () => SiembraPresupuestos.Sembrar(cadena, cantidad: 5, finales: 1, lineasMaximas: 2, fecha: default(DateOnly)));

                // Los artículos quedan en la base temporal; ningún presupuesto ni línea.
                Assert.Equal((long)SiembraPresupuestos.ArticulosSembrados, Contar(cadena, "SELECT count(*) FROM Articulo"));
                Assert.Equal(0L, Contar(cadena, "SELECT count(*) FROM Presupuesto"));
                Assert.Equal(0L, Contar(cadena, "SELECT count(*) FROM LineaPresupuesto"));
                esperado = SiembraPresupuestos.MensajeDatoRechazado;
                break;

            default:
                var (cantidad, finales, lineas) = caso switch
                {
                    "cantidad0" => (0, 0, 1),
                    "cantidad10001" => (10_001, 0, 1),
                    "finalesNegativos" => (10, -1, 1),
                    "finalesMasQueCantidad" => (10, 11, 1),
                    "lineas0" => (10, 1, 0),
                    _ => (10, 1, 6),
                };
                error = Assert.Throws<InvalidOperationException>(
                    () => SiembraPresupuestos.Sembrar(inaccesible, cantidad, finales, lineas));
                esperado = SiembraPresupuestos.MensajeParametros;
                Assert.Equal(0L, Contar(cadena, "SELECT count(*) FROM Presupuesto"));
                break;
        }

        // Mensaje fijo, sin valores (ningún dígito) y sin la excepción del dominio como causa.
        Assert.Equal(esperado, error.Message);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain(error.Message, char.IsDigit);
    }

    /// <summary>
    /// Los presupuestos sembrados se numeran 1..n con 1 a 5 líneas de orden 1..n; los Finales tienen versión 1 (pasaron
    /// de Borrador a Final como en la app) y los Borradores, versión 0; los seis triggers de cierre siguen en el esquema
    /// y uno de ellos rechaza una línea nueva sobre un Final sembrado. El repositorio los lee sin estado corrupto.
    /// </summary>
    [Fact]
    public async Task Siembra_FinalesComoEnLaApp_TriggersIntactos()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        const int cantidad = 40;
        const int finales = 8;

        SiembraPresupuestos.Sembrar(cadena, cantidad, finales, lineasMaximas: 5);

        var filas = Filas(cadena, "SELECT Numero, Estado, Version, Fecha, Dni, Email, Telefono FROM Presupuesto ORDER BY Numero");
        Assert.Equal(
            Enumerable.Range(1, cantidad).Select(n => n.ToString(CultureInfo.InvariantCulture)),
            filas.Select(f => f[0]));
        var finalesSembrados = filas.Where(f => f[1] == "final").Select(f => long.Parse(f[0], CultureInfo.InvariantCulture)).ToList();
        Assert.Equal(finales, finalesSembrados.Count);
        Assert.All(filas, f =>
        {
            var esFinal = SiembraPresupuestos.EsFinal(long.Parse(f[0], CultureInfo.InvariantCulture), cantidad, finales);
            Assert.Equal(esFinal ? "final" : "borrador", f[1]);
            Assert.Equal(esFinal ? "1" : "0", f[2]);
        });

        // Datos ficticios: fecha fija, DNI de la serie 99.999.9xx, email de example.com y teléfono 00 0000-0000.
        Assert.All(filas, f =>
        {
            Assert.Equal(SiembraPresupuestos.FechaSembrada.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), f[3]);
            Assert.StartsWith("999999", f[4], StringComparison.Ordinal);
            Assert.Equal(8, f[4].Length);
            Assert.EndsWith("@example.com", f[5], StringComparison.Ordinal);
            Assert.Equal("00 0000-0000", f[6]);
        });

        // Líneas: de 1 a 5 por presupuesto, con orden 1..n, de artículos sembrados (activos).
        var lineas = Filas(cadena, "SELECT PresupuestoNumero, Orden FROM LineaPresupuesto ORDER BY PresupuestoNumero, Orden");
        foreach (var grupo in lineas.GroupBy(l => long.Parse(l[0], CultureInfo.InvariantCulture)))
        {
            Assert.Equal(SiembraPresupuestos.LineasDe(grupo.Key, 5), grupo.Count());
            Assert.Equal(
                Enumerable.Range(1, grupo.Count()).Select(n => n.ToString(CultureInfo.InvariantCulture)),
                grupo.Select(l => l[1]));
        }

        Assert.Equal(cantidad, lineas.Select(l => l[0]).Distinct().Count());
        Assert.Equal(
            0L,
            Contar(cadena, "SELECT count(*) FROM LineaPresupuesto l JOIN Articulo a ON a.Id = l.CodigoArticulo WHERE a.Activo = 0"));

        // Los seis triggers de cierre siguen en el esquema.
        Assert.Equal(
            TriggersDeCierre,
            Filas(
                    cadena,
                    "SELECT name FROM sqlite_master WHERE type = 'trigger' " +
                    "AND tbl_name IN ('Presupuesto', 'LineaPresupuesto') ORDER BY name")
                .Select(f => f[0]));

        // Un INSERT de línea sobre un Final sembrado es rechazado por el trigger y no deja la línea.
        var final = finalesSembrados[0];
        const string LineasDelPresupuesto = "SELECT count(*) FROM LineaPresupuesto WHERE PresupuestoNumero = $numero";
        var lineasDelFinal = Contar(cadena, LineasDelPresupuesto, final);
        var rechazo = Assert.Throws<SqliteException>(() => Ejecutar(
            cadena,
            "INSERT INTO LineaPresupuesto (PresupuestoNumero, Orden, CodigoArticulo, Descripcion, PrecioUnitarioCentavos, " +
            "PrecioConDescuentoCentavos, PrecioFinalCentavos, Cantidad, DescuentoCentesimos) " +
            "SELECT $numero, 99, Id, Descripcion, 100, 100, 100, 1, 0 FROM Articulo ORDER BY Id LIMIT 1",
            final));
        Assert.Contains("registro_cerrado", rechazo.Message, StringComparison.Ordinal);
        Assert.Equal(lineasDelFinal, Contar(cadena, LineasDelPresupuesto, final));

        // El repositorio los lee como los dejaría la app: un Final con versión 1 y un Borrador con versión 0.
        var contexto = baseTemporal.CrearContexto();
        var repositorio = new PresupuestoRepository(contexto);
        var leidoFinal = await repositorio.ObtenerAsync(final);
        Assert.NotNull(leidoFinal);
        Assert.Equal(EstadoPresupuesto.Final, leidoFinal.Estado);
        Assert.Equal(1L, leidoFinal.Version);
        var borrador = Enumerable.Range(1, cantidad).Select(n => (long)n).First(n => !SiembraPresupuestos.EsFinal(n, cantidad, finales));
        var leidoBorrador = await repositorio.ObtenerAsync(borrador);
        Assert.NotNull(leidoBorrador);
        Assert.Equal(EstadoPresupuesto.Borrador, leidoBorrador.Estado);
        Assert.Equal(0L, leidoBorrador.Version);
        Assert.Equal(cantidad, (await repositorio.ObtenerUltimoNumeroAsync())!.Value);
    }

    private static string CadenaDe(BaseTemporal baseTemporal) =>
        baseTemporal.Servicios.GetRequiredService<FabricaConexion>().CadenaConexion;

    private static long Contar(string cadena, string consulta, long? numero = null)
    {
        using var conexion = new SqliteConnection(cadena);
        conexion.Open();
        using var comando = conexion.CreateCommand();
        comando.CommandText = consulta;
        if (numero is { } valor)
        {
            comando.Parameters.AddWithValue("$numero", valor);
        }

        return (long)comando.ExecuteScalar()!;
    }

    private static void Ejecutar(string cadena, string sentencia, long numero)
    {
        using var conexion = new SqliteConnection(cadena);
        conexion.Open();
        using var comando = conexion.CreateCommand();
        comando.CommandText = sentencia;
        comando.Parameters.AddWithValue("$numero", numero);
        comando.ExecuteNonQuery();
    }

    private static List<string[]> Filas(string cadena, string consulta)
    {
        using var conexion = new SqliteConnection(cadena);
        conexion.Open();
        using var comando = conexion.CreateCommand();
        comando.CommandText = consulta;
        using var lector = comando.ExecuteReader();
        var filas = new List<string[]>();
        while (lector.Read())
        {
            var fila = new string[lector.FieldCount];
            for (var i = 0; i < lector.FieldCount; i++)
            {
                fila[i] = Convert.ToString(lector.GetValue(i), CultureInfo.InvariantCulture) ?? string.Empty;
            }

            filas.Add(fila);
        }

        return filas;
    }
}
