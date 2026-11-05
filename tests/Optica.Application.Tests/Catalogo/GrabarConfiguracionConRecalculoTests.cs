using Microsoft.Extensions.Logging;
using Optica.Application.Catalogo;
using Optica.Application.Comun;
using Optica.Application.Configuracion;
using Optica.Application.Tests.Acceso;
using Optica.Domain.Catalogo;
using Optica.Domain.Configuracion;

namespace Optica.Application.Tests.Catalogo;

/// <summary>
/// La grabación de la configuración con recálculo del catálogo (FEAT-002c, Block 3). Incluye los casos de
/// <c>GrabarConfiguracionNegocioTests</c> de FEAT-001c, que pasaron a este caso de uso (D-10).
/// </summary>
public class GrabarConfiguracionConRecalculoTests
{
    /// <summary>
    /// Un costo cuyo precio de venta (costo × 1,5 en el doble) entra en el rango con múltiplo 0,01
    /// (92.233.720.368.547.758,00) y lo supera al redondear hacia arriba al múltiplo 50.
    /// </summary>
    private const decimal CostoAlBorde = 61_489_146_912_365_172m;

    private const string AgregadoInactivo = "un artículo inactivo hay que reactivarlo para editarlo";

    private readonly CatalogoYConfiguracionEnMemoria _almacen = new();
    private readonly FabricaRegistrosFalsa _registros = new();
    private readonly RelojFijo _reloj = new();

    private ArticulosEnMemoria Articulos => _almacen.Articulos;

    private GrabarConfiguracionConRecalculo CrearCasoDeUso() =>
        new(_almacen, _almacen.Articulos, _almacen, _reloj, _registros);

    private ObtenerConfiguracionNegocio CrearConsulta() => new(_almacen);

    private static DatosConfiguracion Validos(
        decimal? alicuota = 21m,
        string? condicion = "responsable_inscripto",
        decimal? multiplo = 0.01m,
        decimal? tope = 10_000_000m) => new(alicuota, condicion, multiplo, tope);

    private static ErrorValidacion ErrorMultiplo(string accion) => new("multiploRedondeo", accion);

    private void AfirmarNadaGrabado(ConfiguracionNegocio? anterior, IReadOnlyList<Articulo> articulosAntes)
    {
        Assert.Same(anterior, _almacen.Guardada);
        Assert.Equal(
            articulosAntes.Select(a => (a.Codigo, a.PrecioVenta, a.Version, a.Activo)).OrderBy(a => a.Codigo),
            Articulos.Guardados.Select(a => (a.Codigo, a.PrecioVenta, a.Version, a.Activo)).OrderBy(a => a.Codigo));
        Assert.Empty(_registros.Entradas);
    }

    // ---- Datos inválidos ----

    [Fact]
    public async Task Grabar_DatosInvalidos_NoLeeNiGraba()
    {
        Articulos.Sembrar("ABC-1", costo: 1210m);

        var resultado = await CrearCasoDeUso().EjecutarAsync(new DatosConfiguracion(null, null, null, 0.005m));

        Assert.False(resultado.EsGrabado);
        Assert.Equal(4, resultado.Errores.Count);
        Assert.Empty(_registros.Entradas);
        Assert.Equal(0, _almacen.Lecturas);
        Assert.Equal(0, _almacen.Reemplazos);
        Assert.Equal(0, Articulos.Operaciones);
        Assert.Equal(0, Articulos.ListadosParaRecalculo);
        Assert.Equal(0, Articulos.Confirmaciones);
    }

    [Fact]
    public async Task Grabar_Alicuota101_RechazadoSinGrabar()
    {
        var anterior = _almacen.Guardada;

        var resultado = await CrearCasoDeUso().EjecutarAsync(Validos(alicuota: 101m));

        Assert.False(resultado.EsGrabado);
        var error = Assert.Single(resultado.Errores);
        Assert.Equal(new ErrorValidacion("alicuotaIva", "La alícuota de IVA debe estar entre 0 y 100."), error);
        Assert.Same(anterior, _almacen.Guardada);
        Assert.Equal(0, _almacen.Reemplazos);
        Assert.Equal(0, Articulos.Confirmaciones);
    }

