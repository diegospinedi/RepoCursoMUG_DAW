import { cleanup, fireEvent, screen } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import fuenteApp from './App.tsx?raw';
import { MENSAJE_BLOQUEADO } from './api/cliente';
import { MENSAJE_CAMBIADA } from './paginas/CambiarContrasena';
import { json, problemaGeneral, renderizarApp, servidorFalso, vacia } from './pruebas/servidorFalso';

afterEach(cleanup);

describe('App', () => {
  it.each([
    ['definir', false, 'Definí la contraseña de acceso'],
    ['ingreso', true, 'Ingresá al sistema'],
    ['inicio', true, 'Sesión iniciada'],
  ] as const)('App_PantallaDe%s_MuestraElLogo', async (_pantalla, contrasenaDefinida, titulo) => {
    servidorFalso({
      'GET /api/acceso/estado': json(200, { contrasenaDefinida }),
      'GET /api/sesion': vacia(titulo === 'Sesión iniciada' ? 204 : 401),
    });

    renderizarApp('/');

    expect(await screen.findByRole('heading', { name: titulo })).toBeTruthy();
    expect(screen.getByRole('img', { name: 'Logo de la óptica' }).getAttribute('src')).toBe('/api/marca/logo');
  });

  it('App_RutaCambiarContrasena_MuestraLaPantallaConElLogo', async () => {
    servidorFalso({ 'GET /api/sesion': vacia(204) });

    renderizarApp('/cambiar-contrasena');

    expect(await screen.findByRole('heading', { name: 'Cambiar la contraseña' })).toBeTruthy();
    expect(screen.getByTestId('ubicacion').textContent).toBe('/cambiar-contrasena');
    expect(screen.getByRole('img', { name: 'Logo de la óptica' }).getAttribute('src')).toBe('/api/marca/logo');
  });

  it.each([
    ['/articulos', 'Artículos'],
    ['/articulos/7', 'Editar artículo'],
    // La ruta del alta sigue siendo el alta, no la edición de un artículo con código "nuevo".
    ['/articulos/nuevo', 'Nuevo artículo'],
    ['/presupuestos/nuevo', 'Nuevo presupuesto'],
    // El alta tampoco es el presupuesto número "nuevo".
    ['/presupuestos/155', 'Presupuesto'],
  ])('App_Ruta%s_MuestraSuPantalla', async (ruta, titulo) => {
    servidorFalso({ 'GET /api/sesion': vacia(204) });

    renderizarApp(ruta);

    expect(await screen.findByRole('heading', { name: titulo })).toBeTruthy();
    expect(screen.getByTestId('ubicacion').textContent).toBe(ruta);
  });
});

/**
 * Palabras enteras (con su plural) de los datos de ARCA que ninguna pantalla muestra (FR-06, AC-03). Enteras para no
 * confundir otras palabras que las contienen.
 */
const DATOS_ARCA = /(?<![\p{L}\p{N}])(certificados?|puntos? de venta)(?![\p{L}\p{N}])/iu;

/** Atributos que el navegador muestra o que lee un lector de pantalla, además del texto. */
const ATRIBUTOS_VISIBLES = ['alt', 'title', 'placeholder', 'aria-label', 'value'];

/**
 * El texto de la pantalla, nodo por nodo, y el de sus atributos visibles, sin la sonda de ubicación de las pruebas.
 * Cada nodo va en su línea: `textContent` los pegaría ("contraseñaPunto de venta") y la búsqueda de palabras enteras
 * no vería el límite.
 */
function textoDeLaPantalla(): string {
  const cuerpo = document.body.cloneNode(true) as HTMLElement;
  cuerpo.querySelectorAll('[data-testid="ubicacion"]').forEach((sonda) => sonda.remove());
  const textos: string[] = [];
  const recorrido = document.createTreeWalker(cuerpo, NodeFilter.SHOW_TEXT);
  for (let nodo = recorrido.nextNode(); nodo !== null; nodo = recorrido.nextNode()) {
    textos.push(nodo.nodeValue ?? '');
  }
  const atributos = [...cuerpo.querySelectorAll('*')].flatMap((elemento) =>
    ATRIBUTOS_VISIBLES.map((atributo) => elemento.getAttribute(atributo) ?? ''),
  );
  return [...textos, ...atributos].join('\n');
}

