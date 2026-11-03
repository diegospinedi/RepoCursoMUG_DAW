using Optica.Application.Acceso;
using Optica.Application.Comun;

namespace Optica.Api.Acceso;

/// <summary>
/// El comando <c>Optica.Api restablecer-contrasena</c> (FR-04, ADR-007), que se ejecuta en la PC del local con la cuenta
/// de la Api y la Api detenida. En este orden, y antes de cualquier conexión que pueda crear la base: verifica que
/// existan el directorio de datos y la base, sin crear nada (R-09); toma el candado de instancia única y lo mantiene
/// hasta terminar, así no corre con la Api en marcha ni la Api arranca en el medio (R-13); se niega si hay migraciones
/// pendientes (no migra). Después pide escribir <see cref="Confirmacion"/> y restablece.
/// <para>
/// Toda la salida usa <see cref="IDatosLocales.RutaVisible"/> y describe las fallas con
/// <see cref="IDatosLocales.DescribirFalla"/> o solo el tipo de la excepción, nunca su texto ni su traza: traen la ruta
/// expandida con el usuario de Windows (R-10).
/// </para>
/// </summary>
public sealed class ComandoRestablecerContrasena(
    RestablecerContrasena restablecer,
    IDatosLocales datos,
    ICandadoInstancia candado,
    TextReader entrada,
    TextWriter salida)
{
    /// <summary>El primer (y único) argumento de la línea de comandos que ejecuta el comando.</summary>
    public const string Nombre = "restablecer-contrasena";

    /// <summary>Lo que hay que escribir para confirmar, sin distinguir los espacios alrededor.</summary>
    public const string Confirmacion = "RESTABLECER";

    public const int CodigoRestablecida = 0;
    public const int CodigoCancelada = 1;
    public const int CodigoFallo = 2;

    public const string MensajeEnMarcha = "La aplicación está en marcha. Detenela antes de restablecer la contraseña.";
    public const string MensajeMigracionesPendientes = "Iniciá la aplicación una vez antes de restablecer.";

    public async Task<int> EjecutarAsync(CancellationToken cancelacion = default)
    {
        // 1. Solo mira: si la base no está (otra cuenta, otro directorio), no se crea nada.
        if (!datos.BaseExiste)
        {
            return await FallarAsync(MensajeBaseInexistente());
        }

        // 2. El candado: tomado por la Api (o sin permiso sobre él, por ejemplo de otra cuenta) es "en marcha".
        IDisposable? tomado;
        try
        {
            tomado = candado.IntentarAdquirir();
        }
        catch (DirectoryNotFoundException)
        {
            return await FallarAsync(MensajeBaseInexistente());
        }
        catch (Exception error)
        {
            return await FallarAsync(
                $"No se pudo comprobar si la aplicación está en marcha ({error.GetType().Name}). No se cambió nada.");
        }

        if (tomado is null)
        {
            return await FallarAsync(MensajeEnMarcha);
        }

        using (tomado)
        {
            // 3. Sin migrar: la base tiene que estar al día con esta versión.
            try
            {
                if (datos.MigracionesPendientes())
                {
                    return await FallarAsync(MensajeMigracionesPendientes);
                }
            }
            catch (Exception error)
            {
                return await FallarAsync(DescribirFalla(error));
            }

            await salida.WriteLineAsync(
                $"Se va a restablecer la contraseña de acceso de la base en {datos.RutaVisible}: se elimina la contraseña, " +
                "se registra el restablecimiento y se cierran todas las sesiones abiertas. No se modifica ningún otro dato.");
            await salida.WriteLineAsync($"Escribí {Confirmacion} para confirmar, o cualquier otra cosa para cancelar:");
            var respuesta = await entrada.ReadLineAsync(cancelacion);
            if (!string.Equals(respuesta?.Trim(), Confirmacion, StringComparison.Ordinal))
            {
                await salida.WriteLineAsync($"Restablecimiento cancelado: no se escribió {Confirmacion}. No se cambió nada.");
                return CodigoCancelada;
            }

            IntentoRestablecimiento resultado;
            try
            {
                resultado = await restablecer.EjecutarAsync(cancelacion);
            }
            catch (Exception error)
            {
                return await FallarAsync(DescribirFalla(error));
            }

            if (resultado.Tipo == ResultadoRestablecimiento.ClavesSinRenovar)
            {
                return await FallarAsync(
                    $"La contraseña se restableció, pero no se pudieron renovar las claves de sesión ({resultado.TipoError}): " +
                    "las sesiones abiertas podrían seguir valiendo. Repetí el comando.");
            }

            await salida.WriteLineAsync(resultado.HabiaCredencial
                ? "Contraseña restablecida. Las sesiones abiertas se cerraron."
                : "No había una contraseña definida. Se registró el restablecimiento y las sesiones abiertas se cerraron.");
            await salida.WriteLineAsync(
                "Antes de habilitar el acceso desde la red, iniciá la aplicación solo en localhost y definí la contraseña " +
                "desde esta PC (docs/instalacion.md).");
            return CodigoRestablecida;
        }
    }

    private string MensajeBaseInexistente() =>
        $"No se encontró la base de datos en {datos.RutaVisible}. Ejecutá el comando con la cuenta de la Api.";

    /// <summary>Una falla de la base con su tipo y su código de SQLite; cualquier otra, solo con su tipo.</summary>
    private string DescribirFalla(Exception error)
    {
        if (datos.DescribirFalla(error) is { } falla)
        {
            var codigo = falla.CodigoError is { } numero ? $", código de error de SQLite {numero}" : string.Empty;
            return $"No se pudo restablecer la contraseña: falló la base {falla.Archivo} en {falla.Directorio} " +
                $"({falla.TipoError}{codigo}). No se cambió nada.";
        }

        return $"No se pudo restablecer la contraseña ({error.GetType().Name}). No se cambió nada.";
    }

    private async Task<int> FallarAsync(string mensaje)
    {
        await salida.WriteLineAsync(mensaje);
        return CodigoFallo;
    }
}
