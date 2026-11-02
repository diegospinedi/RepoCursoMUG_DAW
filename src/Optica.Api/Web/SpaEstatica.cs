using Microsoft.Extensions.FileProviders;
using Optica.Application.Comun;

namespace Optica.Api.Web;

/// <summary>
/// Sirve la SPA compilada (la salida de <c>npm run build</c> de <c>web/</c>) desde <c>Optica:Web:Directorio</c>, de modo
/// que la Api y la interfaz son un solo desplegable. Va antes del ruteo y de la autorización: los archivos de la SPA
/// son anónimos y no contienen datos; los datos siguen protegidos por la Api.
/// </summary>
public static class SpaEstatica
{
    public const string ClaveDirectorio = "Optica:Web:Directorio";

    /// <summary>Valor por defecto, relativo a la carpeta raíz del contenido de la Api.</summary>
    public const string DirectorioPorDefecto = "wwwroot";

    /// <summary>
    /// Política de contenido de la SPA (R-11): scripts, estilos, imágenes y pedidos solo del mismo origen; nada en
    /// línea. La SPA compilada no usa scripts ni estilos en línea y aplica los colores de marca por CSSOM. Además, ningún
    /// sitio puede mostrarla dentro de un marco (clickjacking sobre las pantallas de contraseña), no se acepta un
    /// <c>&lt;base&gt;</c> que cambie el origen de las rutas relativas, los formularios solo se envían al mismo origen y
    /// no hay complementos (<c>object</c>/<c>embed</c>). <c>frame-ancestors</c>, <c>base-uri</c> y <c>form-action</c> no
    /// heredan de <c>default-src</c>: por eso van explícitas.
    /// </summary>
    public const string PoliticaContenido =
        "default-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'; object-src 'none'";

    private const string Indice = "index.html";

    /// <summary>
    /// Toda ruta que no es de <c>/api</c>: un archivo estático si existe; si no, para GET y HEAD sin extensión de
    /// archivo, <c>index.html</c> (las rutas las resuelve la SPA); cualquier otro caso, 404. Las rutas de <c>/api</c>
    /// nunca caen acá: siguen al ruteo, con 401 sin sesión y 404 con sesión si no existen. Si el directorio no existe
    /// o no tiene <c>index.html</c>, la Api arranca igual, registra una advertencia y la SPA responde 404. Lo mismo si
    /// el directorio es la carpeta de la aplicación (la raíz del contenido o la de los ensamblados) o una que la
    /// contiene: ahí se servirían sin sesión los ensamblados, los PDB y los <c>appsettings.*.json</c>. Y lo mismo si es
    /// la carpeta de datos (<see cref="IDatosLocales.Directorio"/>) o una que la contiene: ahí están la base y, en
    /// <c>claves/</c>, las claves de Data Protection que cifran la cookie de sesión.
    /// </summary>
    public static WebApplication UsarSpa(this WebApplication app)
    {
        var configurado = app.Configuration[ClaveDirectorio];
        var directorio = Path.GetFullPath(
            string.IsNullOrWhiteSpace(configurado) ? DirectorioPorDefecto : configurado,
            app.Environment.ContentRootPath);
        var registro = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(SpaEstatica));
        var datos = app.Services.GetRequiredService<IDatosLocales>().Directorio;

        PhysicalFileProvider? archivos = null;
        if (ContieneOEsIgual(directorio, app.Environment.ContentRootPath)
            || ContieneOEsIgual(directorio, AppContext.BaseDirectory))
        {
            registro.LogWarning(
                "La clave de configuración {Clave} apunta a la carpeta de la aplicación o a una que la contiene: no se " +
                "sirve la interfaz web (las pantallas responden 404) para no exponer la configuración ni los " +
                "ensamblados. Configurá la carpeta de la interfaz compilada (por ejemplo, wwwroot).",
                ClaveDirectorio);
        }
        else if (ContieneOEsIgual(directorio, datos))
        {
            // Sin la ruta: la carpeta de datos puede estar dentro de la del usuario.
            registro.LogWarning(
                "La clave de configuración {Clave} apunta a la carpeta de datos o a una que la contiene: no se sirve la " +
                "interfaz web (las pantallas responden 404) para no exponer la base ni las claves de la sesión. " +
                "Configurá la carpeta de la interfaz compilada (por ejemplo, wwwroot).",
                ClaveDirectorio);
        }
        else if (File.Exists(Path.Combine(directorio, Indice)))
        {
            archivos = new PhysicalFileProvider(directorio);
            app.Lifetime.ApplicationStopped.Register(archivos.Dispose);
        }
        else
        {
            // La ruta pasa por RutasPrivadas: la carpeta de la aplicación puede estar dentro de la del usuario.
            registro.LogWarning(
                "No se encontró la interfaz web compilada ({Indice}) en {Ruta}: las pantallas responden 404 y la " +
                "Api sigue funcionando. Revisá la clave de configuración {Clave}.",
                Indice,
                RutasPrivadas.Ocultar(directorio),
                ClaveDirectorio);
        }

