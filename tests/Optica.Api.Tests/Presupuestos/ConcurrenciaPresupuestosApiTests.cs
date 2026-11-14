using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Optica.Domain.Catalogo;
using Optica.Infrastructure.Persistencia;
using Optica.Pruebas.Compartido;

namespace Optica.Api.Tests.Presupuestos;

/// <summary>
/// Dos sesiones graban presupuestos a la vez por la Api (FR-01, NFR-01 y AC-01 de FEAT-003d): con el encuentro forzado de
/// <see cref="EncuentroDeAltas"/> (D-04, D-06), la primera alta queda retenida dentro de su transacción hasta que la
/// conexión de la segunda choca con la base ocupada; la segunda espera la escritura y toma el número siguiente. Los
/// datos del cliente son ficticios.
/// </summary>
public sealed class ConcurrenciaPresupuestosApiTests
{
    private const string TextoArticuloNoDisponible = "Ese artículo ya no está disponible. Quitá la línea y buscalo de nuevo";

    /// <summary>
    /// AC-01 por la Api: dos sesiones (dos ingresos, dos cookies) graban a la vez; las dos reciben 201 con números N y
    /// N + 1, y el GET de cada uno devuelve lo que se envió.
    /// </summary>
    [Fact]
    public async Task DosSesiones_PostSimultaneos_NumerosConsecutivos()
    {
        using var sesiones = new SesionesConEncuentro();
        await sesiones.IngresarAsync();
        var codigo = await sesiones.GrabarArticuloAsync("CON-1", "Armazón concurrente", 1210m, 50m);
        var anterior = await sesiones.AltaSinEncuentroAsync(SesionesConEncuentro.Cuerpo("Previez", "99999920", 1, 0m, codigo));
        var cuerpoA = SesionesConEncuentro.Cuerpo("Ficticiez", "99999921", 3, 10m, codigo);
        var cuerpoB = SesionesConEncuentro.Cuerpo("Quimerez", "99999922", 5, 50m, codigo);

        var respuestas = await sesiones.DosAltasConEncuentroAsync(cuerpoA, cuerpoB);
        using var primera = respuestas.Primera;
        using var segunda = respuestas.Segunda;

        Assert.Equal(HttpStatusCode.Created, primera.StatusCode);
        Assert.Equal(HttpStatusCode.Created, segunda.StatusCode);
        var jsonA = await Http.JsonAsync(primera);
        var jsonB = await Http.JsonAsync(segunda);
        Assert.Equal(anterior + 1, jsonA.GetProperty("numero").GetInt64());
        Assert.Equal(anterior + 2, jsonB.GetProperty("numero").GetInt64());

        // El GET de cada uno es su respuesta del alta, con el cliente y la línea que se enviaron.
        foreach (var (json, apellido, dni, cantidad, descuento, cookie) in new[]
        {
            (jsonA, "Ficticiez", "99999921", 3, 10m, sesiones.CookieA),
            (jsonB, "Quimerez", "99999922", 5, 50m, sesiones.CookieB),
        })
        {
            var leido = await sesiones.LeerAsync(json.GetProperty("numero").GetInt64(), cookie);
            Assert.Equal(json.GetRawText(), leido.GetRawText());
            var cliente = leido.GetProperty("cliente");
            Assert.Equal(apellido, cliente.GetProperty("apellido").GetString());
            Assert.Equal(dni, cliente.GetProperty("dni").GetString());
            Assert.Equal("cliente@example.com", cliente.GetProperty("email").GetString());
            Assert.Equal("00 0000-0000", cliente.GetProperty("telefono").GetString());
            var linea = Assert.Single(leido.GetProperty("lineas").EnumerateArray());
            Assert.Equal(codigo, linea.GetProperty("codigoArticulo").GetInt64());
            Assert.Equal(cantidad, linea.GetProperty("cantidad").GetInt32());
            Assert.Equal(descuento, linea.GetProperty("porcentajeDescuento").GetDecimal());
            Assert.Equal("borrador", leido.GetProperty("estado").GetString());
        }
    }

