import { expect, test, type APIRequestContext, type Page, type Request } from '@playwright/test';

/**
 * Búsqueda de presupuestos (FEAT-006: AC-01 a AC-10 y la medición de NFR-01 en la pantalla) de punta a punta, contra la
 * Api real que sirve la SPA compilada, en Chrome y Edge (AC-17).
 *
 * Este archivo tiene su propio proyecto por navegador, con su propia Api y una base nueva. La primera prueba que corre
 * define la contraseña de esa base y las demás ingresan con ella (`ingresar` resuelve los dos casos), así que las
 * pruebas no dependen del orden. Cada prueba deja por la Api Responsable Inscripto, alícuota 21 y múltiplo 0,01, crea
 * sus artículos con códigos en el proveedor propios ("PB-…") y graba sus presupuestos por la Api, como la app: en
 * Borrador y, si hace falta un Final, después pasa a Final (AGENTS.md). Ninguna fecha se modifica en la base: todos los
 * presupuestos tienen la fecha del día en que se graban, así que los filtros de fecha se prueban alrededor de ese día
 * (el anterior, el mismo y el siguiente). Los rangos con fechas de marzo de 2026 de AC-04 a AC-06 se prueban en .NET con
 * reloj falso; acá se prueba lo mismo en la pantalla: límites inclusive, una sola fecha cargada y Desde posterior a
 * Hasta.
 *
 * Solo la primera prueba graba Apellidos con "González" y "Gómez", Nombres con "María" y un DNI que contiene "23456":
 * así las búsquedas de AC-01 a AC-03 tienen un resultado exacto aunque las otras pruebas ya hayan grabado. La de AC-08
 * completa la base hasta el presupuesto 120 (falla si ya lo pasó) y recién entonces busca sin filtros.
 *
 * Datos personales: los clientes son ficticios, con DNI improbables (99.xxx.xxx) y emails en example.com.
 */

/** Contraseña de prueba de esta base, no de nadie. */
const CONTRASENA = 'e2e-presupuestos-busqueda-1';

const TITULO_PANTALLA = 'Buscar presupuestos';
const TITULO_RESULTADOS = 'Presupuestos encontrados';
const TITULO_LINEAS = 'Líneas del presupuesto';
const MENSAJE_SIN_COINCIDENCIAS = 'No hay presupuestos que coincidan con la búsqueda.';
const MENSAJE_DESDE_POSTERIOR_A_HASTA = 'La fecha Desde no puede ser posterior a Hasta.';

/** Configuración de partida de cada prueba, como la pide el POST de la Api. */
const PARTIDA = {
  alicuotaIva: 21,
  condicionFiscal: 'responsable_inscripto',
  multiploRedondeo: 0.01,
  topeIdentificacion: null,
};

interface Cliente {
  apellido: string;
  nombre: string;
  dni: string;
  domicilio?: string;
  email?: string;
  telefono?: string;
}

/**
 * AC-01 a AC-03 y AC-09: datos ficticios. El DNI 99.923.456 es ficticio e improbable (no es el del ejemplo del PRD) y
 * contiene "3456" y "23456", lo que buscan AC-03 con "3456" y "23.456".
 */
const GONZALEZ: Cliente = {
  apellido: 'González',
  nombre: 'María José',
  dni: '99.923.456',
  email: 'maria.jose.busqueda@example.com',
};
const GOMEZ: Cliente = { apellido: 'Gómez', nombre: 'Ana', dni: '99.999.998' };
/** El cliente de las altas en cantidad de AC-08: no coincide con ninguna búsqueda de AC-01 a AC-03. */
const PAGINADO: Cliente = { apellido: 'Ficticio Paginado', nombre: 'Cliente de Búsqueda', dni: '99.999.999' };
/** El cliente de la medición de NFR-01. */
const MEDIDO: Cliente = { apellido: 'Ficticio Medido', nombre: 'Cliente de Medición', dni: '99.999.997' };

interface Articulo {
  codigo: number;
  precioVenta: number;
}

interface PresupuestoGrabado {
  numero: number;
  fecha: string;
  estado: string;
  total: number;
  version: number;
}

/** Los filtros de la pantalla; un campo ausente queda vacío. Fechas `yyyy-MM-dd`, como las recibe un campo de fecha. */
interface Filtros {
  apellido?: string;
  nombre?: string;
  dni?: string;
  desde?: string;
  hasta?: string;
}