/**
 * Las rutas que declara App.tsx (sin el comodín), leídas de su código para que una ruta nueva no quede afuera. Solo
 * reconoce `<Route path="literal"`: si hay más `<Route` que rutas reconocidas, alguna está escrita de otra forma
 * (`path={CONSTANTE}`, comillas simples, otro orden de atributos) y la prueba falla en lugar de dejarla sin revisar.
 */
function rutasDeLaApp(): string[] {
  const declaradas = fuenteApp.match(/<Route\s/g)?.length ?? 0;
  const reconocidas = [...fuenteApp.matchAll(/<Route\s+path="([^"]+)"/g)].map((coincidencia) => coincidencia[1]!);
  expect(
    reconocidas.length,
    'App.tsx tiene una ruta escrita de una forma que esta prueba no reconoce: agregala a PANTALLAS o adaptá la ' +
      'extracción de rutasDeLaApp',
  ).toBe(declaradas);
  return reconocidas.filter((r) => r !== '*');
}

interface Pantalla {
  ruta: string;
  /** La dirección que se abre, para una ruta con parámetros (`/articulos/:codigo`); por defecto, `ruta`. */
  url?: string;
  descripcion: string;
  preparar: () => void;
  titulo: string;
  estadoNavegacion?: unknown;
  /** Lo que hace la persona para llegar a ese estado de la pantalla (por ejemplo, un ingreso bloqueado). */
  interactuar?: () => Promise<void>;
}

