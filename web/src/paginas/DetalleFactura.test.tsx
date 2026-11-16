import { act, cleanup, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi, type MockInstance } from 'vitest';
import { json, renderizarApp, servidorFalso, sinRed, vacia } from '../pruebas/servidorFalso';

/** Los métodos de `console` vigilados en cada prueba: la pantalla no escribe nada (datos personales). */
let consola: MockInstance[] = [];

beforeEach(() => {
  consola = (['log', 'info', 'warn', 'error', 'debug', 'trace'] as const).map((metodo) =>
    vi.spyOn(console, metodo).mockImplementation(() => {}),
  );
});

afterEach(() => {
  try {
    // Datos personales: nada de la factura queda en el almacenamiento del navegador ni en la consola.
    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
    for (const espia of consola) {
      expect(espia).not.toHaveBeenCalled();
    }
  } finally {
    cleanup();
    localStorage.clear();
    sessionStorage.clear();
  }
});

const TITULO = 'Factura';
const COMPROBANTE = 'Factura B 0003-00000125';
const RUTA_DETALLE = '/api/presupuestos/155/factura';
const RUTA_PDF = '/api/presupuestos/155/factura/pdf';
const MENSAJE_SIN_CONEXION = 'No se pudo conectar con el sistema. Reintentá.';
const NO_ENCONTRADA = 'No se encontró la factura.';
const SIN_DATOS_EMISOR = 'Completá los datos del emisor en la configuración para generar el PDF de la factura.';

/** La factura del presupuesto 155 con datos ficticios (nombres genéricos del PRD, DNI con 00). */
const DETALLE_155 = {
  comprobante: COMPROBANTE,
  fecha: '2026-10-30',
  presupuesto: 155,
  apellido: 'Pérez',
  nombre: 'Ana',
  dni: '00123456',
  receptor: 'DNI 00123456',
  lineas: [
    { descripcion: 'Armazón metálico', cantidad: 3, importe: 10.02 },
    { descripcion: 'Cristal orgánico', cantidad: 1, importe: 2700 },
  ],
  total: 2710.02,
  ivaContenido: 470.33,
  cae: '12345678901234',
  vencimientoCae: '2026-11-09',
  simulada: true,
};

const pantalla = () => screen.getByRole('heading', { name: TITULO }).closest('section')!;

function dato(etiqueta: string): string | null {
  const termino = within(pantalla()).getByText(etiqueta, { selector: 'dt' });
  return termino.nextElementSibling?.textContent ?? null;
}

async function abrirCon(rutas: Parameters<typeof servidorFalso>[0], url = '/facturas/155') {
  const registro = servidorFalso(rutas);
  renderizarApp(url);
  await screen.findByRole('heading', { name: TITULO });
  return registro;
}

