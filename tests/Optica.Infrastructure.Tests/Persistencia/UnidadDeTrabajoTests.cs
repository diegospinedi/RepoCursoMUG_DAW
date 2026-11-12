using System.Data.Common;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Optica.Application.Comun;
using Optica.Domain.Catalogo;
using Optica.Domain.Configuracion;
using Optica.Domain.Presupuestos;
using Optica.Infrastructure.Persistencia;

namespace Optica.Infrastructure.Tests.Persistencia;

/// <summary>
/// <see cref="UnidadDeTrabajoEf.EjecutarEnTransaccionAsync"/>: una transacción de escritura que abarca las lecturas y la
/// grabación de la operación (ADR-013, D-05 de FEAT-002c).
/// </summary>
public class UnidadDeTrabajoTests
{
    private const int SqliteOcupada = 5; // SQLITE_BUSY

    /// <summary>El mensaje propio de la guarda de anidamiento, distinto del error de EF Core (D-19).</summary>
    private const string MensajeAnidada = "La operación en transacción no se anida.";

    private const string MensajeCambiosSinConfirmar =
        "La operación en transacción terminó con cambios registrados sin confirmar.";

    private const string SelectConfiguracion =
        "SELECT AlicuotaIvaCentesimas, CondicionFiscal, MultiploRedondeoCentavos, " +
        "IFNULL(TopeIdentificacionCentavos, 'NULL') FROM ConfiguracionNegocio";

    private const string SelectArticulos =
        "SELECT CodigoProveedor, Descripcion, PrecioVentaCentavos, Activo, Version FROM Articulo ORDER BY Id";

    /// <summary>La fila de configuración inicial de la migración: 21 %, Responsable Inscripto, múltiplo 0,01, sin tope.</summary>
    private static readonly string[] ConfiguracionInicial = ["2100", "responsable_inscripto", "1", "NULL"];

    /// <summary>La configuración inicial con múltiplo 50 (AC-02): 1.815,00 pasa a 1.850,00.</summary>
    private static ConfiguracionNegocio Multiplo50() =>
        new(21m, CondicionFiscal.ResponsableInscripto, 50m, null);

    [Fact]
    public async Task EjecutarEnTransaccion_Anidada_LanzaError()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var contexto = baseTemporal.CrearContexto();
        var unidad = new UnidadDeTrabajoEf(contexto);
        var anidadaEjecutada = false;