let violacionesCsp: string[] = [];

test.beforeEach(({ page }) => {
  violacionesCsp = [];
  page.on('console', (mensaje) => {
    if (/Content Security Policy/i.test(mensaje.text())) {
      violacionesCsp.push(mensaje.text());
    }
  });
});

test.afterEach(() => {
  expect(violacionesCsp, 'La SPA no debe violar Content-Security-Policy').toEqual([]);
});

const campo = (page: Page, etiqueta: string) => page.getByLabel(etiqueta, { exact: true });
const botonBuscar = (page: Page) => page.getByRole('button', { name: 'Buscar', exact: true });
const tablaResultados = (page: Page) => page.getByRole('table', { name: TITULO_RESULTADOS });
const filasResultados = (page: Page) => tablaResultados(page).locator('tbody tr');
const paginacion = (page: Page) => page.getByRole('navigation', { name: 'Páginas de los resultados' });
const escaparRegex = (texto: string) => texto.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
const dato = (page: Page, etiqueta: string) =>
  page
    .locator('dl.datos > div')
    .filter({ has: page.locator('dt', { hasText: new RegExp(`^${escaparRegex(etiqueta)}$`) }) })
    .locator('dd');

const esBusqueda = (pedido: Request) =>
  pedido.method() === 'POST' && new URL(pedido.url()).pathname === '/api/presupuestos/busqueda';

/** El DNI como lo guarda la Api: solo los dígitos. */
const soloDigitos = (dni: string) => dni.replace(/\D/g, '');

/** `yyyy-MM-dd` como dd/mm/aaaa. */
function comoSeMuestra(fecha: string): string {
  const [anio, mes, dia] = fecha.split('-');
  return `${dia}/${mes}/${anio}`;
}

/** Un importe como lo muestra la pantalla: punto de miles, coma decimal y 2 decimales ("1.210,00"). */
function importe(valor: number): string {
  const [entero, decimales] = valor.toFixed(2).split('.');
  return `${entero!.replace(/\B(?=(\d{3})+$)/g, '.')},${decimales}`;
}

/** El día anterior o siguiente de una fecha `yyyy-MM-dd`, sin pasar por la zona horaria de la máquina. */
function otroDia(fecha: string, dias: number): string {
  const [anio, mes, dia] = fecha.split('-').map(Number);
  return new Date(Date.UTC(anio!, mes! - 1, dia! + dias)).toISOString().slice(0, 10);
}

/**
 * Entra como la operadora: en la base nueva define la contraseña; después ingresa con ella. Termina en el inicio con
 * la sesión iniciada y deja por la Api la configuración de partida.
 */
async function ingresarConConfiguracion(page: Page) {
  await page.goto('/');
  await expect(page).toHaveURL(/\/(definir-contrasena|ingreso)$/);
  await campo(page, 'Contraseña').fill(CONTRASENA);
  if (/\/definir-contrasena$/.test(page.url())) {
    await page.getByRole('button', { name: 'Definir contraseña' }).click();
  } else {
    await page.getByRole('button', { name: 'Ingresar' }).click();
  }
  await expect(page.getByRole('heading', { name: 'Sesión iniciada' })).toBeVisible();
  const respuesta = await page.request.post('/api/configuracion', { data: PARTIDA });
  expect(respuesta.status(), 'No se pudo fijar la configuración de partida').toBe(200);
}

/** Graba por la Api un artículo con ese costo y sin margen; devuelve lo que respondió. */
async function crearArticulo(request: APIRequestContext, codigoProveedor: string, precioCosto: number) {
  const respuesta = await request.post('/api/articulos', {
    data: { codigoProveedor, descripcion: `Artículo de búsqueda ${codigoProveedor}`, precioCosto, margenUtilidad: 0 },
  });
  expect(respuesta.status(), `No se pudo crear ${codigoProveedor}`).toBe(201);
  return (await respuesta.json()) as Articulo;
}

/** Graba un presupuesto en Borrador por la Api con una línea de ese artículo; devuelve lo que respondió. */
async function grabarPorApi(
  request: APIRequestContext,
  cliente: Cliente,
  codigoArticulo: number,
): Promise<PresupuestoGrabado> {
  const respuesta = await request.post('/api/presupuestos', {
    data: { cliente, lineas: [{ codigoArticulo, cantidad: 1, porcentajeDescuento: 0 }] },
  });
  expect(respuesta.status(), 'No se pudo grabar el presupuesto por la Api').toBe(201);
  const grabado = (await respuesta.json()) as PresupuestoGrabado;
  expect(grabado.estado).toBe('borrador');
  return grabado;
}