describe('DetalleFactura', () => {
  it('Detalle_Presupuesto155_SinAccionesDeEdicion', async () => {
    // AC-24: se abre desde la lista de resultados.
    servidorFalso({
      'GET /api/sesion': vacia(204),
      'POST /api/facturacion/busqueda': json(200, {
        facturas: [
          {
            presupuesto: 155,
            comprobante: COMPROBANTE,
            fecha: '2026-10-30',
            apellido: 'Pérez',
            nombre: 'Ana',
            dni: '00123456',
            total: 2710.02,
          },
        ],
        pagina: 1,
        tamanoPagina: 50,
        total: 1,
      }),
      [`GET ${RUTA_DETALLE}`]: json(200, DETALLE_155),
    });
    renderizarApp('/facturas');
    fireEvent.click(await screen.findByRole('button', { name: 'Buscar' }));
    fireEvent.click(await screen.findByRole('link', { name: `Abrir la ${COMPROBANTE}` }));

    await screen.findByRole('heading', { name: TITULO });
    expect(screen.getByTestId('ubicacion').textContent).toBe('/facturas/155');
    await within(pantalla()).findByText('Comprobante', { selector: 'dt' });

    // Sus datos y el presupuesto 155, con un enlace a él.
    expect(dato('Comprobante')).toBe(COMPROBANTE);
    expect(dato('Fecha')).toBe('30/10/2026');
    expect(dato('Presupuesto')).toBe('155');
    const enlacePresupuesto = within(pantalla()).getByRole('link', { name: 'Presupuesto N.º 155' });
    expect(enlacePresupuesto.getAttribute('href')).toBe('/presupuestos/155');
    expect(dato('Apellido')).toBe('Pérez');
    expect(dato('Nombre')).toBe('Ana');
    expect(dato('DNI')).toBe('00123456');
    expect(dato('Receptor')).toBe('DNI 00123456');
    expect(dato('Total ($)')).toBe('2.710,02');
    expect(dato('IVA contenido ($)')).toBe('470,33');
    expect(dato('CAE')).toBe('12345678901234');
    expect(dato('Vencimiento del CAE')).toBe('09/11/2026');
    expect(within(pantalla()).getByText('COMPROBANTE SIMULADO — SIN VALIDEZ FISCAL')).toBeTruthy();
    const filas = within(within(pantalla()).getByRole('table', { name: 'Líneas de la factura' })).getAllByRole('row');
    expect(filas.slice(1).map((fila) => within(fila).getAllByRole('cell').map((celda) => celda.textContent))).toEqual([
      ['Armazón metálico', '3', '10,02'],
      ['Cristal orgánico', '1', '2.700,00'],
    ]);

    // Sin acciones de edición ni de eliminación (FR-24, RF-32): el único botón es Descargar PDF.
    const botones = within(pantalla()).getAllByRole('button').map((boton) => boton.textContent);
    expect(botones).toEqual(['Descargar PDF']);
    const acciones = within(pantalla())
      .queryAllByRole('link')
      .map((enlace) => enlace.textContent ?? '');
    expect(acciones.filter((texto) => /editar|modificar|eliminar|borrar|anular/i.test(texto))).toEqual([]);
  });

  it('Detalle: una Factura C sin IVA contenido y emitida en producción no lo muestran', async () => {
    await abrirCon({
      [`GET ${RUTA_DETALLE}`]: json(200, {
        ...DETALLE_155,
        comprobante: 'Factura C 0003-00000125',
        receptor: 'Consumidor Final',
        ivaContenido: null,
        simulada: false,
      }),
    });
    await within(pantalla()).findByText('Comprobante', { selector: 'dt' });

    expect(dato('Receptor')).toBe('Consumidor Final');
    expect(within(pantalla()).queryByText('IVA contenido ($)')).toBeNull();
    expect(within(pantalla()).queryByText('COMPROBANTE SIMULADO — SIN VALIDEZ FISCAL')).toBeNull();
  });

  describe('Descargar PDF', () => {
    const URL_DE_OBJETO = 'blob:prueba-factura-155';
    const BYTES_PDF = '%PDF-1.7\n';
    const pdf = () => () => new Response(BYTES_PDF, { status: 200, headers: { 'Content-Type': 'application/pdf' } });

    let eventos: string[] = [];
    let enlaces: { descarga: string; href: string | null }[] = [];
    let blobs: Blob[] = [];
    // jsdom no implementa `URL.createObjectURL` ni `URL.revokeObjectURL`, ni la descarga de un enlace: se simulan.
    const urlEstatica = URL as unknown as Record<string, unknown>;
    beforeEach(() => {
      eventos = [];
      enlaces = [];
      blobs = [];
      urlEstatica.createObjectURL = vi.fn((blob: Blob) => {
        blobs.push(blob);
        eventos.push('crear');
        return URL_DE_OBJETO;
      });
      urlEstatica.revokeObjectURL = vi.fn((url: string) => {
        eventos.push(`revocar ${url}`);
      });
      vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
        eventos.push('clic');
        enlaces.push({ descarga: this.download, href: this.getAttribute('href') });
      });
    });
    afterEach(() => {
      delete urlEstatica.createObjectURL;
      delete urlEstatica.revokeObjectURL;
    });

    const botonPdf = () => within(pantalla()).getByRole('button', { name: 'Descargar PDF' });

    it('Detalle_DescargarPdf', async () => {
      // AC-06: el PDF se descarga en la PC con una URL de objeto que se revoca.
      const registro = await abrirCon({ [`GET ${RUTA_DETALLE}`]: json(200, DETALLE_155), [`GET ${RUTA_PDF}`]: pdf() });
      await within(pantalla()).findByText('Comprobante', { selector: 'dt' });

      fireEvent.click(botonPdf());

      await waitFor(() => expect(eventos).toEqual(['crear', 'clic', `revocar ${URL_DE_OBJETO}`]));
      expect(registro.pedidosA('GET', RUTA_PDF)).toHaveLength(1);
      expect(await blobs[0]?.text()).toBe(BYTES_PDF);
      // El nombre lleva solo el comprobante, nunca datos del cliente.
      expect(enlaces).toEqual([{ descarga: 'factura-B-0003-00000125.pdf', href: URL_DE_OBJETO }]);
      expect(document.querySelector('a[download]')).toBeNull();
      expect(within(pantalla()).queryByRole('alert')).toBeNull();
    });

    it('Detalle_SinDatosEmisor_AvisoConEnlace', async () => {
      // AC-05: sin los datos del emisor no se genera el PDF y se indica completarlos en la configuración.
      await abrirCon({
        [`GET ${RUTA_DETALLE}`]: json(200, DETALLE_155),
        [`GET ${RUTA_PDF}`]: json(409, {
          status: 409,
          title: 'No se pudo generar el PDF de la factura.',
          detail: SIN_DATOS_EMISOR,
          motivo: 'sin_datos_emisor',
        }),
      });
      await within(pantalla()).findByText('Comprobante', { selector: 'dt' });

      fireEvent.click(botonPdf());

      const aviso = await within(pantalla()).findByRole('alert');
      expect(aviso.textContent).toContain(SIN_DATOS_EMISOR);
      const enlace = within(aviso).getByRole('link', { name: 'Ir a la configuración' });
      expect(enlace.getAttribute('href')).toBe('/configuracion');
      expect(URL.createObjectURL).not.toHaveBeenCalled();
      expect(eventos).toEqual([]);
    });

    it.each([
      ['un 404', vacia(404), NO_ENCONTRADA],
      ['un 500', vacia(500), MENSAJE_SIN_CONEXION],
    ])('Descargar PDF: %s muestra el mensaje general', async (_caso, respuesta, mensaje) => {
      await abrirCon({ [`GET ${RUTA_DETALLE}`]: json(200, DETALLE_155), [`GET ${RUTA_PDF}`]: respuesta });
      await within(pantalla()).findByText('Comprobante', { selector: 'dt' });

      fireEvent.click(botonPdf());

      expect((await within(pantalla()).findByRole('alert')).textContent).toBe(mensaje);
      expect(within(pantalla()).queryByRole('link', { name: 'Ir a la configuración' })).toBeNull();
      expect(eventos).toEqual([]);
    });

    it('Descargar PDF: sin sesión va al ingreso', async () => {
      await abrirCon({ [`GET ${RUTA_DETALLE}`]: json(200, DETALLE_155), [`GET ${RUTA_PDF}`]: vacia(401) });
      await within(pantalla()).findByText('Comprobante', { selector: 'dt' });

      fireEvent.click(botonPdf());

      await waitFor(() => expect(screen.getByTestId('ubicacion').textContent).toBe('/ingreso'));
      expect(eventos).toEqual([]);
    });

    it('Descargar PDF: mientras descarga no lanza otra', async () => {
      let responder: (respuesta: Response) => void = () => {};
      const registro = await abrirCon({
        [`GET ${RUTA_DETALLE}`]: json(200, DETALLE_155),
        [`GET ${RUTA_PDF}`]: () =>
          new Promise<Response>((resolver) => {
            responder = resolver;
          }),
      });
      await within(pantalla()).findByText('Comprobante', { selector: 'dt' });

      fireEvent.click(botonPdf());
      await waitFor(() => expect(botonPdf().getAttribute('aria-busy')).toBe('true'));
      fireEvent.click(botonPdf());
      responder(pdf()());

      await waitFor(() => expect(eventos).toEqual(['crear', 'clic', `revocar ${URL_DE_OBJETO}`]));
      expect(registro.pedidosA('GET', RUTA_PDF)).toHaveLength(1);
      await waitFor(() => expect(botonPdf().getAttribute('aria-busy')).toBeNull());
    });

    it('Descargar PDF: un PDF que llega después de cambiar de factura no se descarga', async () => {
      let responder: (respuesta: Response) => void = () => {};
      servidorFalso({
        [`GET ${RUTA_DETALLE}`]: json(200, DETALLE_155),
        [`GET ${RUTA_PDF}`]: () =>
          new Promise<Response>((resolver) => {
            responder = resolver;
          }),
        'GET /api/presupuestos/156/factura': json(200, {
          ...DETALLE_155,
          presupuesto: 156,
          comprobante: 'Factura B 0003-00000126',
        }),
      });
      const { volver } = renderizarApp(['/facturas/156', '/facturas/155']);
      await screen.findByRole('heading', { name: TITULO });
      await within(pantalla()).findByText('Comprobante', { selector: 'dt' });
      fireEvent.click(botonPdf());

      await volver();
      await waitFor(() => expect(dato('Comprobante')).toBe('Factura B 0003-00000126'));
      responder(pdf()());

      await waitFor(() => expect(botonPdf().getAttribute('aria-busy')).toBeNull());
      expect(eventos).toEqual([]);
      expect(within(pantalla()).queryByRole('alert')).toBeNull();
    });

    it('Descargar PDF: un PDF que llega después de salir del detalle no se descarga', async () => {
      let responder: (respuesta: Response) => void = () => {};
      await abrirCon({
        'GET /api/sesion': vacia(204),
        [`GET ${RUTA_DETALLE}`]: json(200, DETALLE_155),
        [`GET ${RUTA_PDF}`]: () =>
          new Promise<Response>((resolver) => {
            responder = resolver;
          }),
      });
      await within(pantalla()).findByText('Comprobante', { selector: 'dt' });
      fireEvent.click(botonPdf());

      fireEvent.click(within(pantalla()).getByRole('link', { name: 'Buscar facturas' }));
      await screen.findByRole('heading', { name: 'Buscar facturas' });
      await act(async () => {
        responder(pdf()() as Response);
        await new Promise((resolver) => setTimeout(resolver, 20));
      });

      // La respuesta ya no le corresponde a ninguna pantalla: ni se crea la URL de objeto ni se descarga.
      expect(URL.createObjectURL).not.toHaveBeenCalled();
      expect(eventos).toEqual([]);
      expect(screen.getByTestId('ubicacion').textContent).toBe('/facturas');
    });

    it('Descargar PDF: al cambiar de 155 a 156 con una descarga en curso, la de 156 no queda bloqueada', async () => {
      let responder155: (respuesta: Response) => void = () => {};
      const registro = servidorFalso({
        [`GET ${RUTA_DETALLE}`]: json(200, DETALLE_155),
        [`GET ${RUTA_PDF}`]: () =>
          new Promise<Response>((resolver) => {
            responder155 = resolver;
          }),
        'GET /api/presupuestos/156/factura': json(200, {
          ...DETALLE_155,
          presupuesto: 156,
          comprobante: 'Factura B 0003-00000126',
        }),
        'GET /api/presupuestos/156/factura/pdf': pdf(),
      });
      const { volver } = renderizarApp(['/facturas/156', '/facturas/155']);
      await screen.findByRole('heading', { name: TITULO });
      await within(pantalla()).findByText('Comprobante', { selector: 'dt' });
      fireEvent.click(botonPdf());
      await waitFor(() => expect(botonPdf().getAttribute('aria-busy')).toBe('true'));

      await volver();
      await waitFor(() => expect(dato('Comprobante')).toBe('Factura B 0003-00000126'));
      // El botón de la 156 no hereda la descarga de la 155.
      expect(botonPdf().getAttribute('aria-busy')).toBeNull();
      fireEvent.click(botonPdf());

      await waitFor(() => expect(eventos).toEqual(['crear', 'clic', `revocar ${URL_DE_OBJETO}`]));
      expect(registro.pedidosA('GET', '/api/presupuestos/156/factura/pdf')).toHaveLength(1);
      expect(enlaces).toEqual([{ descarga: 'factura-B-0003-00000126.pdf', href: URL_DE_OBJETO }]);

      // La de la 155 llega tarde: no se descarga ni se muestra.
      await act(async () => {
        responder155(pdf()() as Response);
        await new Promise((resolver) => setTimeout(resolver, 20));
      });
      expect(eventos).toHaveLength(3);
      expect(within(pantalla()).queryByRole('alert')).toBeNull();
      expect(botonPdf().getAttribute('aria-busy')).toBeNull();
    });
  });

  it.each([
    ['respuesta ilegible', json(200, { ...DETALLE_155, cae: 'no es un CAE' })],
    ['500', vacia(500)],
    ['falla de red', sinRed],
  ])('Detalle_RespuestaIlegible_MensajeGeneral (%s)', async (_caso, respuesta) => {
    await abrirCon({ [`GET ${RUTA_DETALLE}`]: respuesta });

    expect((await within(pantalla()).findByRole('alert')).textContent).toBe(MENSAJE_SIN_CONEXION);
    // Sin datos: ni del cliente ni de la factura, y sin botón de descarga.
    expect(within(pantalla()).queryByText('Pérez')).toBeNull();
    expect(within(pantalla()).queryByText(COMPROBANTE)).toBeNull();
    expect(within(pantalla()).queryByRole('button', { name: 'Descargar PDF' })).toBeNull();
  });

  it('Detalle: un 404 dice que no se encontró la factura', async () => {
    await abrirCon({ [`GET ${RUTA_DETALLE}`]: vacia(404) });

    expect((await within(pantalla()).findByRole('alert')).textContent).toBe(NO_ENCONTRADA);
  });

  it('Detalle: un número inválido en la ruta no se consulta', async () => {
    const registro = await abrirCon({}, '/facturas/0155');

    expect((await within(pantalla()).findByRole('alert')).textContent).toBe(NO_ENCONTRADA);
    expect(registro.llamadas.filter((llamada) => llamada.ruta.startsWith('/api/presupuestos'))).toEqual([]);
  });

  it('Detalle: sin sesión va al ingreso', async () => {
    servidorFalso({ [`GET ${RUTA_DETALLE}`]: vacia(401) });
    renderizarApp('/facturas/155');

    await waitFor(() => expect(screen.getByTestId('ubicacion').textContent).toBe('/ingreso'));
  });
});
