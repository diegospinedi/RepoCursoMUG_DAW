using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.Net.Http.Headers;

namespace Optica.Api.Acceso;

/// <summary>
/// Controles del cuerpo antes de ocupar un lugar del limitador y antes de leerlo (R-04, R-05). Va después de la
/// autorización: un pedido sin sesión a un endpoint protegido recibe 401 antes que cualquiera de estos códigos.
/// </summary>
public static class LimitesDelCuerpo
{
    /// <summary>
    /// Todo POST exige <c>Content-Type: application/json</c> (con o sin <c>charset</c>), aun sin cuerpo: otro tipo, o
    /// ninguno, 415. Con un <c>[RequestSizeLimit]</c> en el endpoint, un <c>Content-Length</c> mayor responde 413 sin
    /// leer el cuerpo, y un cuerpo sin <c>Content-Length</c> (en partes) se corta con 413 al pasar el límite.
    /// </summary>
    public static Task AplicarAsync(HttpContext contexto, RequestDelegate siguiente)
    {
        var pedido = contexto.Request;
        if (HttpMethods.IsPost(pedido.Method) && !EsJson(pedido.ContentType))
        {
            contexto.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
            return Task.CompletedTask;
        }

        var limite = contexto.GetEndpoint()?.Metadata.GetMetadata<IRequestSizeLimitMetadata>()?.MaxRequestBodySize;
        if (limite is { } maximo)
        {
            if (pedido.ContentLength > maximo)
            {
                contexto.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                return Task.CompletedTask;
            }

            if (pedido.ContentLength is null)
            {
                pedido.Body = new FlujoLimitado(pedido.Body, maximo);
            }
        }

        return siguiente(contexto);
    }

    private static bool EsJson(string? tipo) =>
        MediaTypeHeaderValue.TryParse(tipo, out var valor)
        && valor.MediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Lectura que lanza <see cref="BadHttpRequestException"/> 413 al pasar el límite; el manejador de errores la
    /// responde con ese código, sin registrarla.
    /// </summary>
    private sealed class FlujoLimitado(Stream origen, long maximo) : Stream
    {
        private long _leidos;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Contar(origen.Read(buffer, offset, count));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            Contar(await origen.ReadAsync(buffer, cancellationToken));

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private int Contar(int leidos)
        {
            _leidos += leidos;
            if (_leidos > maximo)
            {
                throw new BadHttpRequestException(
                    "El cuerpo del pedido supera el tamaño permitido.", StatusCodes.Status413PayloadTooLarge);
            }

            return leidos;
        }
    }
}
