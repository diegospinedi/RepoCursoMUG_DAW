using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Optica.Application.Comun;
using Optica.Application.Presupuestos;
using Optica.Domain.Catalogo;
using Optica.Domain.Presupuestos;
using Optica.Infrastructure.Persistencia;
using Optica.Pruebas.Compartido;

namespace Optica.Infrastructure.Tests.Persistencia;

/// <summary>
/// Dos altas de presupuesto simultáneas sobre la misma base (FR-01, NFR-01 y AC-01 de FEAT-003d): con
/// <c>BEGIN IMMEDIATE</c> (ADR-013) y el tiempo de espera de la cadena, la segunda espera a que la primera confirme y toma
/// el número siguiente. El encuentro se fuerza con <see cref="EncuentroDeAltas"/> (D-04, D-06): la primera queda retenida
/// dentro de su transacción hasta que la conexión de la segunda señala "base ocupada".
/// </summary>
public class ConcurrenciaPresupuestosTests
{
    private const int SqliteOcupada = 5; // SQLITE_BUSY

    /// <summary>El tope de cada encuentro y de la espera de las dos altas.</summary>
    private static readonly TimeSpan Tope = TimeSpan.FromSeconds(30);

    private const string SelectPresupuestos = "SELECT Numero, Apellido FROM Presupuesto ORDER BY Numero";

    private const string SelectLineas = "SELECT PresupuestoNumero, Orden FROM LineaPresupuesto ORDER BY PresupuestoNumero, Orden";

    [Fact]
    public async Task DosAltasSimultaneas_NumerosDistintosYConsecutivos()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var codigo = await GrabarArticuloAsync(baseTemporal);
        var encuentro = new EncuentroDeAltas();