        // La operación graba la configuración y después intenta anidar otra: se rechaza sin ejecutarla (D-19).
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => unidad.EjecutarEnTransaccionAsync(
            async cancelacion =>
            {
                new ConfiguracionNegocioRepository(contexto).Reemplazar(Multiplo50());
                await unidad.ConfirmarAsync(cancelacion);
                return await unidad.EjecutarEnTransaccionAsync(
                    _ =>
                    {
                        anidadaEjecutada = true;
                        return Task.FromResult(0);
                    },
                    cancelacion);
            }));

        Assert.False(anidadaEjecutada);
        Assert.Equal(MensajeAnidada, error.Message);
        Assert.Null(error.InnerException);
        Assert.Equal(ConfiguracionInicial, Assert.Single(Filas(cadena, SelectConfiguracion)));
        Assert.Null(contexto.Database.CurrentTransaction);

        // Otra unidad sobre el mismo contexto (el mismo alcance) tampoco anida.
        var otraUnidad = await Assert.ThrowsAsync<InvalidOperationException>(() => unidad.EjecutarEnTransaccionAsync(
            cancelacion => new UnidadDeTrabajoEf(contexto).EjecutarEnTransaccionAsync(
                _ => Task.FromResult(0), cancelacion)));
        Assert.Equal(MensajeAnidada, otraUnidad.Message);

        // Rechazado el anidamiento, la unidad sigue sirviendo.
        Assert.Equal(7, await unidad.EjecutarEnTransaccionAsync(_ => Task.FromResult(7)));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => unidad.EjecutarEnTransaccionAsync<int>(null!));
    }

    [Fact]
    public async Task EjecutarEnTransaccion_OperacionLanza_RevierteTodo()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        await GrabarAsync(baseTemporal, "ABC-1");
        var antes = Filas(cadena, SelectArticulos);
        var contexto = baseTemporal.CrearContexto();
        var unidad = new UnidadDeTrabajoEf(contexto);
        var articulos = new ArticuloRepository(contexto);
        var falla = new InvalidOperationException("falla de prueba");

        // La configuración y el precio recalculado llegan a grabarse dentro de la operación, que después lanza.
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => unidad.EjecutarEnTransaccionAsync<int>(
            async cancelacion =>
            {
                new ConfiguracionNegocioRepository(contexto).Reemplazar(Multiplo50());
                var articulo = Assert.Single(await articulos.ListarParaRecalculoAsync(cancelacion));
                articulos.RegistrarPrecioRecalculado(articulo.ConPrecioRecalculado(Multiplo50()).Articulo!);
                await unidad.ConfirmarAsync(cancelacion);
                throw falla;
            }));

        Assert.Same(falla, error);
        Assert.Equal(ConfiguracionInicial, Assert.Single(Filas(cadena, SelectConfiguracion)));
        Assert.Equal(antes, Filas(cadena, SelectArticulos));
        Assert.Empty(contexto.ChangeTracker.Entries());
        Assert.Null(contexto.Database.CurrentTransaction);

        // El mismo contexto lee lo guardado, no lo que quedó revertido.
        Assert.Equal(0.01m, (await new ConfiguracionNegocioRepository(contexto).ObtenerAsync()).MultiploRedondeo);
        Assert.Equal(1815m, Assert.Single(await articulos.ListarParaRecalculoAsync()).PrecioVenta);
    }

    [Fact]
    public async Task EjecutarEnTransaccion_ConflictoDeUnicidad_NoGrabaNada()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        await GrabarAsync(baseTemporal, "ABC-1");
        var antes = Filas(cadena, SelectArticulos);
        var contexto = baseTemporal.CrearContexto();
        var unidad = new UnidadDeTrabajoEf(contexto);

        // La configuración se graba dentro de la operación; el alta siguiente repite la clave y nadie atiende el conflicto.
        var error = await Assert.ThrowsAsync<ConflictoAlConfirmarException>(() => unidad.EjecutarEnTransaccionAsync<int>(
            async cancelacion =>
            {
                new ConfiguracionNegocioRepository(contexto).Reemplazar(Multiplo50());
                await unidad.ConfirmarAsync(cancelacion);
                new ArticuloRepository(contexto).Agregar(Articulo.Nuevo(" abc-1 ", "Otro", 1m, 0m, 1m));
                await unidad.ConfirmarAsync(cancelacion);
                return 0;
            }));

        Assert.Equal(MotivoConflicto.Unicidad, error.Motivo);
        Assert.Null(error.InnerException);
        Assert.Equal(ConfiguracionInicial, Assert.Single(Filas(cadena, SelectConfiguracion)));
        Assert.Equal(antes, Filas(cadena, SelectArticulos));
        Assert.Empty(contexto.ChangeTracker.Entries());
        Assert.Null(contexto.Database.CurrentTransaction);
    }

    [Fact]
    public async Task EjecutarEnTransaccion_ConfirmaLecturasYGrabacionJuntas()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        await GrabarAsync(baseTemporal, "ABC-1", "ABC-2");
        var contexto = baseTemporal.CrearContexto();
        var unidad = new UnidadDeTrabajoEf(contexto);
        var configuracion = new ConfiguracionNegocioRepository(contexto);
        var articulos = new ArticuloRepository(contexto);
        string[]? configuracionVistaDesdeAfuera = null;

        // FR-02: la lectura de la configuración y los artículos, el reemplazo y el recálculo, en una sola transacción.
        var cambiados = await unidad.EjecutarEnTransaccionAsync(async cancelacion =>
        {
            var vigente = await configuracion.ObtenerAsync(cancelacion);
            Assert.Equal(0.01m, vigente.MultiploRedondeo);

            // Antes de grabar nada, la transacción ya tiene la escritura tomada (BEGIN IMMEDIATE): otra conexión no
            // puede empezar a escribir entre la lectura y la grabación.
            var ocupada = Assert.Throws<SqliteException>(() => EmpezarEscrituraDesdeOtraConexion(cadena));
            Assert.Equal(SqliteOcupada, ocupada.SqliteErrorCode);

            var nueva = Multiplo50();
            var recalculados = (await articulos.ListarParaRecalculoAsync(cancelacion))
                .Select(articulo => articulo.ConPrecioRecalculado(nueva))
                .Where(resultado => resultado.EsCambiado)
                .Select(resultado => resultado.Articulo!)
                .ToList();
            configuracion.Reemplazar(nueva);
            recalculados.ForEach(articulos.RegistrarPrecioRecalculado);
            await unidad.ConfirmarAsync(cancelacion);

            // ConfirmarAsync dentro de la operación solo grabó: nadie más lo ve hasta que la operación termina.
            configuracionVistaDesdeAfuera = Assert.Single(Filas(cadena, SelectConfiguracion));
            return recalculados.Count;
        });

        Assert.Equal(2, cambiados);
        Assert.Equal(ConfiguracionInicial, configuracionVistaDesdeAfuera);
        Assert.Equal(["2100", "responsable_inscripto", "5000", "NULL"], Assert.Single(Filas(cadena, SelectConfiguracion)));
        Assert.Equal(
            [
                ["ABC-1", "Armazón", "185000", "1", "1"],
                ["ABC-2", "Armazón", "185000", "1", "1"],
            ],
            Filas(cadena, SelectArticulos));
        Assert.Null(contexto.Database.CurrentTransaction);
    }

    [Fact]
    public async Task EjecutarEnTransaccion_ConflictoAtendido_ConfirmaSinCambios()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        await GrabarAsync(baseTemporal, "ABC-1");
        var antes = Filas(cadena, SelectArticulos);
        var contexto = baseTemporal.CrearContexto();
        var unidad = new UnidadDeTrabajoEf(contexto);
        var articulos = new ArticuloRepository(contexto);

        // Como el alta (D-18): la operación atiende el conflicto y termina normalmente; la transacción externa confirma.
        var motivo = await unidad.EjecutarEnTransaccionAsync<MotivoConflicto?>(async cancelacion =>
        {
            articulos.Agregar(Articulo.Nuevo(" abc-1 ", "Otro", 1m, 0m, 1m));
            try
            {
                await unidad.ConfirmarAsync(cancelacion);
                return null;
            }
            catch (ConflictoAlConfirmarException conflicto)
            {
                return conflicto.Motivo;
            }
        });

        Assert.Equal(MotivoConflicto.Unicidad, motivo);
        Assert.Equal(antes, Filas(cadena, SelectArticulos));
        Assert.Empty(contexto.ChangeTracker.Entries());
        Assert.Null(contexto.Database.CurrentTransaction);

        // La conexión quedó libre: otra operación en el mismo contexto graba.
        await unidad.EjecutarEnTransaccionAsync(async cancelacion =>
        {
            articulos.Agregar(Articulo.Nuevo("XYZ-9", "Estuche", 1m, 0m, 1m));
            await unidad.ConfirmarAsync(cancelacion);
            return 0;
        });
        Assert.Equal(2, Filas(cadena, SelectArticulos).Count);
    }

    [Fact]
    public async Task EjecutarEnTransaccion_CambiosSinConfirmar_Rechazado()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        await GrabarAsync(baseTemporal, "ABC-1");
        var antes = Filas(cadena, SelectArticulos);
        var contexto = baseTemporal.CrearContexto();
        var unidad = new UnidadDeTrabajoEf(contexto);

        // La operación confirma la configuración, pero registra un alta y termina sin confirmarla.
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => unidad.EjecutarEnTransaccionAsync(
            async cancelacion =>
            {
                new ConfiguracionNegocioRepository(contexto).Reemplazar(Multiplo50());
                await unidad.ConfirmarAsync(cancelacion);
                new ArticuloRepository(contexto).Agregar(Articulo.Nuevo("QZ-777", "Lente distintivo", 1m, 0m, 1m));
                return 0;
            }));

        Assert.Equal(MensajeCambiosSinConfirmar, error.Message);
        Assert.DoesNotContain("QZ-777", error.ToString(), StringComparison.Ordinal);
        Assert.Equal(ConfiguracionInicial, Assert.Single(Filas(cadena, SelectConfiguracion)));
        Assert.Equal(antes, Filas(cadena, SelectArticulos));
        Assert.Empty(contexto.ChangeTracker.Entries());
        Assert.Null(contexto.Database.CurrentTransaction);
    }

    [Fact]
    public async Task EjecutarEnTransaccion_FallaConCancelacionPedida_Revierte()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var reversiones = new ReversionesObservadas(fallar: false);
        using var contexto = ContextoCon(baseTemporal, reversiones);
        var unidad = new UnidadDeTrabajoEf(contexto);
        using var cancelacion = new CancellationTokenSource();
        var falla = new InvalidOperationException("falla de prueba");

        // La operación graba, el pedido se cancela y la operación lanza: la reversión no depende de ese token.
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => unidad.EjecutarEnTransaccionAsync<int>(
            async token =>
            {
                new ConfiguracionNegocioRepository(contexto).Reemplazar(Multiplo50());
                await unidad.ConfirmarAsync(token);
                await cancelacion.CancelAsync();
                throw falla;
            },
            cancelacion.Token));

        Assert.Same(falla, error);
        Assert.Equal(1, reversiones.Intentos);
        Assert.Equal(1, reversiones.Revertidas);
        Assert.False(reversiones.TokenCanceladoAlRevertir);
        Assert.Equal(ConfiguracionInicial, Assert.Single(Filas(cadena, SelectConfiguracion)));
        Assert.Empty(contexto.ChangeTracker.Entries());
        Assert.Null(contexto.Database.CurrentTransaction);
    }

    [Fact]
    public async Task EjecutarEnTransaccion_ReversionFalla_PropagaLaOriginal()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        var reversiones = new ReversionesObservadas(fallar: true);
        using var contexto = ContextoCon(baseTemporal, reversiones);
        var unidad = new UnidadDeTrabajoEf(contexto);
        var falla = new InvalidOperationException("falla de prueba");

        // La reversión explícita falla: sale la excepción de la operación, no la de la reversión.
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => unidad.EjecutarEnTransaccionAsync<int>(
            async token =>
            {
                new ConfiguracionNegocioRepository(contexto).Reemplazar(Multiplo50());
                await unidad.ConfirmarAsync(token);
                throw falla;
            }));

        Assert.Same(falla, error);
        Assert.Equal(1, reversiones.Intentos);
        Assert.Equal(0, reversiones.Revertidas);
        // Al descartarse sin confirmar, la transacción igual se revierte.
        Assert.Equal(ConfiguracionInicial, Assert.Single(Filas(cadena, SelectConfiguracion)));
        Assert.Empty(contexto.ChangeTracker.Entries());
        Assert.Null(contexto.Database.CurrentTransaction);
    }

    [Fact]
    public async Task Confirmar_TriggerRegistroCerrado_ConflictoSinCausa()
    {
        using var baseTemporal = new BaseTemporal();
        var cadena = CadenaDe(baseTemporal);
        await GrabarAsync(baseTemporal, "ABC-1");
        var articulo = (await new ArticuloRepository(baseTemporal.CrearContexto()).ObtenerPorClaveAsync("ABC-1"))!;
        var linea = LineaPresupuesto.DesdeArticulo(articulo, cantidad: 1, porcentajeDescuento: 0m).Linea!;
        await GrabarPresupuestoAsync(baseTemporal, 1, linea);
        await GrabarPresupuestoAsync(baseTemporal, 2, linea);

        // El 1 pasa a Final, como en la aplicación (AGENTS.md).
        var cierre = baseTemporal.CrearContexto();
        var repositorioCierre = new PresupuestoRepository(cierre);
        var borrador = (await repositorioCierre.ObtenerAsync(1))!;
        repositorioCierre.CambiarEstado(borrador.PasarAFinal(), borrador.Version);
        await new UnidadDeTrabajoEf(cierre).ConfirmarAsync();
        var encabezados = Filas(cadena, SelectPresupuestos);
        var lineas = Filas(cadena, SelectLineas);

        // Un camino que saltea el dominio y el caso de uso modifica el Final con la versión correcta: el trigger lo
        // rechaza y la unidad de trabajo lo informa como registro cerrado, sin causa (ADR-017).
        var contexto = baseTemporal.CrearContexto();
        var unidad = new UnidadDeTrabajoEf(contexto);
        var repositorio = new PresupuestoRepository(contexto);
        var error = await Assert.ThrowsAsync<ConflictoAlConfirmarException>(() => unidad.EjecutarEnTransaccionAsync<int>(
            async cancelacion =>
            {
                var final = (await repositorio.ObtenerAsync(1, cancelacion))!;
                var modificado = new Presupuesto(
                    1, final.Fecha, EstadoPresupuesto.Borrador, ClienteDistintivo(), [linea, linea], final.Version);
                repositorio.Actualizar(final, modificado, final.Version);
                await unidad.ConfirmarAsync(cancelacion);
                return 0;
            }));

        Assert.Equal(MotivoConflicto.RegistroCerrado, error.Motivo);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("Quimerez", error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("99999999", error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("quimerez@example.com", error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("registro_cerrado", error.Message, StringComparison.Ordinal);
        Assert.Empty(contexto.ChangeTracker.Entries());
        Assert.Null(contexto.Database.CurrentTransaction);
        Assert.Equal(encabezados, Filas(cadena, SelectPresupuestos));
        Assert.Equal(lineas, Filas(cadena, SelectLineas));

        // Otro trigger (la fecha de un Borrador) usa el mismo código extendido y otro texto: no es un registro cerrado,
        // se propaga como error.
        var otroContexto = baseTemporal.CrearContexto();
        var fila = otroContexto.Presupuestos.Find(2L)!;
        fila.Fecha = new DateOnly(2026, 3, 11);
        var otroError = await Assert.ThrowsAsync<DbUpdateException>(() => new UnidadDeTrabajoEf(otroContexto).ConfirmarAsync());
        var sqlite = Assert.IsType<SqliteException>(otroError.InnerException);
        Assert.Equal(1811, sqlite.SqliteExtendedErrorCode);
        Assert.Contains("'presupuesto_fecha_numero'", sqlite.Message, StringComparison.Ordinal);
        Assert.Empty(otroContexto.ChangeTracker.Entries());
        Assert.Equal(encabezados, Filas(cadena, SelectPresupuestos));
    }

    private const string SelectPresupuestos =
        "SELECT Numero, Fecha, Estado, Apellido, Nombre, Dni, Version FROM Presupuesto ORDER BY Numero";

    private const string SelectLineas =
        "SELECT PresupuestoNumero, Orden, Cantidad, PrecioFinalCentavos FROM LineaPresupuesto " +
        "ORDER BY PresupuestoNumero, Orden";

    /// <summary>Cliente ficticio con valores distintivos, para buscarlos en los mensajes; DNI y email inventados.</summary>
    private static ClientePresupuesto ClienteDistintivo() =>
        new("Quimerez", "Distintiva", "99999999", null, "quimerez@example.com", null);

    private static async Task GrabarPresupuestoAsync(BaseTemporal baseTemporal, long numero, LineaPresupuesto linea)
    {
        var contexto = baseTemporal.CrearContexto();
        new PresupuestoRepository(contexto).Agregar(Presupuesto.Nuevo(
            numero, new DateOnly(2026, 3, 10), new ClientePresupuesto("Ficticiez", "Prueboncia", "99999998", null, null, null),
            [linea]));
        await new UnidadDeTrabajoEf(contexto).ConfirmarAsync();
    }

    /// <summary>Un contexto como el de la infraestructura, con un interceptor de transacciones agregado.</summary>
    private static OpticaDbContext ContextoCon(BaseTemporal baseTemporal, IInterceptor interceptor)
    {
        var opciones = new DbContextOptionsBuilder<OpticaDbContext>();
        baseTemporal.Servicios.GetRequiredService<FabricaConexion>().Configurar(opciones);
        opciones.AddInterceptors(interceptor);
        return new OpticaDbContext(opciones.Options);
    }

    /// <summary>
    /// Cuenta las reversiones explícitas que EF Core intenta y completa; con <paramref name="fallar"/>, cada intento lanza
    /// antes de revertir, como una reversión que falla.
    /// </summary>
    private sealed class ReversionesObservadas(bool fallar) : DbTransactionInterceptor
    {
        public int Intentos { get; private set; }

        public int Revertidas { get; private set; }

        public bool TokenCanceladoAlRevertir { get; private set; }

        public override ValueTask<InterceptionResult> TransactionRollingBackAsync(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            Intentos++;
            TokenCanceladoAlRevertir |= cancellationToken.IsCancellationRequested;
            return fallar
                ? throw new InvalidOperationException("reversión de prueba")
                : ValueTask.FromResult(result);
        }

        public override Task TransactionRolledBackAsync(
            DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            Revertidas++;
            return Task.CompletedTask;
        }
    }

    /// <summary>Da de alta artículos con costo 1.210 y margen 50 % (precio 1.815, AC-02), fuera de toda operación.</summary>
    private static async Task GrabarAsync(BaseTemporal baseTemporal, params string[] codigos)
    {
        var contexto = baseTemporal.CrearContexto();
        var repositorio = new ArticuloRepository(contexto);
        foreach (var codigo in codigos)
        {
            repositorio.Agregar(Articulo.Nuevo(codigo, "Armazón", 1210m, 50m, 1815m));
        }

        await new UnidadDeTrabajoEf(contexto).ConfirmarAsync();
    }

    /// <summary>Intenta tomar la escritura desde otra conexión, esperando a lo sumo 1 segundo.</summary>
    private static void EmpezarEscrituraDesdeOtraConexion(string cadena)
    {
        using var conexion = new SqliteConnection(cadena);
        conexion.Open();
        using var comando = conexion.CreateCommand();
        comando.CommandTimeout = 1;
        comando.CommandText = "BEGIN IMMEDIATE; ROLLBACK;";
        comando.ExecuteNonQuery();
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
}
