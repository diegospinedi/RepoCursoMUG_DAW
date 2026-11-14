using Optica.Domain.Facturacion;

namespace Optica.Application.Facturacion;

/// <summary>
/// Cómo terminó un pedido de autorización a ARCA (D-16): <see cref="Autorizada"/>, <see cref="Rechazada"/>,
/// <see cref="NoEnviada"/> (falló antes de enviar el pedido de autorización) o <see cref="SinRespuesta"/> (se envió y no
/// hubo respuesta legible dentro del plazo). Solo "sin respuesta" deja la emisión pendiente (FR-23, FR-24). Ningún
/// resultado lleva datos del cliente.
/// </summary>
public abstract class ResultadoAutorizacion
{
    private ResultadoAutorizacion()
    {
    }

    /// <summary>ARCA autorizó el comprobante: número asignado, CAE y su vencimiento (FR-17, FR-18).</summary>
    public sealed class Autorizada : ResultadoAutorizacion
    {
        /// <exception cref="ArgumentException">
        /// El número no está entre 1 y 99.999.999, el CAE no son 14 dígitos o el vencimiento es el por defecto. El
        /// mensaje nombra el campo y no incluye el valor recibido.
        /// </exception>
        public Autorizada(long numero, string cae, DateOnly vencimiento)
        {
            if (numero is < NumeroComprobante.NumeroMinimo or > NumeroComprobante.NumeroMaximo)
            {
                throw new ArgumentException("El número de comprobante no es válido.", nameof(numero));
            }

            ArgumentNullException.ThrowIfNull(cae);
            if (cae.Length != LargoCae || !cae.All(char.IsAsciiDigit))
            {
                throw new ArgumentException("El CAE no es válido.", nameof(cae));
            }

            if (vencimiento == default)
            {
                throw new ArgumentException("El vencimiento del CAE no es válido.", nameof(vencimiento));
            }

            Numero = numero;
            Cae = cae;
            Vencimiento = vencimiento;
        }

        /// <summary>Los dígitos de un CAE.</summary>
        public const int LargoCae = 14;

        /// <summary>El número que asignó ARCA.</summary>
        public long Numero { get; }

        /// <summary>El código de autorización electrónico: 14 dígitos.</summary>
        public string Cae { get; }

        public DateOnly Vencimiento { get; }
    }

    /// <summary>ARCA rechazó la solicitud: su código y su descripción (FR-21, AC-20).</summary>
    public sealed class Rechazada : ResultadoAutorizacion
    {
        /// <exception cref="ArgumentNullException"><paramref name="descripcion"/> es null.</exception>
        public Rechazada(int codigo, string descripcion)
        {
            ArgumentNullException.ThrowIfNull(descripcion);
            Codigo = codigo;
            Descripcion = descripcion;
        }

        public int Codigo { get; }

        public string Descripcion { get; }
    }

    /// <summary>
    /// El pedido de autorización no llegó a enviarse (configuración incompleta, certificado, WSAA, último número o
    /// cancelación antes de enviar): no se emitió nada y se puede volver a intentar.
    /// </summary>
    public sealed class NoEnviada : ResultadoAutorizacion
    {
        /// <param name="motivo">Un texto fijo para el log y la operadora, sin datos del cliente ni secretos.</param>
        /// <exception cref="ArgumentNullException"><paramref name="motivo"/> es null.</exception>
        public NoEnviada(string motivo)
        {
            ArgumentNullException.ThrowIfNull(motivo);
            Motivo = motivo;
        }

        public string Motivo { get; }
    }

    /// <summary>
    /// Se envió el pedido de autorización y no hubo respuesta legible dentro del plazo (FR-23, AC-21): no se sabe si
    /// ARCA autorizó, así que la emisión queda pendiente y no se reintenta.
    /// </summary>
    public sealed class SinRespuesta : ResultadoAutorizacion
    {
    }
}