    [Fact]
    public async Task Grabar_Multiplo0coma001_RechazadoConMinimo()
    {
        var resultado = await CrearCasoDeUso().EjecutarAsync(Validos(multiplo: 0.001m));

        Assert.False(resultado.EsGrabado);
        var error = Assert.Single(resultado.Errores);
        Assert.Equal(ErrorMultiplo("El valor mínimo es 0,01."), error);
        Assert.Equal(0, Articulos.Confirmaciones);
    }

    [Fact]
    public async Task Grabar_Tope0_RechazadoMayorACero()
    {
        var resultado = await CrearCasoDeUso().EjecutarAsync(Validos(tope: 0m));

        Assert.False(resultado.EsGrabado);
        var error = Assert.Single(resultado.Errores);
        Assert.Equal(new ErrorValidacion("topeIdentificacion", "El tope debe ser mayor a 0."), error);
        Assert.Equal(0, Articulos.Confirmaciones);
    }

    [Fact]
    public async Task Grabar_VariosCamposInvalidos_DevuelveTodosLosErrores()
    {
        var resultado = await CrearCasoDeUso().EjecutarAsync(Validos(alicuota: 101m, condicion: "exento", tope: -1m));

        Assert.False(resultado.EsGrabado);
        Assert.Equal(
            [
                new ErrorValidacion("alicuotaIva", "La alícuota de IVA debe estar entre 0 y 100."),
                new ErrorValidacion("condicionFiscal", "Elegí Responsable Inscripto o Monotributo."),
                new ErrorValidacion("topeIdentificacion", "El tope debe ser mayor a 0."),
            ],
            resultado.Errores);
        Assert.Equal(0, _almacen.Reemplazos);
        Assert.Equal(0, Articulos.Confirmaciones);
    }

    // ---- Precio fuera de rango (AC-04, D-01, D-08, D-12) ----

    [Fact]
    public async Task Grabar_PrecioFueraDeRango_RechazadoConCodigos()
    {
        Articulos.Sembrar("ABC-1", costo: 1210m);
        var primero = Articulos.Sembrar("Zq7-proveedor", "Zq7-descripcion", CostoAlBorde);
        var segundo = Articulos.Sembrar("Zq8-proveedor", "Zq8-descripcion", CostoAlBorde);
        var anterior = _almacen.Guardada;
        var antes = Articulos.Guardados.ToList();

        var resultado = await CrearCasoDeUso().EjecutarAsync(Validos(multiplo: 50m));

        Assert.False(resultado.EsGrabado);
        Assert.Equal(
            [
                ErrorMultiplo(
                    $"No se grabó el cambio: el precio de venta de 2 artículos sería demasiado grande (códigos " +
                    $"{primero.Codigo} y {segundo.Codigo}). Revisá esos artículos o el múltiplo."),
            ],
            resultado.Errores);
        // D-12: ni el código en el proveedor ni la descripción ni importes.
        Assert.DoesNotContain("Zq", resultado.Errores[0].Accion, StringComparison.Ordinal);
        // D-03: nada registrado; la operación termina sin cambios.
        Assert.Equal(0, _almacen.Reemplazos);
        Assert.Empty(Articulos.PreciosRecalculadosRegistrados);
        Assert.Equal(0, Articulos.Confirmaciones);
        AfirmarNadaGrabado(anterior, antes);
    }

