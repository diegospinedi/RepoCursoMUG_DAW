namespace Optica.Application.Facturacion;

/// <summary>
/// Cómo terminó la consulta de un comprobante por número en ARCA (FR-04, FR-05, ADR-025):
/// <see cref="Autorizado"/> (con total, CAE, vencimiento y fecha), <see cref="NoAutorizado"/> (ARCA no tiene ese número),
/// <see cref="SinRespuesta"/> (se envió y no hubo respuesta legible dentro del plazo) o <see cref="NoConsultada"/> (falló
/// antes de enviar la consulta). Ningún resultado lleva el documento del receptor (D-14), y <see cref="ToString"/> no
/// muestra valores.
/// </summary>
public abstract class ResultadoConsulta
{
    private ResultadoConsulta()
    {
    }

    /// <summary>El nombre del caso, sin valores.</summary>
    public override string ToString() => GetType().Name;

    /// <summary>ARCA autorizó ese número: lo que la verificación compara y guarda (FR-05).</summary>
    public sealed class Autorizado : ResultadoConsulta
    {
        /// <exception cref="ArgumentException">
        /// El total es negativo, el CAE no son 14 dígitos o alguna fecha es la por defecto. El mensaje nombra el campo y
        /// no incluye el valor recibido.
        /// </exception>
        public Autorizado(decimal total, string cae, DateOnly vencimiento, DateOnly fecha)
        {
            if (total < 0m)
            {
                throw new ArgumentException("El total del comprobante no es válido.", nameof(total));
            }

            ArgumentNullException.ThrowIfNull(cae);
            if (cae.Length != ResultadoAutorizacion.Autorizada.LargoCae || !cae.All(char.IsAsciiDigit))
            {
                throw new ArgumentException("El CAE no es válido.", nameof(cae));
            }

            if (vencimiento == default)
            {
                throw new ArgumentException("El vencimiento del CAE no es válido.", nameof(vencimiento));
            }

            if (fecha == default)
            {
                throw new ArgumentException("La fecha del comprobante no es válida.", nameof(fecha));
            }

            Total = total;
            Cae = cae;
            Vencimiento = vencimiento;
            Fecha = fecha;
        }

        /// <summary>El importe total que autorizó ARCA (<c>ImpTotal</c>).</summary>
        public decimal Total { get; }

        /// <summary>El código de autorización electrónico: 14 dígitos (<c>CodAutorizacion</c>).</summary>
        public string Cae { get; }

        /// <summary>El vencimiento del CAE (<c>FchVto</c>).</summary>
        public DateOnly Vencimiento { get; }

        /// <summary>La fecha del comprobante (<c>CbteFch</c>).</summary>
        public DateOnly Fecha { get; }
    }

    /// <summary>ARCA no tiene ese número: no hay un comprobante autorizado con ese tipo, punto de venta y número.</summary>
    public sealed class NoAutorizado : ResultadoConsulta
    {
    }

    /// <summary>Se envió la consulta y no hubo respuesta legible dentro del plazo (AC-11): no se sabe nada nuevo.</summary>
    public sealed class SinRespuesta : ResultadoConsulta
    {
    }

    /// <summary>
    /// La consulta no llegó a enviarse (configuración incompleta, certificado, WSAA, sin plazo o cancelación antes de
    /// enviar).
    /// </summary>
    public sealed class NoConsultada : ResultadoConsulta
    {
        /// <param name="motivo">Un texto fijo para el log y la operadora, sin datos del cliente ni secretos.</param>
        /// <exception cref="ArgumentNullException"><paramref name="motivo"/> es null.</exception>
        public NoConsultada(string motivo)
        {
            ArgumentNullException.ThrowIfNull(motivo);
            Motivo = motivo;
        }

        public string Motivo { get; }
    }
}
