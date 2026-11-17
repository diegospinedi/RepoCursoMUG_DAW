using System.Globalization;
using Optica.Api.Acceso;
using Optica.Application.Backup;
using Optica.Application.Comun;

namespace Optica.Api.Backup;

/// <summary>
/// El comando <c>Optica.Api restaurar-backup &lt;copia&gt;</c> (FR-13 de FEAT-010, ADR-030), que se ejecuta en la PC del
/// local con la cuenta de la Api y la Api detenida. Solo maneja la consola, el candado y la confirmación (D-27), como
/// <see cref="ComandoRestablecerContrasena"/>; la restauración la hace el caso de uso <see cref="RestaurarBackup"/>.
/// </summary>
/// <remarks>
/// <para>
/// En este orden: verifica que exista la base, sin crear nada; toma el candado de instancia única y lo mantiene hasta
/// terminar, así no corre con la Api en marcha ni la Api arranca en el medio (R-08); verifica la copia (nombre, integridad
/// y versión, D-28) y muestra el resultado y cuántas facturas con CAE de la base actual son posteriores a la última de la
/// copia (R-10); pide escribir <see cref="Confirmacion"/>; restaura; y recuerda revisar en ARCA los comprobantes emitidos
/// desde la fecha de la copia antes de volver a facturar (R-07, D-15) y conservar bajo llave la carpeta con la base
/// anterior (D-29). La restauración renueva las claves de sesión: todas las sesiones abiertas se cierran. Desde la
/// confirmación hasta el final de la restauración, Ctrl+C no corta el proceso. No migra: la Api migra al arrancar.
/// </para>
/// <para>
/// La salida nunca muestra rutas completas (D-12): ni la de la copia ni la de la carpeta de datos, que pueden llevar el
/// nombre de una persona. Muestra el nombre del archivo de la copia (que tiene el patrón fijo <c>optica-AAAA-MM-DD.db</c>),
/// su fecha y la carpeta anterior relativa a la carpeta de datos. Las fallas se describen con el tipo de la excepción,
/// nunca con su texto ni su traza.
/// </para>
/// </remarks>
public sealed class ComandoRestaurarBackup(
    RestaurarBackup restaurar,
    IDatosLocales datos,
    ICandadoInstancia candado,
    string rutaCopia,
    TextReader entrada,
    TextWriter salida,
    Func<IDisposable>? bloquearCorte = null)
{
    /// <summary>El primer argumento de la línea de comandos que ejecuta el comando; el segundo es la ruta de la copia.</summary>
    public const string Nombre = "restaurar-backup";

    /// <summary>Lo que hay que escribir para confirmar, sin distinguir los espacios alrededor.</summary>
    public const string Confirmacion = "RESTAURAR";

    public const int CodigoRestaurada = 0;
    public const int CodigoCancelada = 1;

    /// <summary>El código de falla, el mismo de <see cref="ComandoRestablecerContrasena"/>.</summary>
    public const int CodigoFallo = ComandoRestablecerContrasena.CodigoFallo;

    public const string MensajeEnMarcha = "La aplicación está en marcha. Detenela antes de restaurar un backup.";

    private const string NoSeCambioNada = "No se cambió nada.";

    /// <summary>
    /// La carpeta de las bases apartadas como la ve la operadora, relativa a la carpeta de datos (<c>anterior\</c> en
    /// Windows): nunca con la ruta de la carpeta de datos (D-12).
    /// </summary>
    private static readonly string CarpetaAnteriorVisible = "anterior" + Path.DirectorySeparatorChar;

    /// <summary>
    /// Lo que se pone mientras restaura para que Ctrl+C no corte el proceso; por defecto, <see cref="BloqueoCtrlC"/> sobre
    /// la consola. Las pruebas pasan uno propio.
    /// </summary>
    private readonly Func<IDisposable> _bloquearCorte = bloquearCorte ?? (() => new BloqueoCtrlC(salida));

    public async Task<int> EjecutarAsync(CancellationToken cancelacion = default)
    {
        // 1. Solo mira: si la base no está (otra cuenta, otra carpeta, instalación sin iniciar), no se crea nada.
        if (!datos.BaseExiste)
        {
            return await FallarAsync(MensajeBaseInexistente);
        }

        // 2. El candado: tomado por la Api (o sin permiso sobre él, por ejemplo de otra cuenta) es "en marcha".
        IDisposable? tomado;
        try
        {
            tomado = candado.IntentarAdquirir();
        }
        catch (DirectoryNotFoundException)
        {
            return await FallarAsync(MensajeBaseInexistente);
        }
        catch (Exception error)
        {
            return await FallarAsync(
                $"No se pudo comprobar si la aplicación está en marcha ({error.GetType().Name}). {NoSeCambioNada}");
        }

        if (tomado is null)
        {
            return await FallarAsync(MensajeEnMarcha);
        }

        using (tomado)
        {
            // 3. La copia, antes de tocar nada.
            VerificacionCopia copia;
            try
            {
                copia = await restaurar.VerificarAsync(rutaCopia, cancelacion);
            }
            catch (Exception error)
            {
                return await FallarAsync($"No se pudo verificar la copia ({error.GetType().Name}). {NoSeCambioNada}");
            }

            if (!copia.Valida)
            {
                return await FallarAsync(MensajeRechazo(copia));
            }

            await MostrarVerificacionAsync(copia);

            // 4. La confirmación.
            await salida.WriteLineAsync(
                "Se va a reemplazar la base actual por la de la copia. La base actual no se borra: se aparta a la carpeta " +
                $"{CarpetaAnteriorVisible} dentro de la carpeta de datos.");
            await salida.WriteLineAsync($"Escribí {Confirmacion} para confirmar, o cualquier otra cosa para cancelar:");
            var respuesta = await entrada.ReadLineAsync(cancelacion);
            if (!string.Equals(respuesta?.Trim(), Confirmacion, StringComparison.Ordinal))
            {
                await salida.WriteLineAsync($"Restauración cancelada: no se escribió {Confirmacion}. {NoSeCambioNada}");
                return CodigoCancelada;
            }

            // 5. La restauración, sin Ctrl+C: cortarla en el medio del intercambio es lo que hay que evitar (WARN-3).
            await salida.WriteLineAsync(
                "Restaurando la copia. No cierres esta ventana ni apagues la PC hasta que termine: puede tardar unos minutos.");
            ResultadoRestauracion resultado;
            try
            {
                using (_bloquearCorte())
                {
                    resultado = await restaurar.RestaurarAsync(rutaCopia, cancelacion);
                }
            }
            catch (Exception error)
            {
                // El caso de uso informa sus fallas en el resultado; esto es lo inesperado, y no se sabe en qué paso fue.
                return await FallarAsync(
                    $"No se pudo terminar la restauración ({error.GetType().Name}). La base actual pudo haberse apartado: " +
                    "antes de iniciar la aplicación, revisá la carpeta de datos con docs/instalacion.md, sección 10.");
            }

            if (!resultado.Hecha)
            {
                return await FallarAsync(MensajeFalla(resultado));
            }

            await MostrarRestauradaAsync(resultado);
            if (resultado.TipoErrorClaves is { } tipoClaves)
            {
                // Como restablecer: la base ya está restaurada (no se deshace), pero las sesiones de antes podrían seguir.
                return await FallarAsync(
                    $"La base se restauró, pero no se pudieron renovar las claves de sesión ({tipoClaves}): las sesiones " +
                    "abiertas antes de restaurar podrían seguir valiendo. Con la aplicación detenida, vaciá la carpeta " +
                    "claves de la carpeta de datos (docs/instalacion.md, sección 10).");
            }

            return CodigoRestaurada;
        }
    }

    private const string MensajeBaseInexistente =
        "No se encontró la base de datos en la carpeta de datos. Ejecutá el comando con la cuenta de la Api. En una " +
        "instalación nueva, iniciá la aplicación una vez, detenela y repetí el comando. " + NoSeCambioNada;

    private async Task MostrarVerificacionAsync(VerificacionCopia copia)
    {
        await salida.WriteLineAsync(
            $"Copia: {copia.NombreArchivo}, del {Fecha(copia.Fecha)}. Verificación: la copia está completa (integridad " +
            "correcta) y es de esta aplicación.");
        if (copia.RequiereActualizar)
        {
            await salida.WriteLineAsync(
                "La copia es de una versión anterior de la aplicación: se actualiza sola la primera vez que se inicie.");
        }

        await salida.WriteLineAsync(copia.FacturasPosteriores switch
        {
            null =>
                "Atención: no se pudo leer la base actual para contar sus facturas con CAE posteriores a la copia. Puede " +
                $"tener facturas que la copia no tiene: después de restaurar quedan solo en la carpeta {CarpetaAnteriorVisible}.",
            0 => "La base actual no tiene facturas con CAE posteriores a la última de la copia.",
            1 =>
                "Atención: la base actual tiene 1 factura con CAE posterior a la última de la copia. Después de restaurar " +
                $"queda solo en la carpeta {CarpetaAnteriorVisible}.",
            { } cantidad =>
                $"Atención: la base actual tiene {cantidad.ToString(CultureInfo.InvariantCulture)} facturas con CAE " +
                "posteriores a la última de la copia. Después de restaurar quedan solo en la carpeta " +
                $"{CarpetaAnteriorVisible}.",
        });
    }

    private async Task MostrarRestauradaAsync(ResultadoRestauracion resultado)
    {
        var fecha = Fecha(resultado.Copia.Fecha);
        await salida.WriteLineAsync(
            $"Base restaurada desde la copia {resultado.Copia.NombreArchivo}. La base anterior quedó en la carpeta " +
            $"{resultado.CarpetaAnterior} dentro de la carpeta de datos.");
        if (resultado.TipoErrorClaves is null)
        {
            await salida.WriteLineAsync("Todas las sesiones abiertas se cerraron: hay que volver a ingresar en cada equipo.");
        }

        await salida.WriteLineAsync(
            $"Antes de volver a facturar, revisá en ARCA los comprobantes emitidos desde el {fecha}: los que ARCA tiene y " +
            "la base restaurada no, ya están facturados y no se tienen que volver a facturar.");
        await salida.WriteLineAsync(
            $"Conservá la carpeta {resultado.CarpetaAnterior} bajo llave, como un backup, mientras tenga facturas que la " +
            "copia no tiene: guarda los datos personales de los clientes sin cifrar.");
        await salida.WriteLineAsync(
            "Iniciá la aplicación e ingresá con la contraseña que estaba vigente en la fecha de la copia " +
            "(docs/instalacion.md, sección 10).");
    }

    /// <summary>El rechazo de la copia; con un nombre fuera del patrón no se repite el nombre (puede llevar el de una persona).</summary>
    private static string MensajeRechazo(VerificacionCopia copia) => copia.Rechazo switch
    {
        RechazoCopia.NombreInvalido =>
            "El archivo elegido no es una copia de backup del sistema: su nombre tiene que ser optica-AAAA-MM-DD.db. " +
            NoSeCambioNada,
        RechazoCopia.Inexistente =>
            $"No se encontró la copia {copia.NombreArchivo}. Revisá la ruta y la unidad (pendrive o disco). {NoSeCambioNada}",
        RechazoCopia.Danada =>
            $"La copia {copia.NombreArchivo} está dañada o no es una base de datos: no pasó la verificación de integridad. " +
            $"Elegí otra copia. {NoSeCambioNada}",
        RechazoCopia.VersionMasNueva =>
            $"La copia {copia.NombreArchivo} es de una versión más nueva de la aplicación. Instalá esa versión antes de " +
            $"restaurarla. {NoSeCambioNada}",
        _ =>
            $"La copia {copia.NombreArchivo} no es una base del sistema de la óptica (no tiene el historial de versiones " +
            $"esperado). Elegí otra copia. {NoSeCambioNada}",
    };

    private static string MensajeFalla(ResultadoRestauracion resultado)
    {
        var tipo = resultado.TipoError is null ? string.Empty : $" ({resultado.TipoError})";
        return resultado.Falla switch
        {
            FallaRestauracion.Verificacion =>
                $"No se pudo verificar la copia{tipo}: la base actual no se modificó. Revisá que la copia siga disponible " +
                "(el pendrive conectado, el archivo de la nube descargado) y repetí el comando.",
            FallaRestauracion.CopiaRechazada => MensajeRechazo(resultado.Copia),
            FallaRestauracion.Temporal =>
                $"No se pudo restaurar la copia {resultado.Copia.NombreArchivo}{tipo}: la base actual no se tocó. Revisá " +
                "que haya espacio en el disco y repetí el comando.",
            FallaRestauracion.Intercambio =>
                $"No se pudo poner la base restaurada{tipo}: la base actual volvió a su lugar y no cambió. Repetí el comando.",
            _ =>
                $"No se pudo poner la base restaurada ni devolver la base actual{tipo}. La base actual está en la carpeta " +
                $"{resultado.CarpetaAnterior} dentro de la carpeta de datos: no inicies la aplicación y seguí los pasos de " +
                "docs/instalacion.md, sección 10, para volverla a su lugar.",
        };
    }

    private static string Fecha(DateOnly? fecha) =>
        fecha?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? string.Empty;

    private async Task<int> FallarAsync(string mensaje)
    {
        await salida.WriteLineAsync(mensaje);
        return CodigoFallo;
    }

    /// <summary>
    /// Mientras está puesto, Ctrl+C no termina el proceso (<see cref="ConsoleCancelEventArgs.Cancel"/>) y se avisa que la
    /// restauración no se puede cortar. Ctrl+Break no se puede cancelar en Windows y queda como está; cerrar la ventana
    /// tampoco se puede impedir (la guía pide no hacerlo, y la Api se niega a arrancar si queda la restauración a medias).
    /// </summary>
    private sealed class BloqueoCtrlC : IDisposable
    {
        public const string Aviso = "La restauración está en curso y no se puede cortar: esperá a que termine.";

        private readonly TextWriter _salida;

        public BloqueoCtrlC(TextWriter salida)
        {
            _salida = salida;
            Console.CancelKeyPress += AlCortar;
        }

        public void Dispose() => Console.CancelKeyPress -= AlCortar;

        private void AlCortar(object? emisor, ConsoleCancelEventArgs evento)
        {
            if (evento.SpecialKey == ConsoleSpecialKey.ControlC)
            {
                evento.Cancel = true;
                _salida.WriteLine(Aviso);
            }
        }
    }
}
