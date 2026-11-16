/**
 * Descarga un PDF recibido con el nombre indicado (D-10 de FEAT-006): una URL de objeto, un enlace temporal con
 * `download`, su clic y la revocación de la URL, también si el clic falla. El nombre lleva solo el número del
 * presupuesto (`presupuesto-155.pdf`) o el comprobante de la factura (`factura-B-0003-00000125.pdf`, FEAT-009), nunca
 * datos del cliente (R-02). Lo usan la pantalla del presupuesto y la del detalle de la factura.
 */
export function guardarPdf(contenido: Blob, nombre: string) {
  const url = URL.createObjectURL(contenido);
  const enlace = document.createElement('a');
  enlace.href = url;
  enlace.download = nombre;
  enlace.hidden = true;
  document.body.append(enlace);
  try {
    enlace.click();
  } finally {
    enlace.remove();
    URL.revokeObjectURL(url);
  }
}