        app.UseWhen(
            contexto => !contexto.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase),
            rama =>
            {
                rama.Use((contexto, siguiente) =>
                {
                    contexto.Response.Headers.ContentSecurityPolicy = PoliticaContenido;
                    contexto.Response.Headers.XContentTypeOptions = "nosniff";
                    return siguiente(contexto);
                });
                if (archivos is not null)
                {
                    rama.UseStaticFiles(new StaticFileOptions
                    {
                        FileProvider = archivos,
                        // index.html pedido directo también se revalida, igual que por el fallback.
                        OnPrepareResponse = archivo =>
                        {
                            if (string.Equals(archivo.File.Name, Indice, StringComparison.OrdinalIgnoreCase))
                            {
                                archivo.Context.Response.Headers.CacheControl = "no-cache";
                            }
                        },
                    });
                }

                rama.Run(contexto => ResponderIndiceAsync(contexto, archivos));
            });
        return app;
    }

    private static async Task ResponderIndiceAsync(HttpContext contexto, IFileProvider? archivos)
    {
        var pedido = contexto.Request;
        var esLectura = HttpMethods.IsGet(pedido.Method) || HttpMethods.IsHead(pedido.Method);
        // Un archivo que no existe (por ejemplo, /assets/viejo.js) no recibe index.html: sería HTML con otro tipo.
        var pareceArchivo = Path.HasExtension(pedido.Path.Value);
        // index.html se busca en cada pedido: puede haber desaparecido con la Api en marcha (wwwroot reemplazado).
        var indice = archivos?.GetFileInfo(Indice);
        if (indice is null || !indice.Exists || indice.IsDirectory || !esLectura || pareceArchivo)
        {
            contexto.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        contexto.Response.ContentType = "text/html; charset=utf-8";
        // Siempre se revalida: después de instalar una versión nueva, el navegador pide los archivos nuevos.
        contexto.Response.Headers.CacheControl = "no-cache";
        if (HttpMethods.IsHead(pedido.Method))
        {
            // HEAD: los mismos encabezados que GET, sin cuerpo.
            contexto.Response.ContentLength = indice.Length;
            return;
        }

        try
        {
            await contexto.Response.SendFileAsync(indice);
        }
        catch (Exception error) when (
            error is FileNotFoundException or DirectoryNotFoundException && !contexto.Response.HasStarted)
        {
            // Borrado entre la comprobación y el envío: 404, sin el tipo ni el caché de index.html.
            contexto.Response.Clear();
            contexto.Response.StatusCode = StatusCodes.Status404NotFound;
        }
    }

    /// <summary>
    /// Verdadero si <paramref name="directorio"/> es <paramref name="otra"/> o una carpeta que la contiene. En Windows
    /// las rutas se comparan sin distinguir mayúsculas.
    /// </summary>
    private static bool ContieneOEsIgual(string directorio, string otra)
    {
        var comparacion = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var contenedor = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directorio));
        var contenida = Path.TrimEndingDirectorySeparator(Path.GetFullPath(otra));
        if (string.Equals(contenedor, contenida, comparacion))
        {
            return true;
        }

        // La raíz del disco conserva su separador final; el resto se completa para no confundir "App" con "App2".
        var prefijo = Path.EndsInDirectorySeparator(contenedor) ? contenedor : contenedor + Path.DirectorySeparatorChar;
        return contenida.StartsWith(prefijo, comparacion);
    }
}