    /// <summary>
    /// Sad path: mientras una sesión graba, la otra pide un alta con un artículo inactivo. El rechazo del catálogo ocurre
    /// dentro de la transacción (D-14 de FEAT-003a), así que espera la escritura; después responde 400 en
    /// <c>lineas[0]</c> sin grabar, y el alta siguiente toma el número que sigue al de la otra sesión, sin hueco.
    /// </summary>
    [Fact]
    public async Task DosSesiones_UnaRechazada_NumeroSinHueco()
    {
        using var sesiones = new SesionesConEncuentro();
        await sesiones.IngresarAsync();
        var activo = await sesiones.GrabarArticuloAsync("CON-1", "Armazón concurrente", 1210m, 50m);
        var inactivo = await sesiones.GrabarArticuloAsync("CON-2", "Estuche dado de baja", 500m, 0m);
        await sesiones.DesactivarArticuloAsync(inactivo);
        var anterior = await sesiones.AltaSinEncuentroAsync(SesionesConEncuentro.Cuerpo("Previez", "99999923", 1, 0m, activo));

        var respuestas = await sesiones.DosAltasConEncuentroAsync(
            SesionesConEncuentro.Cuerpo("Ficticiez", "99999924", 1, 0m, activo),
            SesionesConEncuentro.Cuerpo("Quimerez", "99999925", 1, 0m, inactivo));
        using var valida = respuestas.Primera;
        using var rechazada = respuestas.Segunda;

        Assert.Equal(HttpStatusCode.Created, valida.StatusCode);
        Assert.Equal(anterior + 1, (await Http.JsonAsync(valida)).GetProperty("numero").GetInt64());
        Assert.Equal(HttpStatusCode.BadRequest, rechazada.StatusCode);
        Assert.Equal("application/problem+json", rechazada.Content.Headers.ContentType?.MediaType);
        var errores = (await Http.JsonAsync(rechazada)).GetProperty("errors");
        Assert.Equal(["lineas[0]"], errores.EnumerateObject().Select(campo => campo.Name).ToArray());
        Assert.Equal(
            [TextoArticuloNoDisponible],
            errores.GetProperty("lineas[0]").EnumerateArray().Select(valor => valor.GetString()!).ToArray());
        Assert.DoesNotContain("99999925", await rechazada.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // El rechazo no grabó nada: el alta siguiente de la sesión rechazada toma el número que sigue, sin hueco.
        await sesiones.AfirmarSinPresupuestoAsync(anterior + 2);
        var siguiente = await sesiones.AltaSinEncuentroAsync(
            SesionesConEncuentro.Cuerpo("Quimerez", "99999925", 1, 0m, activo), sesiones.CookieB);
        Assert.Equal(anterior + 2, siguiente);
    }
}

/// <summary>
/// La Api con <see cref="EncuentroDeAltas"/> registrado por el parámetro <c>reemplazos</c> de <see cref="FabricaApi"/>:
/// el decorador de artículos en lugar del repositorio, el coordinador como singleton y, por <c>ConfigureDbContext</c>, el
/// busy handler y las marcas de transacción en cada contexto. Dos sesiones con cookie propia (ADR-001). La preparación
/// (contraseña, artículos, altas previas) se hace con el coordinador desarmado.
/// </summary>
internal sealed class SesionesConEncuentro : IDisposable
{
    public const string Ruta = "/api/presupuestos";
    private const string RutaArticulos = "/api/articulos";

    /// <summary>El tope de cada encuentro y de la espera de las dos altas.</summary>
    public static readonly TimeSpan Tope = TimeSpan.FromSeconds(30);

    private readonly ConcurrentQueue<MarcasDeTransaccion> _marcas = new();

    public SesionesConEncuentro()
    {
        Encuentro = new EncuentroDeAltas();
        var encuentro = Encuentro;
        var marcas = _marcas;
        Fabrica = new FabricaApi(servicios =>
        {
            servicios.AddSingleton(encuentro);
            servicios.RemoveAll<IArticuloRepository>();
            servicios.AddScoped<IArticuloRepository, ArticulosConEncuentro>();
            // Las opciones del contexto son por alcance: cada pedido tiene sus marcas de transacción.
            servicios.ConfigureDbContext<OpticaDbContext>((_, opciones) =>
            {
                var propias = new MarcasDeTransaccion(encuentro);
                marcas.Enqueue(propias);
                opciones.AddInterceptors(new BaseOcupadaAlAbrir(encuentro, "api"), propias);
            });
        });
        ClienteA = Fabrica.CrearClienteSinCookies();
        ClienteB = Fabrica.CrearClienteSinCookies();
    }