/** Pasa a Final por la Api, con la versión leída, como lo hace la pantalla al confirmar. */
async function pasarAFinalPorApi(request: APIRequestContext, presupuesto: PresupuestoGrabado) {
  const respuesta = await request.put(`/api/presupuestos/${presupuesto.numero}/estado`, {
    data: { estado: 'final', version: presupuesto.version },
  });
  expect(respuesta.status(), 'No se pudo pasar a Final por la Api').toBe(200);
  const final = (await respuesta.json()) as PresupuestoGrabado;
  expect(final.estado).toBe('final');
  return final;
}

/** Abre la búsqueda desde el inicio, como la operadora. */
async function abrirBusqueda(page: Page) {
  await page.goto('/');
  await page.getByRole('link', { name: 'Buscar presupuestos' }).click();
  await expect(page.getByRole('heading', { name: TITULO_PANTALLA })).toBeVisible();
  await expect(page).toHaveURL(/\/presupuestos$/);
}

/** Carga los cinco filtros (un filtro ausente queda vacío). */
async function cargarFiltros(page: Page, filtros: Filtros) {
  await campo(page, 'Apellido').fill(filtros.apellido ?? '');
  await campo(page, 'Nombre').fill(filtros.nombre ?? '');
  await campo(page, 'DNI').fill(filtros.dni ?? '');
  await campo(page, 'Desde').fill(filtros.desde ?? '');
  await campo(page, 'Hasta').fill(filtros.hasta ?? '');
}

/**
 * Carga los filtros, pulsa Buscar y espera la respuesta. Verifica que los filtros viajaron solo en el cuerpo (ADR-019,
 * R-02): la URL del pedido no tiene consulta y la de la pantalla no cambia.
 */
async function buscar(page: Page, filtros: Filtros) {
  await cargarFiltros(page, filtros);
  const [pedido] = await Promise.all([page.waitForRequest(esBusqueda), botonBuscar(page).click()]);
  const respuesta = await pedido.response();
  expect(respuesta?.status(), 'La búsqueda tiene que responder 200').toBe(200);
  expect(new URL(pedido.url()).search).toBe('');
  expect(new URL(page.url()).search).toBe('');
  await expect(botonBuscar(page)).toBeEnabled();
}

/** Los números de las filas de la grilla, en el orden en que se muestran. */
async function numerosDeLaGrilla(page: Page): Promise<number[]> {
  const textos = await filasResultados(page).locator('td:first-child').allTextContents();
  return textos.map((texto) => Number(texto.trim()));
}

/** Busca y espera la grilla con exactamente esos números (o el mensaje sin coincidencias si la lista es vacía). */
async function buscarYEsperar(page: Page, filtros: Filtros, numeros: number[]) {
  await buscar(page, filtros);
  if (numeros.length === 0) {
    await expect(page.getByText(MENSAJE_SIN_COINCIDENCIAS, { exact: true })).toBeVisible();
    await expect(tablaResultados(page)).toHaveCount(0);
    return;
  }
  // Con espera: la grilla anterior puede seguir en pantalla hasta que React dibuja la respuesta.
  await expect.poll(() => numerosDeLaGrilla(page)).toEqual(numeros);
}

