using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Optica.Domain.Configuracion;
using Optica.Infrastructure.Persistencia;

namespace Optica.Infrastructure.Tests.Persistencia;

/// <summary>
/// Spec FEAT-009, Block 1 (FR-01, AC-01, ADR-028): los datos del emisor en la tabla <c>DatosEmisor</c>, fila única con
/// <c>Id = 1</c>. Datos ficticios; el CUIT es el de ejemplo del PRD.
/// </summary>
public class DatosEmisorRepositoryTests
{
    private const string SelectFila =
        "SELECT Id, RazonSocial, DomicilioComercial, Cuit, IngresosBrutos, InicioActividades FROM DatosEmisor";

    private static readonly DateOnly Hoy = new(2026, 11, 16);

    [Fact]
    public async Task Repositorio_GrabaYLeeLosCincoDatos()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = FacturasPrueba.CadenaDe(baseTemporal);

        // La primera grabación inserta la fila, y solo al confirmar la unidad de trabajo.
        var contexto = baseTemporal.CrearContexto();
        var repositorio = new DatosEmisorRepository(contexto);
        repositorio.Reemplazar(Datos("Óptica Ficticia Prueba S.R.L.", "Calle Inventada 123", "20-00000000-1", "2020-03-01"));
        Assert.Empty(FacturasPrueba.Filas(cadena, SelectFila));
        await new UnidadDeTrabajoEf(contexto).ConfirmarAsync();

        // El CUIT queda sin guiones; la fecha, como yyyy-MM-dd.
        Assert.Equal(
            ["1", "Óptica Ficticia Prueba S.R.L.", "Calle Inventada 123", "20000000001", "901-123456-7", "2020-03-01"],
            Assert.Single(FacturasPrueba.Filas(cadena, SelectFila)));

        // AC-01: otro alcance los vuelve a leer, los cinco.
        var leidos = await new DatosEmisorRepository(baseTemporal.CrearContexto()).ObtenerAsync();
        Assert.NotNull(leidos);
        Assert.Equal("Óptica Ficticia Prueba S.R.L.", leidos.RazonSocial);
        Assert.Equal("Calle Inventada 123", leidos.DomicilioComercial);
        Assert.Equal("20000000001", leidos.Cuit.Valor);
        Assert.Equal("901-123456-7", leidos.IngresosBrutos);
        Assert.Equal(new DateOnly(2020, 3, 1), leidos.InicioActividades);

        // La segunda grabación reemplaza la misma fila, también desde un contexto que ya la había leído.
        var otroContexto = baseTemporal.CrearContexto();
        var otroRepositorio = new DatosEmisorRepository(otroContexto);
        Assert.NotNull(await otroRepositorio.ObtenerAsync());
        otroRepositorio.Reemplazar(Datos("Óptica Imaginaria", "Avenida Supuesta 456", "20000000001", "2021-07-15"));
        await new UnidadDeTrabajoEf(otroContexto).ConfirmarAsync();
        Assert.Equal(
            ["1", "Óptica Imaginaria", "Avenida Supuesta 456", "20000000001", "901-123456-7", "2021-07-15"],
            Assert.Single(FacturasPrueba.Filas(cadena, SelectFila)));

        // La última grabación gana aunque la entrada reutilizada tenga valores originales viejos: el primer contexto
        // sigue con la fila en seguimiento con los datos de la primera grabación, y vuelve a grabar esos mismos datos
        // después de que el otro grabó otros. El UPDATE tiene que llevar los cinco valores igual.
        repositorio.Reemplazar(Datos("Óptica Ficticia Prueba S.R.L.", "Calle Inventada 123", "20-00000000-1", "2020-03-01"));
        await new UnidadDeTrabajoEf(contexto).ConfirmarAsync();
        Assert.Equal(
            ["1", "Óptica Ficticia Prueba S.R.L.", "Calle Inventada 123", "20000000001", "901-123456-7", "2020-03-01"],
            Assert.Single(FacturasPrueba.Filas(cadena, SelectFila)));

        // Registrado en la raíz de composición, por alcance como el resto de los repositorios.
        using var alcance = baseTemporal.Servicios.CreateScope();
        Assert.IsType<DatosEmisorRepository>(alcance.ServiceProvider.GetRequiredService<IDatosEmisorRepository>());

        // Los CHECK de la tabla son la segunda barrera: un CUIT con guiones no entra aunque se esquive el dominio.
        var check = Assert.Throws<SqliteException>(() => FacturasPrueba.Ejecutar(
            cadena, "UPDATE DatosEmisor SET Cuit = '20-00000000-1'"));
        Assert.Contains("CHECK constraint failed: CK_DatosEmisor_Cuit", check.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Repositorio_SinFila_DevuelveNull()
    {
        using var baseTemporal = new BaseTemporal();

        // Una base recién creada no tiene datos del emisor: la migración no inserta una fila (ADR-028).
        Assert.Empty(FacturasPrueba.Filas(FacturasPrueba.CadenaDe(baseTemporal), SelectFila));
        Assert.Null(await new DatosEmisorRepository(baseTemporal.CrearContexto()).ObtenerAsync());
    }

    [Theory]
    [InlineData("20000000002", "2020-03-01", "20000000002")] // pasa el CHECK pero no el dígito verificador
    [InlineData("20000000001", "2020-02-30", "2020-02-30")] // la fecha no existe
    [InlineData("20000000001", "01/03/2020", "01/03/2020")] // otro formato, con los CHECK desactivados
    public async Task Repositorio_FilaCorrupta_ErrorSinValores(string cuit, string inicio, string valor)
    {
        using var baseTemporal = new BaseTemporal();

        // Una fila editada a mano con los CHECK desactivados. La conexión es propia y sin grupo, así el pragma no queda
        // activo en una conexión que después reutilice el contexto.
        var sinGrupo = new SqliteConnectionStringBuilder(FacturasPrueba.CadenaDe(baseTemporal)) { Pooling = false }
            .ToString();
        using (var conexion = new SqliteConnection(sinGrupo))
        {
            conexion.Open();
            using (var pragma = conexion.CreateCommand())
            {
                pragma.CommandText = "PRAGMA ignore_check_constraints = ON;";
                pragma.ExecuteNonQuery();
            }

            FacturasPrueba.Insertar(conexion, "DatosEmisor", new(StringComparer.Ordinal)
            {
                ["Id"] = 1L,
                ["RazonSocial"] = "Óptica Ficticia Prueba S.R.L.",
                ["DomicilioComercial"] = "Calle Inventada 123",
                ["Cuit"] = cuit,
                ["IngresosBrutos"] = "901-123456-7",
                ["InicioActividades"] = inicio,
            });
        }

        var repositorio = new DatosEmisorRepository(baseTemporal.CrearContexto());

        // Estado corrupto, con texto fijo: ni el valor que falló ni el resto de los datos de la fila.
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => repositorio.ObtenerAsync());
        Assert.Equal("Los datos del emisor guardados no son válidos.", error.Message);
        for (Exception? causa = error; causa is not null; causa = causa.InnerException)
        {
            Assert.DoesNotContain(valor, causa.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("Ficticia", causa.Message, StringComparison.Ordinal);
        }
    }

    private static DatosEmisor Datos(string razonSocial, string domicilio, string cuit, string inicio) =>
        DatosEmisor.ParaGrabar(razonSocial, domicilio, cuit, "901-123456-7", inicio, Hoy).Datos
        ?? throw new InvalidOperationException("Datos de prueba inválidos.");
}