    [Fact]
    public async Task Grabar_MasDe10FueraDeRango_RechazadoCon10Codigos()
    {
        Articulos.Sembrar("ABC-1", costo: 1210m);
        for (var i = 0; i < 12; i++)
        {
            Articulos.Sembrar($"BORDE-{i}", costo: CostoAlBorde);
        }

        var resultado = await CrearCasoDeUso().EjecutarAsync(Validos(multiplo: 50m));

        // Los artículos 2 a 13 quedan fuera de rango: se nombran los 10 primeros por código (orden numérico).
        Assert.False(resultado.EsGrabado);
        Assert.Equal(
            [
                ErrorMultiplo(
                    "No se grabó el cambio: el precio de venta de 12 artículos sería demasiado grande (códigos " +
                    "2, 3, 4, 5, 6, 7, 8, 9, 10, 11 y 2 más). Revisá esos artículos o el múltiplo."),
            ],
            resultado.Errores);
        Assert.Empty(Articulos.PreciosRecalculadosRegistrados);
    }

    [Fact]
    public async Task Grabar_InactivoFueraDeRango_RechazadoConMarca()
    {
        var activo = Articulos.Sembrar("BORDE-1", costo: CostoAlBorde);
        var inactivo = Articulos.Sembrar("BORDE-2", costo: CostoAlBorde, activo: false);

        var resultado = await CrearCasoDeUso().EjecutarAsync(Validos(multiplo: 50m));

        Assert.False(resultado.EsGrabado);
        Assert.Equal(
            [
                ErrorMultiplo(
                    $"No se grabó el cambio: el precio de venta de 2 artículos sería demasiado grande (códigos " +
                    $"{activo.Codigo} y {inactivo.Codigo}, inactivo). Revisá esos artículos o el múltiplo; " +
                    $"{AgregadoInactivo}."),
            ],
            resultado.Errores);
    }

    [Fact]
    public async Task Grabar_UnoFueraDeRango_CodigoEnSingular()
    {
        var articulo = Articulos.Sembrar("BORDE-1", costo: CostoAlBorde);

        var resultado = await CrearCasoDeUso().EjecutarAsync(Validos(multiplo: 50m));

        Assert.Equal(
            [
                ErrorMultiplo(
                    $"No se grabó el cambio: el precio de venta de 1 artículo sería demasiado grande (código " +
                    $"{articulo.Codigo}). Revisá esos artículos o el múltiplo."),
            ],
            resultado.Errores);
    }

    // ---- Fallas ----

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Grabar_FallaAlConfirmar_NoRegistraNiGraba(bool enElCommitExterno)
    {
        Articulos.Sembrar("ABC-1", costo: 1210m);
        var anterior = _almacen.Guardada;
        var antes = Articulos.Guardados.ToList();
        var falla = new InvalidOperationException("Falla de la base.");
        if (enElCommitExterno)
        {
            Articulos.ErrorAlConfirmarOperacion = falla;
        }
        else
        {
            Articulos.ErrorAlConfirmar = falla;
        }

        var lanzada = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CrearCasoDeUso().EjecutarAsync(Validos(multiplo: 50m)));