test('búsqueda por apellido, nombre, DNI y fechas', async ({ page }) => {
  await ingresarConConfiguracion(page);
  const articulo = await crearArticulo(page.request, 'PB-FILTROS', 1000);
  // González queda en Final (AC-09 muestra su estado) y Gómez en Borrador.
  const gonzalez = await pasarAFinalPorApi(
    page.request,
    await grabarPorApi(page.request, GONZALEZ, articulo.codigo),
  );
  const gomez = await grabarPorApi(page.request, GOMEZ, articulo.codigo);
  const hoy = gonzalez.fecha;
  // Los dos se grabaron el mismo día: los rangos de abajo valen para ambos.
  expect(gomez.fecha).toBe(hoy);
  const ayer = otroDia(hoy, -1);
  const manana = otroDia(hoy, 1);
  await abrirBusqueda(page);

  // AC-01: coincidencia parcial sin acentos ni mayúsculas; Gómez no coincide.
  await buscarYEsperar(page, { apellido: 'ONZALEZ' }, [gonzalez.numero]);

  // AC-09: el resultado muestra número, fecha, Apellido, Nombre, DNI, estado y total.
  await expect(tablaResultados(page).locator('thead th')).toHaveText([
    'Número',
    'Fecha',
    'Apellido',
    'Nombre',
    'DNI',
    'Estado',
    'Total ($)',
  ]);
  await expect(filasResultados(page).first().locator('td')).toHaveText([
    String(gonzalez.numero),
    comoSeMuestra(hoy),
    'González',
    'María José',
    soloDigitos(GONZALEZ.dni),
    'Final',
    importe(gonzalez.total),
  ]);
  // El total es el de la línea grabada: 1000 + 21 % de IVA.
  expect(gonzalez.total).toBe(articulo.precioVenta);

  // AC-02: el Nombre "maria" encuentra "María José" y no "Ana".
  await buscarYEsperar(page, { nombre: 'maria' }, [gonzalez.numero]);

  // AC-03: parte del DNI, con o sin puntos.
  await buscarYEsperar(page, { dni: '3456' }, [gonzalez.numero]);
  await buscarYEsperar(page, { dni: '23.456' }, [gonzalez.numero]);

  // AC-04: Apellido y Desde sin Hasta; Desde es inclusive.
  await buscarYEsperar(page, { apellido: 'González', desde: manana }, []);
  await buscarYEsperar(page, { apellido: 'González', desde: hoy }, [gonzalez.numero]);

  // AC-05: Desde y Hasta, los dos inclusive; un rango que no incluye el día no muestra nada.
  await buscarYEsperar(page, { apellido: 'ómez', desde: hoy, hasta: hoy }, [gomez.numero]);
  await buscarYEsperar(page, { apellido: 'ómez', desde: ayer, hasta: ayer }, []);
  // "GO" está en los dos Apellidos y en ninguno de las otras pruebas de este archivo.
  await buscarYEsperar(page, { apellido: 'GO', desde: ayer, hasta: manana }, [gomez.numero, gonzalez.numero]);

  // AC-06: solo Hasta; Hasta es inclusive y no muestra los posteriores.
  await buscarYEsperar(page, { apellido: 'González', hasta: hoy }, [gonzalez.numero]);
  await buscarYEsperar(page, { apellido: 'González', hasta: ayer }, []);

  // AC-07: Desde posterior a Hasta no busca y avisa junto a Desde.
  const pedidos: Request[] = [];
  const registrar = (pedido: Request) => {
    if (esBusqueda(pedido)) pedidos.push(pedido);
  };
  page.on('request', registrar);
  await cargarFiltros(page, { apellido: 'González', desde: manana, hasta: hoy });
  await botonBuscar(page).click();
  const desde = campo(page, 'Desde');
  await expect(desde).toHaveAttribute('aria-invalid', 'true');
  const idError = await desde.getAttribute('aria-describedby');
  expect(idError, 'El mensaje tiene que estar vinculado al campo Desde').toBeTruthy();
  await expect(page.locator(`[id="${idError}"]`)).toHaveText(MENSAJE_DESDE_POSTERIOR_A_HASTA);
  await expect(campo(page, 'Hasta')).not.toHaveAttribute('aria-invalid', 'true');
  // La grilla anterior no queda como si fuera el resultado.
  await expect(tablaResultados(page)).toHaveCount(0);

  // Enter en un campo busca, como Buscar (con el rango corregido). El listener sigue activo: si el rechazo de AC-07
  // hubiera enviado algo, aparecería acá como un pedido de más.
  await cargarFiltros(page, { apellido: 'González', desde: hoy, hasta: hoy });
  await Promise.all([page.waitForRequest(esBusqueda), campo(page, 'Apellido').press('Enter')]);
  await expect(filasResultados(page)).toHaveCount(1);
  expect(await numerosDeLaGrilla(page)).toEqual([gonzalez.numero]);
  page.off('request', registrar);
  expect(pedidos, 'AC-07 no envía la búsqueda; Enter envía exactamente una').toHaveLength(1);
  expect(JSON.parse(pedidos[0]!.postData() ?? '{}')).toEqual({
    apellido: 'González',
    desde: hoy,
    hasta: hoy,
    pagina: 1,
  });
});