const PANTALLAS: Pantalla[] = [
  {
    ruta: '/',
    descripcion: 'inicio',
    preparar: () =>
      servidorFalso({ 'GET /api/acceso/estado': json(200, { contrasenaDefinida: true }), 'GET /api/sesion': vacia(204) }),
    titulo: 'Sesión iniciada',
  },
  {
    ruta: '/',
    descripcion: 'inicio con el aviso del cambio',
    preparar: () =>
      servidorFalso({ 'GET /api/acceso/estado': json(200, { contrasenaDefinida: true }), 'GET /api/sesion': vacia(204) }),
    titulo: 'Sesión iniciada',
    estadoNavegacion: { mensaje: MENSAJE_CAMBIADA },
  },
  {
    ruta: '/definir-contrasena',
    descripcion: 'definición',
    preparar: () => servidorFalso({ 'GET /api/acceso/estado': json(200, { contrasenaDefinida: false }) }),
    titulo: 'Definí la contraseña de acceso',
  },
  {
    ruta: '/ingreso',
    descripcion: 'ingreso',
    preparar: () => servidorFalso({}),
    titulo: 'Ingresá al sistema',
  },
  {
    ruta: '/ingreso',
    descripcion: 'ingreso bloqueado',
    preparar: () => servidorFalso({ 'POST /api/acceso/ingresar': problemaGeneral(423, MENSAJE_BLOQUEADO) }),
    titulo: 'Ingresá al sistema',
    interactuar: async () => {
      fireEvent.change(screen.getByLabelText('Contraseña'), { target: { value: 'ocho1234' } });
      fireEvent.click(screen.getByRole('button', { name: 'Ingresar' }));
      expect(await screen.findByText(MENSAJE_BLOQUEADO)).toBeTruthy();
    },
  },
  {
    ruta: '/cambiar-contrasena',
    descripcion: 'cambio de contraseña',
    preparar: () => servidorFalso({ 'GET /api/sesion': vacia(204) }),
    titulo: 'Cambiar la contraseña',
  },
  {
    ruta: '/configuracion',
    descripcion: 'configuración sin tope',
    preparar: () =>
      servidorFalso({
        'GET /api/configuracion': json(200, {
          alicuotaIva: 21,
          condicionFiscal: 'responsable_inscripto',
          multiploRedondeo: 0.01,
          topeIdentificacion: null,
          faltaTope: true,
        }),
      }),
    titulo: 'Configuración',
  },
  {
    ruta: '/configuracion',
    descripcion: 'configuración con un error junto al campo',
    preparar: () =>
      servidorFalso({
        'GET /api/configuracion': json(200, {
          alicuotaIva: 21,
          condicionFiscal: 'monotributo',
          multiploRedondeo: 10,
          topeIdentificacion: 250000,
          faltaTope: false,
        }),
      }),
    titulo: 'Configuración',
    interactuar: async () => {
      fireEvent.change(await screen.findByLabelText('Alícuota de IVA (%)'), { target: { value: '101' } });
      fireEvent.click(screen.getByRole('button', { name: 'Grabar' }));
      expect(await screen.findByText('La alícuota de IVA debe estar entre 0 y 100.')).toBeTruthy();
    },
  },
  {
    ruta: '/articulos/nuevo',
    descripcion: 'alta de artículo grabado con advertencia',
    preparar: () =>
      servidorFalso({
        'GET /api/sesion': vacia(204),
        'POST /api/articulos': json(201, {
          codigo: 1,
          codigoProveedor: 'ABC-1',
          descripcion: 'Armazón metálico',
          precioCosto: -100,
          margenUtilidad: 50,
          precioVenta: -150,
          advertencia: 'Precio de costo negativo o cero',
          activo: true,
          version: 0,
        }),
      }),
    titulo: 'Nuevo artículo',
    interactuar: async () => {
      fireEvent.change(await screen.findByLabelText('Código en el proveedor'), { target: { value: 'ABC-1' } });
      fireEvent.change(screen.getByLabelText('Descripción'), { target: { value: 'Armazón metálico' } });
      fireEvent.change(screen.getByLabelText('Precio de costo ($)'), { target: { value: '-100' } });
      fireEvent.change(screen.getByLabelText('Margen de utilidad (%)'), { target: { value: '50' } });
      fireEvent.click(screen.getByRole('button', { name: 'Grabar' }));
      expect(await screen.findByText('Artículo grabado con el código 1.')).toBeTruthy();
    },
  },
  {
    ruta: '/articulos',
    descripcion: 'listado de artículos',
    preparar: () =>
      servidorFalso({
        'GET /api/articulos?pagina=1': json(200, {
          articulos: [
            { codigo: 1, codigoProveedor: 'ABC-1', descripcion: 'Armazón metálico', precioVenta: 1815, activo: true },
          ],
          pagina: 1,
          tamanoPagina: 50,
          total: 1,
        }),
      }),
    titulo: 'Artículos',
    interactuar: async () => {
      expect(await screen.findByText('Armazón metálico')).toBeTruthy();
    },
  },
  {
    ruta: '/articulos/:codigo',
    url: '/articulos/1',
    descripcion: 'edición de un artículo inactivo',
    preparar: () =>
      servidorFalso({
        'GET /api/articulos/1': json(200, {
          codigo: 1,
          codigoProveedor: 'ABC-1',
          descripcion: 'Armazón metálico',
          precioCosto: 1210,
          margenUtilidad: 50,
          precioVenta: 1815,
          advertencia: null,
          activo: false,
          version: 2,
        }),
      }),
    titulo: 'Editar artículo',
    interactuar: async () => {
      expect(await screen.findByText('Este artículo está inactivo. Reactivalo para editarlo.')).toBeTruthy();
    },
  },
  {
    ruta: '/presupuestos/nuevo',
    descripcion: 'alta de presupuesto con una línea y errores junto a los campos',
    preparar: () =>
      servidorFalso({
        'GET /api/sesion': vacia(204),
        'GET /api/articulos?texto=armazon&pagina=1&soloActivos=true': json(200, {
          articulos: [
            { codigo: 1, codigoProveedor: 'ABC-1', descripcion: 'Armazón metálico', precioVenta: 1850, activo: true },
          ],
          pagina: 1,
          tamanoPagina: 50,
          total: 1,
        }),
      }),
    titulo: 'Nuevo presupuesto',
    interactuar: async () => {
      fireEvent.change(await screen.findByLabelText('Texto a buscar'), { target: { value: 'armazon' } });
      fireEvent.click(screen.getByRole('button', { name: 'Buscar' }));
      fireEvent.click(await screen.findByRole('button', { name: 'Agregar 1 Armazón metálico' }));
      fireEvent.change(screen.getByLabelText('Email'), { target: { value: 'ana@' } });
      fireEvent.click(screen.getByRole('button', { name: 'Grabar' }));
      expect(await screen.findByText('Ingresá el DNI del cliente')).toBeTruthy();
    },
  },
  {
    ruta: '/presupuestos/:numero',
    url: '/presupuestos/155',
    descripcion: 'presupuesto grabado con la confirmación',
    preparar: () =>
      servidorFalso({
        'GET /api/presupuestos/155': json(200, {
          numero: 155,
          fecha: '2026-03-10',
          estado: 'borrador',
          cliente: {
            apellido: 'Pérez',
            nombre: 'Ana',
            dni: '23456789',
            domicilio: 'Calle Falsa 123',
            email: 'ana@mail.com',
            telefono: '11 5555-0000',
          },
          lineas: [
            {
              codigoArticulo: 1,
              descripcion: 'Armazón metálico',
              precioUnitario: 1850,
              cantidad: 1,
              porcentajeDescuento: 0,
              precioConDescuento: 1850,
              precioFinal: 1850,
            },
          ],
          total: 1850,
        }),
      }),
    titulo: 'Presupuesto',
    estadoNavegacion: { mensaje: 'Presupuesto N.º 155 grabado. Estado: Borrador.' },
    interactuar: async () => {
      expect(await screen.findByText('10/03/2026')).toBeTruthy();
    },
  },
];

