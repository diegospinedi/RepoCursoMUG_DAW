using Optica.Domain.Facturacion;

namespace Optica.Application.Facturacion;

/// <summary>
/// Cómo terminó el pedido del próximo número a ARCA (ADR-025): <see cref="Numero"/> (el último autorizado + 1) o
/// <see cref="NoEnviada"/>. La consulta del último número es de solo lectura en ARCA, así que cualquier falla es "no
/// enviada": no se emitió nada. Sin valores en <see cref="ToString"/>.
/// </summary>
public abstract class ResultadoProximoNumero
{
    private ResultadoProximoNumero()
    {
    }

    /// <summary>El nombre del caso, sin valores.</summary>
    public override string ToString() => GetType().Name;

    /// <summary>El número que corresponde al próximo comprobante del tipo y punto de venta.</summary>
    public sealed class Numero : ResultadoProximoNumero
    {
        /// <exception cref="ArgumentException">
        /// El número no está entre 1 y 99.999.999; el mensaje no incluye el valor recibido.
        /// </exception>
        public Numero(long valor)
        {
            if (valor is < NumeroComprobante.NumeroMinimo or > NumeroComprobante.NumeroMaximo)
            {
                throw new ArgumentException("El número de comprobante no es válido.", nameof(valor));
            }

            Valor = valor;
        }

        /// <summary>De 1 a 99.999.999.</summary>
        public long Valor { get; }
    }

    /// <summary>
    /// No se obtuvo el número (configuración incompleta, certificado, WSAA, último número, número máximo alcanzado o
    /// cancelación): no se envió nada y se puede volver a intentar.
    /// </summary>
    public sealed class NoEnviada : ResultadoProximoNumero
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
}