        // Cinco vueltas sobre la misma base: 2, 4, …, 10 presupuestos, cada par con números N y N + 1.
        for (var vuelta = 1; vuelta <= 5; vuelta++)
        {
            var esperadoPrimera = (2L * vuelta) - 1;
            encuentro.Armar(Tope);
            try
            {
                var (primera, segunda) = await DosAltasConEncuentroAsync(baseTemporal, encuentro, codigo);

                // Ninguna SqliteException: las dos se grabaron, con números distintos y consecutivos.
                Assert.True(primera.EsGrabado);
                Assert.True(segunda.EsGrabado);
                Assert.Equal(esperadoPrimera, primera.Presupuesto.Numero);
                Assert.Equal(esperadoPrimera + 1, segunda.Presupuesto.Numero);
                Assert.True(encuentro.BaseOcupada.IsCompletedSuccessfully, $"Vuelta {vuelta}: no hubo encuentro.");
            }
            finally
            {
                encuentro.Desarmar();
            }

            Assert.Equal(
                Enumerable.Range(1, 2 * vuelta).Select(n => n.ToString(CultureInfo.InvariantCulture)),
                Filas(cadena, SelectPresupuestos).Select(f => f[0]));
        }
    }

    [Fact]
    public async Task DosAltasSimultaneas_LaSegundaEsperaLaEscritura()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var codigo = await GrabarArticuloAsync(baseTemporal);
        var encuentro = new EncuentroDeAltas();
        encuentro.Armar(Tope);
        try
        {
            var primeraMarcas = new MarcasDeTransaccion(encuentro);
            var segundaMarcas = new MarcasDeTransaccion(encuentro);
            await using var contextoPrimera = ContextoConEncuentro(baseTemporal, encuentro, "primera", primeraMarcas);
            await using var contextoSegunda = ContextoConEncuentro(baseTemporal, encuentro, "segunda", segundaMarcas);
            var primera = AltaConEncuentro(contextoPrimera, encuentro);
            var segunda = AltaConEncuentro(contextoSegunda, encuentro);

            var tareaPrimera = primera.EjecutarAsync(Pedido("Ficticiez", "99999901", codigo));
            await encuentro.PrimeraRetenida.WaitAsync(Tope);

            // La primera está retenida dentro de su transacción: tiene la escritura y todavía no grabó nada.
            Assert.NotNull(primeraMarcas.Inicio);
            Assert.Null(primeraMarcas.Confirmacion);
            Assert.Empty(Filas(cadena, SelectPresupuestos));

            // La segunda, en otro hilo: su BEGIN IMMEDIATE bloquea el hilo que lo llama mientras reintenta.
            var tareaSegunda = Task.Run(() => segunda.EjecutarAsync(Pedido("Quimerez", "99999902", codigo)));
            await encuentro.BaseOcupada.WaitAsync(Tope);

            var resultados = await Task.WhenAll(tareaPrimera, tareaSegunda).WaitAsync(Tope);

            // La señal salió de la conexión de la segunda, mientras la primera seguía retenida (sin confirmar) y antes de
            // que la segunda empezara su transacción.
            Assert.Equal("segunda", encuentro.OrigenBaseOcupada);
            var senal = Assert.NotNull(encuentro.MarcaBaseOcupada);
            var confirmacionPrimera = Assert.NotNull(primeraMarcas.Confirmacion);
            var inicioSegunda = Assert.NotNull(segundaMarcas.Inicio);
            var confirmacionSegunda = Assert.NotNull(segundaMarcas.Confirmacion);
            Assert.True(primeraMarcas.Inicio < senal, "La primera no tenía la escritura cuando la segunda chocó.");
            Assert.True(senal < confirmacionPrimera, "La señal de base ocupada no precede a la confirmación de la primera.");
            Assert.True(
                confirmacionPrimera < inicioSegunda,
                "La transacción de la segunda empezó antes de que la primera confirmara.");
            Assert.True(inicioSegunda < confirmacionSegunda);

            Assert.Equal(1L, resultados[0].Presupuesto!.Numero);
            Assert.Equal(2L, resultados[1].Presupuesto!.Numero);
            Assert.Equal([["1", "Ficticiez"], ["2", "Quimerez"]], Filas(cadena, SelectPresupuestos));
        }
        finally
        {
            encuentro.Desarmar();
        }
    }

    [Fact]
    public async Task Alta_BaseOcupadaMasQueElTiempo_ErrorDeBloqueoSinGrabar()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var codigo = await GrabarArticuloAsync(baseTemporal);

        // Otra conexión, fuera del grupo, retiene la escritura más que el tiempo de espera del alta.
        using var retenedora = new SqliteConnection(new SqliteConnectionStringBuilder(cadena) { Pooling = false }.ToString());
        retenedora.Open();
        using (var comando = retenedora.CreateCommand())
        {
            comando.CommandText = "BEGIN IMMEDIATE;";
            comando.ExecuteNonQuery();
        }

        await using var contexto = ContextoCon(baseTemporal);
        ((SqliteConnection)contexto.Database.GetDbConnection()).DefaultTimeout = 1;
        var alta = new AltaPresupuesto(
            new PresupuestoRepository(contexto), new ArticuloRepository(contexto), new UnidadDeTrabajoEf(contexto),
            TimeProvider.System);

        var error = await Assert.ThrowsAsync<SqliteException>(
            () => alta.EjecutarAsync(Pedido("Ficticiez", "99999903", codigo)));

        // SQLITE_BUSY sin traducir: la transacción no se abrió y no se grabó nada (W-PRD-04).
        Assert.Equal(SqliteOcupada, error.SqliteErrorCode);
        Assert.Null(contexto.Database.CurrentTransaction);
        using (var comando = retenedora.CreateCommand())
        {
            comando.CommandText = "ROLLBACK;";
            comando.ExecuteNonQuery();
        }

        Assert.Empty(Filas(cadena, SelectPresupuestos));
        Assert.Empty(Filas(cadena, SelectLineas));
    }

    [Fact]
    public async Task Alta_NumeroDuplicado_ConflictoSinEscritura()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var codigo = await GrabarArticuloAsync(baseTemporal);
        await using (var previo = ContextoCon(baseTemporal))
        {
            await AltaSimple(previo).EjecutarAsync(Pedido("Ficticiez", "99999904", codigo));
        }

        await using (var previo = ContextoCon(baseTemporal))
        {
            await AltaSimple(previo).EjecutarAsync(Pedido("Prueboncia", "99999905", codigo));
        }

        var encabezados = Filas(cadena, SelectPresupuestos);
        var lineas = Filas(cadena, SelectLineas);

        // La fila 2 ya está grabada y la lectura vieja del máximo devuelve 1: el alta intenta grabar el 2 otra vez.
        await using var contexto = ContextoCon(baseTemporal);
        var alta = new AltaPresupuesto(
            new UltimoNumeroViejo(new PresupuestoRepository(contexto), ultimoLeido: 1),
            new ArticuloRepository(contexto),
            new UnidadDeTrabajoEf(contexto),
            TimeProvider.System);

        var error = await Assert.ThrowsAsync<ConflictoAlConfirmarException>(
            () => alta.EjecutarAsync(Pedido("Quimerez", "99999906", codigo)));

        // ADR-015: conflicto de unicidad, sin escritura: ni encabezado ni líneas nuevas, y la fila 2 sigue siendo la suya.
        Assert.Equal(MotivoConflicto.Unicidad, error.Motivo);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("Quimerez", error.ToString(), StringComparison.Ordinal);
        Assert.Equal(encabezados, Filas(cadena, SelectPresupuestos));
        Assert.Equal(lineas, Filas(cadena, SelectLineas));
    }

    [Fact]
    public async Task Encuentro_SinSegundaAlta_FallaPorTiempo()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var codigo = await GrabarArticuloAsync(baseTemporal);
        var encuentro = new EncuentroDeAltas();

        // Armar rechaza un tope menor o igual a cero.
        Assert.Throws<ArgumentOutOfRangeException>(() => encuentro.Armar(TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => encuentro.Armar(TimeSpan.FromSeconds(-1)));

        encuentro.Armar(TimeSpan.FromSeconds(1));
        try
        {
            await using var contexto = ContextoConEncuentro(
                baseTemporal, encuentro, "primera", new MarcasDeTransaccion(encuentro));
            var alta = AltaConEncuentro(contexto, encuentro);

            // Sin segunda alta no hay base ocupada: la retenida falla por el tope, con el mensaje fijo, sin colgarse.
            var error = await Assert.ThrowsAsync<TimeoutException>(
                () => alta.EjecutarAsync(Pedido("Ficticiez", "99999907", codigo)).WaitAsync(Tope));

            Assert.Equal(EncuentroDeAltas.MensajeTopeVencido, error.Message);
            Assert.False(encuentro.BaseOcupada.IsCompleted);
        }
        finally
        {
            encuentro.Desarmar();
        }

        Assert.Empty(Filas(cadena, SelectPresupuestos));
        Assert.Empty(Filas(cadena, SelectLineas));
    }

    /// <summary>
    /// Lanza la primera alta, espera a que quede retenida y lanza la segunda en otro hilo; devuelve los dos resultados.
    /// </summary>
    private static async Task<(ResultadoAltaPresupuesto Primera, ResultadoAltaPresupuesto Segunda)> DosAltasConEncuentroAsync(
        BaseTemporal baseTemporal, EncuentroDeAltas encuentro, long codigo)
    {
        await using var contextoPrimera = ContextoConEncuentro(
            baseTemporal, encuentro, "primera", new MarcasDeTransaccion(encuentro));
        await using var contextoSegunda = ContextoConEncuentro(
            baseTemporal, encuentro, "segunda", new MarcasDeTransaccion(encuentro));
        var primera = AltaConEncuentro(contextoPrimera, encuentro);
        var segunda = AltaConEncuentro(contextoSegunda, encuentro);

        var tareaPrimera = primera.EjecutarAsync(Pedido("Ficticiez", "99999908", codigo));
        await encuentro.PrimeraRetenida.WaitAsync(Tope);
        var tareaSegunda = Task.Run(() => segunda.EjecutarAsync(Pedido("Quimerez", "99999909", codigo)));

        var resultados = await Task.WhenAll(tareaPrimera, tareaSegunda).WaitAsync(Tope);
        return (resultados[0], resultados[1]);
    }

    /// <summary>
    /// Un contexto propio, con su conexión, el busy handler del encuentro y las marcas de transacción. Lo libera quien lo
    /// pide.
    /// </summary>
    private static OpticaDbContext ContextoConEncuentro(
        BaseTemporal baseTemporal, EncuentroDeAltas encuentro, string origen, MarcasDeTransaccion marcas) =>
        ContextoCon(baseTemporal, new BaseOcupadaAlAbrir(encuentro, origen), marcas);

    /// <summary>Un alta sobre <paramref name="contexto"/> con el decorador de artículos del encuentro.</summary>
    private static AltaPresupuesto AltaConEncuentro(OpticaDbContext contexto, EncuentroDeAltas encuentro) =>
        new(
            new PresupuestoRepository(contexto),
            new ArticulosConEncuentro(contexto, encuentro),
            new UnidadDeTrabajoEf(contexto),
            TimeProvider.System);

    private static AltaPresupuesto AltaSimple(OpticaDbContext contexto)
    {
        return new AltaPresupuesto(
            new PresupuestoRepository(contexto), new ArticuloRepository(contexto), new UnidadDeTrabajoEf(contexto),
            TimeProvider.System);
    }

    /// <summary>Un pedido de una línea con cliente ficticio (DNI 99.999.9xx, email @example.com).</summary>
    private static DatosPresupuesto Pedido(string apellido, string dni, long codigo) => new(
        new DatosCliente(apellido, "Prueboncia", dni, null, "cliente@example.com", null),
        [new DatosLinea(codigo, 1, 0m)]);

    private static async Task<long> GrabarArticuloAsync(BaseTemporal baseTemporal)
    {
        var contexto = baseTemporal.CrearContexto();
        var repositorio = new ArticuloRepository(contexto);
        repositorio.Agregar(Articulo.Nuevo("CON-1", "Armazón concurrente", 1850m, 0m, 1850m));
        await new UnidadDeTrabajoEf(contexto).ConfirmarAsync();
        return (await repositorio.ObtenerPorClaveAsync(NormalizacionCodigo.Clave("CON-1")))!.Codigo!.Value;
    }

    /// <summary>Un contexto como el de la infraestructura, con interceptores agregados (patrón de UnidadDeTrabajoTests).</summary>
    private static OpticaDbContext ContextoCon(BaseTemporal baseTemporal, params IInterceptor[] interceptores)
    {
        var opciones = new DbContextOptionsBuilder<OpticaDbContext>();
        baseTemporal.Servicios.GetRequiredService<FabricaConexion>().Configurar(opciones);
        if (interceptores.Length > 0)
        {
            opciones.AddInterceptors(interceptores);
        }

        return new OpticaDbContext(opciones.Options);
    }

    private static string CadenaDe(BaseTemporal baseTemporal) =>
        baseTemporal.Servicios.GetRequiredService<FabricaConexion>().CadenaConexion;

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

    /// <summary>
    /// Simula la lectura vieja del máximo (D-05): <see cref="ObtenerUltimoNumeroAsync"/> devuelve un número fijo; lo demás
    /// va al repositorio real.
    /// </summary>
    private sealed class UltimoNumeroViejo(IPresupuestoRepository real, long ultimoLeido) : IPresupuestoRepository
    {
        public Task<long?> ObtenerUltimoNumeroAsync(CancellationToken cancelacion = default) =>
            Task.FromResult<long?>(ultimoLeido);

        public void Agregar(Presupuesto presupuesto) => real.Agregar(presupuesto);

        public Task<Presupuesto?> ObtenerAsync(long numero, CancellationToken cancelacion = default) =>
            real.ObtenerAsync(numero, cancelacion);

        public void Actualizar(Presupuesto original, Presupuesto modificado, long versionOriginal) =>
            real.Actualizar(original, modificado, versionOriginal);

        public void CambiarEstado(Presupuesto presupuesto, long versionOriginal) =>
            real.CambiarEstado(presupuesto, versionOriginal);
    }
}