        Assert.Same(falla, lanzada);
        Assert.Equal(1, Articulos.Operaciones);
        Assert.Equal(0, Articulos.OperacionesConfirmadas);
        AfirmarNadaGrabado(anterior, antes);
    }

    /// <summary>
    /// Un pedido cancelado durante la operación (aquí, al listar el catálogo) propaga la cancelación: la transacción se
    /// revierte, la configuración, los precios y las versiones quedan como estaban y no se registra la grabación.
    /// </summary>
    [Fact]
    public async Task Grabar_Cancelado_RevierteSinRegistrar()
    {
        Articulos.Sembrar("ABC-1", costo: 1210m, version: 4);
        var anterior = _almacen.Guardada;
        var antes = Articulos.Guardados.ToList();
        using var cancelacion = new CancellationTokenSource();
        Articulos.AlListarParaRecalculo = cancelacion.Cancel;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CrearCasoDeUso().EjecutarAsync(Validos(multiplo: 50m), cancelacion.Token));

        Assert.True(cancelacion.IsCancellationRequested);
        Assert.Equal(1, Articulos.Operaciones);
        Assert.Equal(0, Articulos.OperacionesConfirmadas);
        Assert.Empty(Articulos.PreciosRecalculadosRegistrados);
        AfirmarNadaGrabado(anterior, antes);
    }

    [Fact]
    public async Task Grabar_ConflictoAlConfirmar_SePropaga()
    {
        var anterior = _almacen.Guardada;
        Articulos.ErrorAlConfirmar = new ConflictoAlConfirmarException("Conflicto de prueba.", MotivoConflicto.Concurrencia);

        await Assert.ThrowsAsync<ConflictoAlConfirmarException>(
            () => CrearCasoDeUso().EjecutarAsync(Validos(tope: 1m)));

        Assert.Same(anterior, _almacen.Guardada);
        Assert.Empty(_registros.Entradas);
    }

    [Fact]
    public async Task Grabar_SinFila_PropagaError()
    {
        _almacen.Guardada = null;

        await Assert.ThrowsAsync<InvalidOperationException>(() => CrearCasoDeUso().EjecutarAsync(Validos()));

        Assert.Equal(0, Articulos.Confirmaciones);
        Assert.Equal(0, Articulos.OperacionesConfirmadas);
        Assert.Empty(_registros.Entradas);
    }

    // ---- Recálculo (AC-01, AC-02, AC-03, D-04, D-07, D-09, D-22) ----

    [Fact]
    public async Task Grabar_Multiplo50_Recalcula1815A1850()
    {
        var articulo = Articulos.Sembrar("ABC-1", costo: 1210m, version: 4);

        var resultado = await CrearCasoDeUso().EjecutarAsync(Validos(multiplo: 50m));

        Assert.True(resultado.EsGrabado);
        Assert.Equal(1, resultado.PreciosCambiados);
        var guardado = Articulos.Guardado(articulo.Codigo!.Value);
        Assert.Equal(1850m, guardado.PrecioVenta);
        Assert.Equal(5, guardado.Version);
        Assert.Equal(50m, _almacen.Guardada!.MultiploRedondeo);
        Assert.Equal(1, Articulos.Operaciones);
        Assert.Equal(1, Articulos.OperacionesConfirmadas);
        // D-03: la configuración y los precios en una sola confirmación, dentro de la operación.
        Assert.Equal(1, Articulos.Confirmaciones);
        Assert.All(Articulos.Accesos, acceso => Assert.True(acceso.DentroDeOperacion));
    }

    [Fact]
    public async Task Grabar_RecalculaInactivos()
    {
        var inactivo = Articulos.Sembrar("ABC-1", costo: 1210m, activo: false, version: 2);
        var activo = Articulos.Sembrar("ABC-2", costo: 1210m, version: 7);

        var resultado = await CrearCasoDeUso().EjecutarAsync(Validos(multiplo: 50m));

        Assert.True(resultado.EsGrabado);
        Assert.Equal(2, resultado.PreciosCambiados);
        var guardadoInactivo = Articulos.Guardado(inactivo.Codigo!.Value);
        Assert.Equal(1850m, guardadoInactivo.PrecioVenta);
        Assert.False(guardadoInactivo.Activo);
        Assert.Equal(3, guardadoInactivo.Version);
        Assert.Equal(8, Articulos.Guardado(activo.Codigo!.Value).Version);
    }

    [Fact]
    public async Task Grabar_SoloTope_NoRecalcula()
    {
        var articulo = Articulos.Sembrar("ABC-1", costo: 1210m, version: 4);

        var resultado = await CrearCasoDeUso().EjecutarAsync(Validos(tope: 4_500_000m));

        Assert.True(resultado.EsGrabado);
        Assert.Equal(0, resultado.PreciosCambiados);
        Assert.Equal(4_500_000m, _almacen.Guardada!.TopeIdentificacion);
        Assert.Equal(0, Articulos.ListadosParaRecalculo);
        Assert.Empty(Articulos.PreciosRecalculadosRegistrados);
        Assert.Equal(4, Articulos.Guardado(articulo.Codigo!.Value).Version);
        Assert.Equal(1, Articulos.OperacionesConfirmadas);
    }

    [Fact]
    public async Task Grabar_Alicuota10coma5_CantidadCero()
    {
        var primero = Articulos.Sembrar("ABC-1", costo: 1210m, version: 4);
        var segundo = Articulos.Sembrar("ABC-2", costo: 1105m, activo: false, version: 1);

        var resultado = await CrearCasoDeUso().EjecutarAsync(Validos(alicuota: 10.5m));

        Assert.True(resultado.EsGrabado);
        Assert.Equal(0, resultado.PreciosCambiados);
        Assert.Equal(10.5m, _almacen.Guardada!.AlicuotaIva);
        // D-22: se recorre el catálogo aunque ningún precio cambie, y ninguna versión se mueve.
        Assert.Equal(1, Articulos.ListadosParaRecalculo);
        Assert.Empty(Articulos.PreciosRecalculadosRegistrados);
        Assert.Equal(4, Articulos.Guardado(primero.Codigo!.Value).Version);
        Assert.Equal(1, Articulos.Guardado(segundo.Codigo!.Value).Version);
        Assert.Equal(1815m, Articulos.Guardado(primero.Codigo.Value).PrecioVenta);
        Assert.Equal(1, Articulos.OperacionesConfirmadas);
    }

    // ---- Grabaciones de FEAT-001c ----

    [Fact]
    public async Task Grabar_TopeNuevo_ObtenerDevuelveElNuevo()
    {
        var resultado = await CrearCasoDeUso().EjecutarAsync(Validos(tope: 4_500_000.50m));

        Assert.True(resultado.EsGrabado);
        Assert.Empty(resultado.Errores);
        var vigente = await CrearConsulta().EjecutarAsync();
        Assert.Equal(4_500_000.50m, vigente.TopeIdentificacion);
        Assert.False(vigente.FaltaTope);
        Assert.Equal(21m, vigente.AlicuotaIva);
        Assert.Equal("responsable_inscripto", vigente.CondicionFiscal);
        Assert.Equal(0.01m, vigente.MultiploRedondeo);
        Assert.Equal(1, Articulos.Confirmaciones);
    }

    [Fact]
    public async Task Grabar_Monotributo_ObtenerDevuelveMonotributo()
    {
        var resultado = await CrearCasoDeUso().EjecutarAsync(Validos(condicion: "monotributo"));

        Assert.True(resultado.EsGrabado);
        Assert.Equal(CondicionFiscal.Monotributo, _almacen.Guardada!.CondicionFiscal);
        Assert.Equal("monotributo", (await CrearConsulta().EjecutarAsync()).CondicionFiscal);
    }

    [Fact]
    public async Task Grabar_TopeAusente_GrabaSinTope()
    {
        // D-02 de FEAT-001c: también se puede vaciar un tope ya cargado.
        _almacen.Guardada = new ConfiguracionNegocio(21m, CondicionFiscal.ResponsableInscripto, 0.01m, 5_000_000m);

        var resultado = await CrearCasoDeUso().EjecutarAsync(Validos(tope: null));

        Assert.True(resultado.EsGrabado);
        var vigente = await CrearConsulta().EjecutarAsync();
        Assert.Null(vigente.TopeIdentificacion);
        Assert.True(vigente.FaltaTope);
    }

    // ---- Registro en el log (R-04 de FEAT-001c, D-11, D-17) ----

    [Fact]
    public async Task Grabar_Exitoso_RegistraValoresAnterioresYNuevos()
    {
        _almacen.Guardada = new ConfiguracionNegocio(21m, CondicionFiscal.ResponsableInscripto, 0.01m, null);
        Articulos.Sembrar("ABC-1", costo: 1210m);

        await CrearCasoDeUso().EjecutarAsync(Validos(
            alicuota: 10.5m, condicion: "monotributo", multiplo: 10m, tope: 4_500_000m));

        var entrada = Assert.Single(_registros.Entradas);
        Assert.Equal(LogLevel.Information, entrada.Nivel);
        Assert.StartsWith("Configuración de negocio grabada", entrada.Mensaje);
        Assert.Null(entrada.Error);
        Assert.Equal(21m, entrada.Valores["AlicuotaIvaAnterior"]);
        Assert.Equal("responsable_inscripto", entrada.Valores["CondicionFiscalAnterior"]);
        Assert.Equal(0.01m, entrada.Valores["MultiploRedondeoAnterior"]);
        Assert.Null(entrada.Valores["TopeIdentificacionAnterior"]);
        Assert.Equal(10.5m, entrada.Valores["AlicuotaIva"]);
        Assert.Equal("monotributo", entrada.Valores["CondicionFiscal"]);
        Assert.Equal(10m, entrada.Valores["MultiploRedondeo"]);
        Assert.Equal(4_500_000m, entrada.Valores["TopeIdentificacion"]);
        Assert.Equal(RelojFijo.Instante.UtcDateTime, entrada.Valores["FechaHoraUtc"]);
        // 1815 redondeado hacia arriba al múltiplo 10 da 1820.
        Assert.Equal(1, entrada.Valores["PreciosCambiados"]);
        Assert.Equal(1, Articulos.OperacionesConfirmadas);
    }

    [Fact]
    public async Task Grabar_RegistraEnElLogDespuesDeConfirmar()
    {
        Articulos.Sembrar("Zq7-proveedor", "Zq7-descripcion", 1210m);
        var confirmadasAlRegistrar = new List<int>();
        _registros.AlRegistrar = () => confirmadasAlRegistrar.Add(Articulos.OperacionesConfirmadas);

        var resultado = await CrearCasoDeUso().EjecutarAsync(Validos(multiplo: 50m));

        Assert.True(resultado.EsGrabado);
        // D-17: la categoría explícita, que appsettings.json envía al Registro de eventos.
        Assert.Equal(["Optica.Application.Configuracion.Grabacion"], _registros.Categorias);
        Assert.Equal("Optica.Application.Configuracion.Grabacion", GrabarConfiguracionConRecalculo.CategoriaRegistro);
        // D-11: una sola entrada, escrita con la operación externa ya confirmada.
        Assert.Equal([1], confirmadasAlRegistrar);
        var entrada = Assert.Single(_registros.Entradas);
        // Nunca códigos ni importes de artículos: solo la configuración, la fecha y la cantidad.
        Assert.Equal(
            [
                "AlicuotaIva", "AlicuotaIvaAnterior", "CondicionFiscal", "CondicionFiscalAnterior", "FechaHoraUtc",
                "MultiploRedondeo", "MultiploRedondeoAnterior", "PreciosCambiados", "TopeIdentificacion",
                "TopeIdentificacionAnterior", "{OriginalFormat}",
            ],
            entrada.Valores.Keys.Order(StringComparer.Ordinal));
        Assert.DoesNotContain("Zq7", entrada.Mensaje, StringComparison.Ordinal);
        Assert.DoesNotContain("1850", entrada.Mensaje, StringComparison.Ordinal);
        Assert.DoesNotContain("1815", entrada.Mensaje, StringComparison.Ordinal);
        Assert.DoesNotContain("1210", entrada.Mensaje, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Grabar_FueraDeRango_NoRegistraEnElLog()
    {
        Articulos.Sembrar("BORDE-1", costo: CostoAlBorde);

        var resultado = await CrearCasoDeUso().EjecutarAsync(Validos(multiplo: 50m));

        Assert.False(resultado.EsGrabado);
        Assert.Empty(_registros.Entradas);
    }

    [Fact]
    public async Task Grabar_DatosNulos_Lanza()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => CrearCasoDeUso().EjecutarAsync(null!));
    }
}