describe('Sin datos de ARCA en pantalla', () => {
  it('Pantallas_NingunaMuestraDatosArca', async () => {
    // Cada ruta de la App tiene al menos una pantalla revisada: una ruta nueva sin revisar hace fallar la prueba.
    const rutas = rutasDeLaApp();
    expect(rutas.length).toBeGreaterThan(0);
    expect(
      rutas.filter((ruta) => !PANTALLAS.some((p) => p.ruta === ruta)),
      'Rutas de App.tsx sin una pantalla en PANTALLAS',
    ).toEqual([]);

    for (const pantalla of PANTALLAS) {
      pantalla.preparar();
      const url = pantalla.url ?? pantalla.ruta;
      renderizarApp(pantalla.estadoNavegacion === undefined ? url : { pathname: url, state: pantalla.estadoNavegacion });
      expect(await screen.findByRole('heading', { name: pantalla.titulo })).toBeTruthy();
      await pantalla.interactuar?.();

      const encontrado = DATOS_ARCA.exec(textoDeLaPantalla());
      expect(
        encontrado?.[0] ?? null,
        `La pantalla de ${pantalla.descripcion} (${pantalla.ruta}) muestra un dato de ARCA`,
      ).toBeNull();
      cleanup();
    }
  });

  it('DatosArca_SoloPalabrasEnteras', () => {
    expect(DATOS_ARCA.test('Punto de Venta 3')).toBe(true);
    expect(DATOS_ARCA.test('certificados vencidos')).toBe(true);
    expect(DATOS_ARCA.test('concepto y receptor')).toBe(false);
    expect(DATOS_ARCA.test('precertificado')).toBe(false);
    expect(DATOS_ARCA.test('certificadora')).toBe(false);
  });
});