test('paginación y abrir un resultado', async ({ page }) => {
  // Hasta 120 altas por la Api, una tras otra: más que el plazo por defecto de 30 s en una PC lenta.
  test.setTimeout(180_000);
  await ingresarConConfiguracion(page);
  const articulo = await crearArticulo(page.request, 'PB-PAGINAS', 500);

  // Completa la base hasta el presupuesto 120: los números son consecutivos desde el 1 (AC-08).
  let ultimo = (await grabarPorApi(page.request, PAGINADO, articulo.codigo)).numero;
  expect(ultimo, 'La base ya pasó el 120 antes de esta prueba').toBeLessThanOrEqual(120);
  while (ultimo < 120) {
    const siguiente = (await grabarPorApi(page.request, PAGINADO, articulo.codigo)).numero;
    expect(siguiente).toBe(ultimo + 1);
    ultimo = siguiente;
  }
  expect((await page.request.get('/api/presupuestos/121')).status()).toBe(404);
  await abrirBusqueda(page);

  // AC-08: sin filtros lista todos, de mayor a menor, en páginas de 50.
  await buscar(page, {});
  await expect(filasResultados(page)).toHaveCount(50);
  expect(await numerosDeLaGrilla(page)).toEqual(Array.from({ length: 50 }, (_, i) => 120 - i));
  await expect(paginacion(page)).toContainText('Página 1 de 3');
  await expect(paginacion(page).getByRole('button', { name: 'Anterior' })).toBeDisabled();

  await paginacion(page).getByRole('button', { name: 'Siguiente' }).click();
  await expect(paginacion(page)).toContainText('Página 2 de 3');
  await expect(filasResultados(page)).toHaveCount(50);
  expect(await numerosDeLaGrilla(page)).toEqual(Array.from({ length: 50 }, (_, i) => 70 - i));

  await paginacion(page).getByRole('button', { name: 'Siguiente' }).click();
  await expect(paginacion(page)).toContainText('Página 3 de 3');
  await expect(filasResultados(page)).toHaveCount(20);
  expect(await numerosDeLaGrilla(page)).toEqual(Array.from({ length: 20 }, (_, i) => 20 - i));
  await expect(paginacion(page).getByRole('button', { name: 'Siguiente' })).toBeDisabled();

  // AC-10: abrir un resultado muestra ese presupuesto con sus datos y sus líneas.
  const abierto = await page.request.get('/api/presupuestos/7');
  expect(abierto.status()).toBe(200);
  const presupuesto7 = (await abierto.json()) as PresupuestoGrabado & {
    cliente: { apellido: string; nombre: string; dni: string };
  };
  await page.getByRole('link', { name: 'Abrir el presupuesto 7', exact: true }).click();
  await expect(page).toHaveURL(/\/presupuestos\/7$/);
  await expect(page.getByRole('heading', { name: 'Presupuesto', exact: true })).toBeVisible();
  await expect(dato(page, 'Número')).toHaveText('7');
  await expect(dato(page, 'Apellido')).toHaveText(presupuesto7.cliente.apellido);
  await expect(dato(page, 'Nombre')).toHaveText(presupuesto7.cliente.nombre);
  await expect(dato(page, 'DNI')).toHaveText(presupuesto7.cliente.dni);
  await expect(page.getByRole('table', { name: TITULO_LINEAS }).locator('tbody tr')).toHaveCount(1);
});

test('buscar muestra la grilla en menos de 2 s', async ({ page }) => {
  // NFR-01 en la pantalla (D-03): desde la pulsación de Buscar hasta que la grilla está visible. El volumen de AC-16
  // (10.000 presupuestos) se mide en las pruebas de la Api; la grilla dibuja como máximo 50 filas.
  await ingresarConConfiguracion(page);
  const articulo = await crearArticulo(page.request, 'PB-MEDICION', 800);
  const grabados: number[] = [];
  for (let i = 0; i < 3; i++) {
    grabados.unshift((await grabarPorApi(page.request, MEDIDO, articulo.codigo)).numero);
  }
  await abrirBusqueda(page);
  await cargarFiltros(page, { apellido: 'Medido' });

  const inicio = Date.now();
  await botonBuscar(page).click();
  await expect(filasResultados(page)).toHaveCount(3);
  const demora = Date.now() - inicio;

  expect(await numerosDeLaGrilla(page)).toEqual(grabados);
  expect(demora, `La grilla tardó ${demora} ms desde la pulsación de Buscar`).toBeLessThan(2000);
});