    public FabricaApi Fabrica { get; }

    public EncuentroDeAltas Encuentro { get; }

    public HttpClient ClienteA { get; }

    public HttpClient ClienteB { get; }

    public string CookieA { get; private set; } = string.Empty;

    public string CookieB { get; private set; } = string.Empty;

    /// <summary>La primera sesión define la contraseña; la segunda ingresa con ella: dos cookies distintas.</summary>
    public async Task IngresarAsync()
    {
        CookieA = await Http.DefinirAsync(ClienteA);
        using var ingreso = await Http.PostJsonAsync(
            ClienteB, "/api/acceso/ingresar", Http.CuerpoContrasena(Http.Contrasena));
        Assert.True(ingreso.IsSuccessStatusCode, $"El ingreso de la segunda sesión respondió {ingreso.StatusCode}.");
        CookieB = Http.Cookie(ingreso) ?? throw new InvalidOperationException("El ingreso no emitió la cookie de sesión.");
        Assert.NotEqual(CookieA, CookieB);
    }

    /// <summary>
    /// Arma el encuentro, lanza el alta de la sesión A, espera a que quede retenida dentro de su transacción y lanza la de
    /// la sesión B en otro hilo (su <c>BEGIN IMMEDIATE</c> bloquea el hilo que lo llama). Afirma el orden: B chocó con la
    /// base ocupada mientras A tenía la escritura y antes de que confirmara, y la transacción de B empezó después de esa
    /// confirmación. Desarma en un <c>finally</c>; si la prueba falló antes de que terminaran las dos altas, libera la
    /// retenida, las espera con el tope sin propagar sus errores y descarta las respuestas, así no quedan tareas colgadas.
    /// Quien llama descarta las dos respuestas devueltas.
    /// </summary>
    public async Task<(HttpResponseMessage Primera, HttpResponseMessage Segunda)> DosAltasConEncuentroAsync(
        string cuerpoA, string cuerpoB)
    {
        Task<HttpResponseMessage>? tareaA = null;
        Task<HttpResponseMessage>? tareaB = null;
        var devueltas = false;
        Encuentro.Armar(Tope);
        try
        {
            var desde = Encuentro.SiguienteMarca();
            tareaA = Http.PostJsonAsync(ClienteA, Ruta, cuerpoA, CookieA);
            await Encuentro.PrimeraRetenida.WaitAsync(Tope);
            tareaB = Task.Run(() => Http.PostJsonAsync(ClienteB, Ruta, cuerpoB, CookieB));

            // Si la segunda respondiera sin chocar con la base ocupada, no habría esperado la escritura de la primera.
            await Task.WhenAny(Encuentro.BaseOcupada, tareaB).WaitAsync(Tope);
            Assert.True(
                Encuentro.BaseOcupada.IsCompletedSuccessfully,
                "La segunda alta respondió sin esperar la escritura de la primera.");
            var respuestas = await Task.WhenAll(tareaA, tareaB).WaitAsync(Tope);

            // Solo las dos altas abrieron transacciones desde que se armó el encuentro.
            var transacciones = _marcas
                .Where(m => m.Inicio > desde)
                .OrderBy(m => m.Inicio)
                .ToList();
            Assert.Equal(2, transacciones.Count);
            var senal = Assert.NotNull(Encuentro.MarcaBaseOcupada);
            var inicioA = transacciones[0].Inicio!.Value;
            var confirmacionA = Assert.NotNull(transacciones[0].Confirmacion);
            var inicioB = transacciones[1].Inicio!.Value;
            Assert.True(inicioA < senal, "La primera no tenía la escritura cuando la segunda chocó.");
            Assert.True(senal < confirmacionA, "La señal de base ocupada no precede a la confirmación de la primera.");
            Assert.True(confirmacionA < inicioB, "La transacción de la segunda empezó antes de que la primera confirmara.");
            devueltas = true;
            return (respuestas[0], respuestas[1]);
        }
        finally
        {
            var pendientes = new[] { tareaA, tareaB }.OfType<Task<HttpResponseMessage>>().ToArray();
            if (pendientes.Any(tarea => !tarea.IsCompleted))
            {
                // La retenida solo se libera con la señal o con el tope: se da la señal (sin efecto si ya la hubo) antes de
                // desarmar, así no se esperan los 30 s.
                Encuentro.SenalarBaseOcupada("finally");
            }

            Encuentro.Desarmar();
            if (!devueltas)
            {
                await EsperarSinErroresAsync(pendientes);
            }
        }
    }

