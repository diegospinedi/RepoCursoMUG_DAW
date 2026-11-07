import { cleanup, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { mensajeJuntoAlCampo } from '../pruebas/formulario';
import { json, renderizarApp, servidorFalso, sinRed, vacia } from '../pruebas/servidorFalso';

afterEach(() => {
  try {
    // D-17: los datos del cliente viven solo en el estado de React.
    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
  } finally {
    cleanup();
    localStorage.clear();
    sessionStorage.clear();
  }
});

const RUTA = '/presupuestos/nuevo';
const TITULO = 'Nuevo presupuesto';
const ETIQUETA_BUSQUEDA = 'Texto a buscar';
const MENSAJE_SIN_CONEXION = 'No se pudo conectar con el sistema. Reintentá.';
const MENSAJE_CUERPO_GRANDE = 'Los datos enviados son demasiado largos. Revisalos y grabá de nuevo.';
const MENSAJE_PRECIO_NEGATIVO = 'El precio unitario no puede ser negativo';
const MENSAJE_SIN_LINEAS = 'Agregá al menos una línea al presupuesto';
const ENCABEZADOS_LINEAS = [
  'Código de Artículo',
  'Descripción',
  'Precio Unitario ($)',
  'Cantidad',
  'Porcentaje de Descuento (%)',
  'Precio con Descuento ($)',
  'Precio Final ($)',
  'Acción',
];

/** Los seis datos del cliente, ficticios (D-17). */
const CLIENTE = {
  apellido: 'Pérez',
  nombre: 'Ana',
  dni: '23.456.789',
  domicilio: 'Calle Falsa 123',
  email: 'ana@mail.com',
  telefono: '11 5555-0000',
};
type CampoCliente = keyof typeof CLIENTE;
const ETIQUETAS: Record<CampoCliente, string> = {
  apellido: 'Apellido',
  nombre: 'Nombre',
  dni: 'DNI',
  domicilio: 'Domicilio',
  email: 'Email',
  telefono: 'Nro. de Teléfono',
};

const ARMAZON = { codigo: 7, codigoProveedor: 'ARM-7', descripcion: 'Armazón metálico', precioVenta: 1850, activo: true };
const ESTUCHE = { codigo: 9, codigoProveedor: 'EST-9', descripcion: 'Estuche rígido', precioVenta: -150, activo: true };
const pagina = (articulos: unknown[], numero = 1, total = articulos.length) => ({
  articulos,
  pagina: numero,
  tamanoPagina: 50,
  total,
});
const BUSCAR_ARMAZON = 'GET /api/articulos?texto=armazon&pagina=1&soloActivos=true';
const BUSCAR_TODO = 'GET /api/articulos?pagina=1&soloActivos=true';

const LINEA_GRABADA = {
  codigoArticulo: 7,
  descripcion: 'Armazón metálico',
  precioUnitario: 1850,
  cantidad: 1,
  porcentajeDescuento: 0,
  precioConDescuento: 1850,
  precioFinal: 1850,
};
const GRABADO = {
  numero: 155,
  fecha: '2026-03-10',
  estado: 'borrador',
  cliente: { ...CLIENTE, dni: '23456789' },
  lineas: [LINEA_GRABADA],
};

const conSesion = { 'GET /api/sesion': vacia(204) };

/** Un servidor con sesión, la búsqueda de "armazon" y el alta (por defecto, 201 con el presupuesto 155). */
function servidor(rutas: Parameters<typeof servidorFalso>[0] = {}) {
  return servidorFalso({
    ...conSesion,
    [BUSCAR_ARMAZON]: json(200, pagina([ARMAZON, ESTUCHE])),
    'POST /api/presupuestos': json(201, GRABADO),
    'GET /api/presupuestos/155': json(200, GRABADO),
    ...rutas,
  });
}

async function abrir() {
  renderizarApp(RUTA);
  await screen.findByRole('heading', { name: TITULO });
  await screen.findByRole('button', { name: 'Grabar' });
}

const campo = (etiqueta: string) => screen.getByLabelText(etiqueta) as HTMLInputElement;

function completar(valores: Partial<Record<CampoCliente, string>> = {}) {
  for (const [nombre, etiqueta] of Object.entries(ETIQUETAS) as [CampoCliente, string][]) {
    fireEvent.change(campo(etiqueta), { target: { value: valores[nombre] ?? CLIENTE[nombre] } });
  }
}

/** Busca un texto y espera los resultados. */
async function buscar(texto = 'armazon') {
  fireEvent.change(campo(ETIQUETA_BUSQUEDA), { target: { value: texto } });
  fireEvent.click(screen.getByRole('button', { name: 'Buscar' }));
  await screen.findByRole('table', { name: 'Artículos activos' });
}

const agregar = (articulo: { codigo: number; descripcion: string }) =>
  fireEvent.click(screen.getByRole('button', { name: `Agregar ${articulo.codigo} ${articulo.descripcion}` }));

const grabar = () => fireEvent.click(screen.getByRole('button', { name: 'Grabar' }));

const tablaLineas = () => screen.getByRole('table', { name: 'Líneas del presupuesto' });

/** El texto de cada celda de cada línea cargada. */
function lineas(): string[][] {
  const [, ...datos] = within(tablaLineas()).getAllByRole('row');
  return datos.map((fila) => within(fila).getAllByRole('cell').map((celda) => celda.textContent ?? ''));
}

/** El cuerpo del único POST enviado. */
function cuerpoEnviado(registro: ReturnType<typeof servidorFalso>) {
  const pedidos = registro.pedidosA('POST', '/api/presupuestos');
  expect(pedidos).toHaveLength(1);
  return JSON.parse(String(pedidos[0]?.init?.body)) as unknown;
}

/** Graba con valores inválidos y comprueba el mensaje junto al campo, sin enviar. */
async function rechazadoSinEnviar(valores: Partial<Record<CampoCliente, string>>, etiqueta: string, mensaje: string) {
  const registro = servidor();
  await abrir();
  completar(valores);
  await buscar();
  agregar(ARMAZON);
  grabar();

  await waitFor(() => expect(mensajeJuntoAlCampo(campo(etiqueta))).toBe(mensaje));
  expect(campo(etiqueta).getAttribute('aria-invalid')).toBe('true');
  expect(registro.pedidosA('POST', '/api/presupuestos')).toHaveLength(0);
  expect(screen.getByTestId('ubicacion').textContent).toBe(RUTA);
}

describe('NuevoPresupuesto', () => {
  it('NuevoPresupuesto_AlAbrir_CamposVaciosYSinLineas', async () => {
    const registro = servidor();
    await abrir();

    for (const etiqueta of Object.values(ETIQUETAS)) {
      expect(campo(etiqueta).value).toBe('');
      expect(campo(etiqueta).getAttribute('aria-invalid')).toBe('false');
      // D-17: el navegador no guarda en su historial de formularios lo escrito.
      expect(campo(etiqueta).getAttribute('autocomplete')).toBe('off');
    }
    expect(campo('Apellido').getAttribute('maxlength')).toBe('100');
    expect(campo('Nombre').getAttribute('maxlength')).toBe('100');
    // Alcanza para "23.456.789", con los puntos.
    expect(campo('DNI').getAttribute('maxlength')).toBe('10');
    expect(campo('Domicilio').getAttribute('maxlength')).toBe('200');
    expect(campo('Email').getAttribute('maxlength')).toBe('254');
    expect(campo('Nro. de Teléfono').getAttribute('maxlength')).toBe('30');
    expect(within(tablaLineas()).getAllByRole('columnheader').map((c) => c.textContent)).toEqual(ENCABEZADOS_LINEAS);
    expect(lineas()).toEqual([]);
    // No busca hasta que la operadora lo pide.
    expect(registro.llamadas.filter((l) => l.ruta.startsWith('/api/articulos'))).toEqual([]);
    expect(registro.pedidosA('GET', '/api/sesion')).toHaveLength(1);
    expect(screen.getByRole('link', { name: 'Volver al inicio' }).getAttribute('href')).toBe('/');
  });

  it('NuevoPresupuesto_ErroresPorCampo400_JuntoAlCampo', async () => {
    // AC-02 y AC-03 desde el servidor: cada mensaje de `errors` junto a su campo, `lineas[i]` junto a la línea i y
    // `lineas` junto a la tabla.
    const registro = servidor({
      'POST /api/presupuestos': json(400, {
        errors: {
          apellido: ['Ingresá el apellido del cliente'],
          nombre: ['Ingresá el nombre del cliente'],
          dni: ['Ingresá el DNI del cliente'],
          domicilio: ['Usá hasta 200 caracteres.'],
          email: ['El email debe tener el formato usuario@dominio.extensión'],
          telefono: ['Usá hasta 30 caracteres.'],
          'lineas[1]': [MENSAJE_PRECIO_NEGATIVO],
          lineas: ['Usá hasta 100 líneas.'],
          otro: ['Revisá el pedido.'],
        },
      }),
    });
    await abrir();
    completar();
    await buscar();
    agregar(ARMAZON);
    agregar(ARMAZON);
    grabar();

    await waitFor(() => expect(mensajeJuntoAlCampo(campo('DNI'))).toBe('Ingresá el DNI del cliente'));
    expect(mensajeJuntoAlCampo(campo('Apellido'))).toBe('Ingresá el apellido del cliente');
    expect(mensajeJuntoAlCampo(campo('Nombre'))).toBe('Ingresá el nombre del cliente');
    expect(mensajeJuntoAlCampo(campo('Domicilio'))).toBe('Usá hasta 200 caracteres.');
    expect(mensajeJuntoAlCampo(campo('Email'))).toBe('El email debe tener el formato usuario@dominio.extensión');
    expect(mensajeJuntoAlCampo(campo('Nro. de Teléfono'))).toBe('Usá hasta 30 caracteres.');
    expect(mensajeJuntoAlCampo(screen.getByRole('button', { name: 'Quitar línea 2' }))).toBe(MENSAJE_PRECIO_NEGATIVO);
    expect(mensajeJuntoAlCampo(screen.getByRole('button', { name: 'Quitar línea 1' }))).toBeNull();
    expect(mensajeJuntoAlCampo(tablaLineas())).toBe('Usá hasta 100 líneas.');
    expect(screen.getByText('Revisá el pedido.')).toBeTruthy();
    expect(cuerpoEnviado(registro)).toBeTruthy();
    expect(screen.getByTestId('ubicacion').textContent).toBe(RUTA);

    // Al quitar la línea rechazada, su mensaje se va con ella y el de la otra línea no aparece.
    fireEvent.click(screen.getByRole('button', { name: 'Quitar línea 2' }));
    expect(screen.queryByText(MENSAJE_PRECIO_NEGATIVO)).toBeNull();
    expect(lineas()).toHaveLength(1);
  });

  it('NuevoPresupuesto_ErrorDeCampo401ConErrores_JuntoAlCampo', async () => {
    servidor({ 'POST /api/presupuestos': json(401, { errors: { dni: ['Ingresá el DNI del cliente'] } }) });
    await abrir();
    completar();
    await buscar();
    agregar(ARMAZON);
    grabar();

    await waitFor(() => expect(mensajeJuntoAlCampo(campo('DNI'))).toBe('Ingresá el DNI del cliente'));
  });

  it.each([
    ['apellido', 'Apellido', 'Ingresá el apellido del cliente'],
    ['nombre', 'Nombre', 'Ingresá el nombre del cliente'],
    ['dni', 'DNI', 'Ingresá el DNI del cliente'],
  ] as const)('NuevoPresupuesto_Sin%s_ErrorAntesDeEnviar', async (nombre, etiqueta, mensaje) => {
    // AC-02 y AC-03 en el navegador.
    await rechazadoSinEnviar({ [nombre]: '  ' }, etiqueta, mensaje);
  });

  it.each(['123456', '123456789', '23A56789'])('NuevoPresupuesto_Dni123456_ErrorAntesDeEnviar (%s)', async (dni) => {
    // AC-06.
    await rechazadoSinEnviar({ dni }, 'DNI', 'El DNI debe tener 7 u 8 dígitos');
  });

  it.each(['ana@', 'ana@mail'])('NuevoPresupuesto_EmailAnaArroba_ErrorAntesDeEnviar (%s)', async (email) => {
    // AC-07.
    await rechazadoSinEnviar({ email }, 'Email', 'El email debe tener el formato usuario@dominio.extensión');
  });

  it('NuevoPresupuesto_SinLineas_ErrorSinEnviar', async () => {
    // AC-11.
    const registro = servidor();
    await abrir();
    completar();
    grabar();

    await waitFor(() => expect(mensajeJuntoAlCampo(tablaLineas())).toBe(MENSAJE_SIN_LINEAS));
    expect(registro.pedidosA('POST', '/api/presupuestos')).toHaveLength(0);

    // Al agregar una línea el aviso se quita.
    await buscar();
    agregar(ARMAZON);
    expect(screen.queryByText(MENSAJE_SIN_LINEAS)).toBeNull();
  });

  it('NuevoPresupuesto_ClienteInvalidoYSinLineas_TodosLosErroresJuntos', async () => {
    const registro = servidor();
    await abrir();
    completar({ apellido: '', email: 'ana@' });
    grabar();

    await waitFor(() => expect(mensajeJuntoAlCampo(tablaLineas())).toBe(MENSAJE_SIN_LINEAS));
    expect(mensajeJuntoAlCampo(campo('Apellido'))).toBe('Ingresá el apellido del cliente');
    expect(mensajeJuntoAlCampo(campo('Email'))).toBe('El email debe tener el formato usuario@dominio.extensión');
    expect(registro.pedidosA('POST', '/api/presupuestos')).toHaveLength(0);
  });

  it('NuevoPresupuesto_MasDe100Lineas_UsaHasta100SinEnviar', async () => {
    // D-31: se rechaza al grabar, junto a la tabla, sin enviar.
    const registro = servidor();
    await abrir();
    completar();
    await buscar();
    for (let i = 0; i < 101; i++) {
      agregar(ARMAZON);
    }
    grabar();

    await waitFor(() => expect(mensajeJuntoAlCampo(tablaLineas())).toBe('Usá hasta 100 líneas.'));
    expect(registro.pedidosA('POST', '/api/presupuestos')).toHaveLength(0);
    // 101 clics que vuelven a dibujar la tabla: con la medición de cobertura de jsdom tarda más que el límite común.
  }, 20_000);

  it('NuevoPresupuesto_ArticuloMenos150_NoSeAgrega', async () => {
    // AC-16: no se agrega y el mensaje queda junto al buscador.
    servidor();
    await abrir();
    await buscar();
    agregar(ESTUCHE);

    expect(mensajeJuntoAlCampo(campo(ETIQUETA_BUSQUEDA))).toBe(MENSAJE_PRECIO_NEGATIVO);
    expect(campo(ETIQUETA_BUSQUEDA).getAttribute('aria-invalid')).toBe('true');
    expect(lineas()).toEqual([]);

    // Un artículo válido sí se agrega y el aviso se quita.
    agregar(ARMAZON);
    expect(mensajeJuntoAlCampo(campo(ETIQUETA_BUSQUEDA))).toBeNull();
    expect(lineas()).toHaveLength(1);
  });

  it('NuevoPresupuesto_RespuestaIlegible_MensajeGeneral', async () => {
    servidor({ 'POST /api/presupuestos': json(201, { ...GRABADO, estado: 'otro' }) });
    await abrir();
    completar();
    await buscar();
    agregar(ARMAZON);
    grabar();

    expect(await screen.findByText(MENSAJE_SIN_CONEXION)).toBeTruthy();
    expect(screen.getByTestId('ubicacion').textContent).toBe(RUTA);
    // Lo cargado sigue en pantalla para reintentar.
    expect(campo('Apellido').value).toBe('Pérez');
    expect(lineas()).toHaveLength(1);
  });

  it('NuevoPresupuesto_SinRed_MensajeGeneral', async () => {
    servidor({ 'POST /api/presupuestos': sinRed });
    await abrir();
    completar();
    await buscar();
    agregar(ARMAZON);
    grabar();

    expect(await screen.findByText(MENSAJE_SIN_CONEXION)).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Grabar' }).hasAttribute('disabled')).toBe(false);
  });

  it('NuevoPresupuesto_413_Mensaje', async () => {
    servidor({ 'POST /api/presupuestos': vacia(413) });
    await abrir();
    completar();
    await buscar();
    agregar(ARMAZON);
    grabar();

    expect(await screen.findByText(MENSAJE_CUERPO_GRANDE)).toBeTruthy();
    expect(screen.getByTestId('ubicacion').textContent).toBe(RUTA);
  });

  it('NuevoPresupuesto_SinSesion401_VuelveAlIngreso', async () => {
    servidor({ 'POST /api/presupuestos': vacia(401) });
    await abrir();
    completar();
    await buscar();
    agregar(ARMAZON);
    grabar();

    expect(await screen.findByRole('heading', { name: 'Ingresá al sistema' })).toBeTruthy();
    expect(screen.getByTestId('ubicacion').textContent).toBe('/ingreso');
    expect(screen.queryByRole('heading', { name: TITULO })).toBeNull();
  });

  it('NuevoPresupuesto_SinSesionAlAbrir_VuelveAlIngreso', async () => {
    servidorFalso({ 'GET /api/sesion': vacia(401) });
    renderizarApp(RUTA);

    expect(await screen.findByRole('heading', { name: 'Ingresá al sistema' })).toBeTruthy();
    expect(screen.getByTestId('ubicacion').textContent).toBe('/ingreso');
  });

  it('NuevoPresupuesto_SinSesionAlBuscar_VuelveAlIngreso', async () => {
    servidor({ [BUSCAR_ARMAZON]: vacia(401) });
    await abrir();
    fireEvent.change(campo(ETIQUETA_BUSQUEDA), { target: { value: 'armazon' } });
    fireEvent.click(screen.getByRole('button', { name: 'Buscar' }));

    expect(await screen.findByRole('heading', { name: 'Ingresá al sistema' })).toBeTruthy();
  });

  it('NuevoPresupuesto_SesionConError_MuestraElMensajeYElFormulario', async () => {
    servidorFalso({ 'GET /api/sesion': vacia(500) });
    await abrir();

    expect(screen.getByText(MENSAJE_SIN_CONEXION)).toBeTruthy();
  });

  it('NuevoPresupuesto_Grabar_NavegaYConfirmaBorrador', async () => {
    // AC-01 y AC-12.
    const registro = servidor();
    await abrir();
    completar();
    await buscar();
    agregar(ARMAZON);
    grabar();

    expect(await screen.findByText('Presupuesto N.º 155 grabado. Estado: Borrador.')).toBeTruthy();
    expect(screen.getByTestId('ubicacion').textContent).toBe('/presupuestos/155');
    expect(cuerpoEnviado(registro)).toEqual({
      cliente: CLIENTE,
      lineas: [{ codigoArticulo: 7 }],
    });
    // El presupuesto grabado muestra los seis datos (AC-01).
    for (const valor of [...Object.values(CLIENTE).filter((v) => v !== CLIENTE.dni), '23456789']) {
      expect(screen.getByText(valor)).toBeTruthy();
    }
  });

  it('NuevoPresupuesto_SinOpcionales_Graba', async () => {
    // AC-04: sin domicilio, email ni teléfono no reclama nada y los envía como null.
    const registro = servidor();
    await abrir();
    completar({ domicilio: '', email: '  ', telefono: '' });
    await buscar();
    agregar(ARMAZON);
    grabar();

    await screen.findByText('Presupuesto N.º 155 grabado. Estado: Borrador.');
    expect(cuerpoEnviado(registro)).toEqual({
      cliente: { ...CLIENTE, domicilio: null, email: null, telefono: null },
      lineas: [{ codigoArticulo: 7 }],
    });
  });

  it('NuevoPresupuesto_EmailAnaMailCom_Graba', async () => {
    // AC-08.
    const registro = servidor();
    await abrir();
    completar({ email: ' ana@mail.com ' });
    await buscar();
    agregar(ARMAZON);
    grabar();

    await screen.findByText('Presupuesto N.º 155 grabado. Estado: Borrador.');
    expect(cuerpoEnviado(registro)).toMatchObject({ cliente: { email: 'ana@mail.com' } });
  });

  it('NuevoPresupuesto_Buscar_PideSoloActivos', async () => {
    // AC-13 y AC-14: la búsqueda de las líneas pide solo activos, por descripción o por código.
    const registro = servidor({
      'GET /api/articulos?texto=7&pagina=1&soloActivos=true': json(200, pagina([ARMAZON])),
    });
    await abrir();
    await buscar(' armazon ');

    const resultados = screen.getByRole('table', { name: 'Artículos activos' });
    expect(within(resultados).getAllByRole('columnheader').map((c) => c.textContent)).toEqual([
      'Código',
      'Descripción',
      'Precio de venta ($)',
      'Acción',
    ]);
    const [, ...filas] = within(resultados).getAllByRole('row');
    expect(filas.map((f) => within(f).getAllByRole('cell').map((c) => c.textContent))).toEqual([
      ['7', 'Armazón metálico', '1.850,00', 'Agregar'],
      ['9', 'Estuche rígido', '-150,00', 'Agregar'],
    ]);

    fireEvent.change(campo(ETIQUETA_BUSQUEDA), { target: { value: '7' } });
    fireEvent.click(screen.getByRole('button', { name: 'Buscar' }));
    await waitFor(() => expect(screen.queryByText('Estuche rígido')).toBeNull());
    expect(screen.getByText('Armazón metálico')).toBeTruthy();

    expect(registro.llamadas.filter((l) => l.ruta.startsWith('/api/articulos')).map((l) => l.ruta)).toEqual([
      '/api/articulos?texto=armazon&pagina=1&soloActivos=true',
      '/api/articulos?texto=7&pagina=1&soloActivos=true',
    ]);
  });

  it('NuevoPresupuesto_BuscarVacio_ListaTodosLosActivos', async () => {
    const registro = servidor({ [BUSCAR_TODO]: json(200, pagina([ARMAZON])) });
    await abrir();
    await buscar('');

    expect(registro.pedidosA('GET', '/api/articulos?pagina=1&soloActivos=true')).toHaveLength(1);
  });

  it('NuevoPresupuesto_BuscarSinCoincidencias_LoInforma', async () => {
    servidor({ [BUSCAR_ARMAZON]: json(200, pagina([])) });
    await abrir();
    fireEvent.change(campo(ETIQUETA_BUSQUEDA), { target: { value: 'armazon' } });
    fireEvent.click(screen.getByRole('button', { name: 'Buscar' }));

    expect(await screen.findByText('No hay artículos activos que coincidan con la búsqueda.')).toBeTruthy();
    expect(screen.queryByRole('table', { name: 'Artículos activos' })).toBeNull();
  });

  it('NuevoPresupuesto_BusquedaLarga_ErrorSinPedir', async () => {
    const registro = servidor();
    await abrir();
    fireEvent.change(campo(ETIQUETA_BUSQUEDA), { target: { value: 'a'.repeat(201) } });
    fireEvent.click(screen.getByRole('button', { name: 'Buscar' }));

    expect(mensajeJuntoAlCampo(campo(ETIQUETA_BUSQUEDA))).toBe('Usá hasta 200 caracteres.');
    expect(registro.llamadas.filter((l) => l.ruta.startsWith('/api/articulos'))).toEqual([]);
  });

  it('NuevoPresupuesto_Busqueda400_ErrorJuntoAlBuscadorOMensajeGeneral', async () => {
    servidor({
      [BUSCAR_ARMAZON]: json(400, { errors: { texto: ['Usá hasta 200 caracteres.'] } }),
      'GET /api/articulos?texto=otro&pagina=1&soloActivos=true': json(400, {
        errors: { soloActivos: ['Indicá true o false.'] },
      }),
    });
    await abrir();
    fireEvent.change(campo(ETIQUETA_BUSQUEDA), { target: { value: 'armazon' } });
    fireEvent.click(screen.getByRole('button', { name: 'Buscar' }));
    await waitFor(() => expect(mensajeJuntoAlCampo(campo(ETIQUETA_BUSQUEDA))).toBe('Usá hasta 200 caracteres.'));

    fireEvent.change(campo(ETIQUETA_BUSQUEDA), { target: { value: 'otro' } });
    fireEvent.click(screen.getByRole('button', { name: 'Buscar' }));
    expect(await screen.findByText('Indicá true o false.')).toBeTruthy();
    expect(mensajeJuntoAlCampo(campo(ETIQUETA_BUSQUEDA))).toBeNull();
  });

  it('NuevoPresupuesto_BusquedaConError_MensajeGeneralSinResultados', async () => {
    servidor({ [BUSCAR_ARMAZON]: vacia(500) });
    await abrir();
    fireEvent.change(campo(ETIQUETA_BUSQUEDA), { target: { value: 'armazon' } });
    fireEvent.click(screen.getByRole('button', { name: 'Buscar' }));

    expect(await screen.findByText(MENSAJE_SIN_CONEXION)).toBeTruthy();
    expect(screen.queryByRole('table', { name: 'Artículos activos' })).toBeNull();
  });

  it('NuevoPresupuesto_Busqueda_PaginaSiguienteYAnterior', async () => {
    const registro = servidor({
      [BUSCAR_ARMAZON]: json(200, pagina([ARMAZON], 1, 51)),
      'GET /api/articulos?texto=armazon&pagina=2&soloActivos=true': json(200, pagina([ESTUCHE], 2, 51)),
    });
    await abrir();
    await buscar();
    expect(screen.getByText('Página 1 de 2')).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Anterior' }).hasAttribute('disabled')).toBe(true);

    fireEvent.click(screen.getByRole('button', { name: 'Siguiente' }));
    expect(await screen.findByText('Página 2 de 2')).toBeTruthy();
    expect(screen.getByText('Estuche rígido')).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Siguiente' }).hasAttribute('disabled')).toBe(true);

    fireEvent.click(screen.getByRole('button', { name: 'Anterior' }));
    expect(await screen.findByText('Página 1 de 2')).toBeTruthy();
    expect(registro.pedidosA('GET', '/api/articulos?texto=armazon&pagina=2&soloActivos=true')).toHaveLength(1);
    expect(registro.pedidosA('GET', BUSCAR_ARMAZON.slice(4))).toHaveLength(2);
  });

  it('NuevoPresupuesto_Agregar1850_PrecioUnitarioSoloLectura', async () => {
    // AC-15 y FR-14: Código, Descripción y Precio Unitario del artículo; Cantidad 1, Descuento 0 y los dos precios
    // iguales al unitario. El Precio Unitario es de solo lectura.
    servidor();
    await abrir();
    await buscar();
    agregar(ARMAZON);

    expect(lineas()).toEqual([['7', 'Armazón metálico', '', '1', '0', '1.850,00', '1.850,00', 'Quitar']]);
    const precio = screen.getByLabelText('Precio unitario de la línea 1') as HTMLInputElement;
    expect(precio.value).toBe('1.850,00');
    expect(precio.readOnly).toBe(true);
  });

  it('NuevoPresupuesto_MismoArticuloDosVecesYQuitar', async () => {
    // D-16: el mismo artículo puede ir en más de una línea; D-21: se puede quitar una línea antes de grabar.
    const registro = servidor();
    await abrir();
    completar();
    await buscar();
    agregar(ARMAZON);
    agregar(ARMAZON);
    expect(lineas()).toHaveLength(2);

    fireEvent.click(screen.getByRole('button', { name: 'Quitar línea 1' }));
    expect(lineas()).toHaveLength(1);
    expect(screen.queryByRole('button', { name: 'Quitar línea 2' })).toBeNull();

    grabar();
    await screen.findByText('Presupuesto N.º 155 grabado. Estado: Borrador.');
    expect(cuerpoEnviado(registro)).toMatchObject({ lineas: [{ codigoArticulo: 7 }] });
  });

  it('NuevoPresupuesto_NoGuardaDatosEnElAlmacenamiento', async () => {
    // D-17 (Ley 25.326): los datos del cliente no quedan en el almacenamiento, la URL, el estado de la navegación ni
    // la consola.
    const consola = (['log', 'info', 'warn', 'error', 'debug', 'trace'] as const).map((metodo) =>
      vi.spyOn(console, metodo).mockImplementation(() => {}),
    );
    servidor();
    await abrir();
    completar();
    await buscar();
    agregar(ARMAZON);
    grabar();
    await screen.findByText('Presupuesto N.º 155 grabado. Estado: Borrador.');

    const ubicacion = screen.getByTestId('ubicacion');
    const lugares = [
      ubicacion.textContent ?? '',
      ubicacion.getAttribute('data-estado') ?? '',
      decodeURIComponent(window.location.href),
      ...consola.flatMap((espia) => espia.mock.calls.map((argumentos) => JSON.stringify(argumentos))),
    ];
    for (const dato of [...Object.values(CLIENTE), '23456789']) {
      expect(lugares.filter((lugar) => lugar.includes(dato)), dato).toEqual([]);
    }
    expect(JSON.parse(ubicacion.getAttribute('data-estado') ?? 'null')).toEqual({
      mensaje: 'Presupuesto N.º 155 grabado. Estado: Borrador.',
    });
    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
  });
});