    /// <summary>Espera las altas lanzadas con el tope, sin propagar sus errores, y descarta las respuestas que hubo.</summary>
    private static async Task EsperarSinErroresAsync(Task<HttpResponseMessage>[] tareas)
    {
        try
        {
            await Task.WhenAll(tareas).WaitAsync(Tope);
        }
        catch (Exception)
        {
            // La prueba ya falló por su aserción; acá solo se espera que terminen.
        }

        foreach (var tarea in tareas.Where(tarea => tarea.IsCompletedSuccessfully))
        {
            tarea.Result.Dispose();
        }
    }

    /// <summary>Un alta sin encuentro (desarmado), por la sesión indicada (A por defecto); devuelve su número.</summary>
    public async Task<long> AltaSinEncuentroAsync(string cuerpo, string? cookie = null)
    {
        using var respuesta = await Http.PostJsonAsync(ClienteA, Ruta, cuerpo, cookie ?? CookieA);
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return (await Http.JsonAsync(respuesta)).GetProperty("numero").GetInt64();
    }

    public async Task<long> GrabarArticuloAsync(
        string codigoProveedor, string descripcion, decimal precioCosto, decimal margenUtilidad)
    {
        using var respuesta = await Http.PostJsonAsync(
            ClienteA,
            RutaArticulos,
            JsonSerializer.Serialize(new { codigoProveedor, descripcion, precioCosto, margenUtilidad }),
            CookieA);
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return (await Http.JsonAsync(respuesta)).GetProperty("codigo").GetInt64();
    }

    public async Task DesactivarArticuloAsync(long codigo)
    {
        using var respuesta = await Http.EnviarAsync(
            ClienteA,
            HttpMethod.Put,
            $"{RutaArticulos}/{codigo}/estado",
            new StringContent("{\"activo\":false}", System.Text.Encoding.UTF8, "application/json"),
            CookieA);
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
    }

    public async Task<JsonElement> LeerAsync(long numero, string cookie)
    {
        using var respuesta = await Http.GetAsync(ClienteA, $"{Ruta}/{numero}", cookie);
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        // FEAT-007 (D-28): el GET suma factura y emisionPendiente; sin ellas es la respuesta del alta tal cual.
        return Http.SinFacturacion(await Http.JsonAsync(respuesta));
    }

    public async Task AfirmarSinPresupuestoAsync(long numero)
    {
        using var respuesta = await Http.GetAsync(ClienteA, $"{Ruta}/{numero}", CookieA);
        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>
    /// Un alta de una línea con cliente ficticio: DNI de la serie 99.999.9xx, email de example.com y teléfono
    /// 00 0000-0000.
    /// </summary>
    public static string Cuerpo(string apellido, string dni, int cantidad, decimal porcentajeDescuento, long codigo) =>
        JsonSerializer.Serialize(new
        {
            cliente = new
            {
                apellido,
                nombre = "Prueboncia",
                dni,
                domicilio = (string?)null,
                email = "cliente@example.com",
                telefono = "00 0000-0000",
            },
            lineas = new[] { new { codigoArticulo = codigo, cantidad, porcentajeDescuento } },
        });

    public void Dispose()
    {
        ClienteA.Dispose();
        ClienteB.Dispose();
        Fabrica.Dispose();
    }
}
