import { expect, type Page } from '@playwright/test';

/**
 * Medidas compartidas por `inicio-menu.spec.ts` y `estilo-uniforme.spec.ts` (FEAT-012): el contraste WCAG 2.1 calculado
 * desde los estilos computados, la espera de la marca y de las transiciones, y el ancho de la página. Como no termina en
 * `.spec.ts`, no es un proyecto de Playwright (igual que `xlsx-minimo.ts`).
 *
 * El contraste se calcula en dos partes. {@link muestrear} corre en el navegador (por eso no usa nada de afuera): junta,
 * por cada elemento, su texto, su color y la cadena de fondos y opacidades hasta la raíz del documento. Después
 * {@link contrastes}, {@link fondoEfectivo} y {@link contraste} componen esos datos en Node.
 */

/** Una capa de fondo de un elemento o de un ancestro, del elemento hacia la raíz. */
export interface Capa {
  /** `background-color` computado. */
  fondo: string;
  /** `background-image` computado (`none` si no tiene). */
  imagen: string;
  /** `opacity` computada del elemento de esta capa. */
  opacidad: number;
  /** El elemento, para los mensajes de error. */
  etiqueta: string;
}

/** Un texto medido: su contenido, su color y la cadena de capas desde su elemento hasta la raíz. */
export interface Muestra {
  texto: string;
  color: string;
  capas: Capa[];
}

/**
 * Corre en el navegador. Con `'textos'`, una muestra por cada elemento visible dentro de `raiz` con texto propio, y por
 * cada campo de texto con valor (desde FEAT-013 la web no tiene desplegables `select`: la alícuota y la condición
 * fiscal son campos de texto con sugerencias, D-13 y D-24). Se dejan afuera los controles deshabilitados (WCAG 1.4.3
 * los exceptúa), los SVG y lo que el navegador no dibuja o mide 1px o menos (textos solo para lectores de pantalla).
 * Las sugerencias abiertas son elementos con texto propio y se miden como cualquier otro. Con `'fondo'`, una sola
 * muestra de `raiz`, sin texto: sirve para el fondo de un elemento.
 */
export function muestrear(raiz: Element, modo: 'textos' | 'fondo'): Muestra[] {
  const capasDe = (elemento: Element) => {
    const capas: { fondo: string; imagen: string; opacidad: number; etiqueta: string }[] = [];
    for (let actual: Element | null = elemento; actual !== null; actual = actual.parentElement) {
      const estilo = getComputedStyle(actual);
      const clase = typeof actual.className === 'string' && actual.className !== '' ? `.${actual.className.split(' ')[0]}` : '';
      capas.push({
        fondo: estilo.backgroundColor,
        imagen: estilo.backgroundImage,
        opacidad: Number(estilo.opacity),
        etiqueta: `${actual.tagName.toLowerCase()}${clase}`,
      });
    }
    return capas;
  };
  if (modo === 'fondo') {
    return [{ texto: '', color: getComputedStyle(raiz).color, capas: capasDe(raiz) }];
  }
  const muestras: { texto: string; color: string; capas: ReturnType<typeof capasDe> }[] = [];
  for (const elemento of [raiz, ...raiz.querySelectorAll('*')]) {
    if (elemento.matches('script, style, svg, svg *') || elemento.closest(':disabled, [aria-disabled="true"]')) {
      continue;
    }
    const campo = elemento.matches('input:not([type="file"], [type="radio"], [type="checkbox"], [type="hidden"])');
    const propio = campo
      ? (elemento as HTMLInputElement).value.trim()
      : [...elemento.childNodes]
          .filter((nodo) => nodo.nodeType === Node.TEXT_NODE)
          .map((nodo) => nodo.textContent ?? '')
          .join('')
          .trim();
    const caja = elemento.getBoundingClientRect();
    if (propio === '' || !elemento.checkVisibility({ visibilityProperty: true }) || caja.width <= 1 || caja.height <= 1) {
      continue;
    }
    muestras.push({ texto: propio.slice(0, 60), color: getComputedStyle(elemento).color, capas: capasDe(elemento) });
  }
  return muestras;
}

type Rgba = [number, number, number, number];

/** Un color computado (`rgb()`, `rgba()` o `color(srgb …)`, con exponentes) como [r, g, b, a] con r, g, b de 0 a 255. */
export function aRgba(texto: string): Rgba {
  const numero = String.raw`(-?[\d.]+(?:e[-+]?\d+)?)`;
  const rgb = new RegExp(String.raw`^rgba?\(${numero},\s*${numero},\s*${numero}(?:,\s*${numero})?\)$`).exec(texto);
  if (rgb) {
    return [Number(rgb[1]), Number(rgb[2]), Number(rgb[3]), rgb[4] === undefined ? 1 : Number(rgb[4])];
  }
  const srgb = new RegExp(String.raw`^color\(srgb ${numero} ${numero} ${numero}(?: \/ ${numero})?\)$`).exec(texto);
  if (srgb) {
    return [Number(srgb[1]) * 255, Number(srgb[2]) * 255, Number(srgb[3]) * 255, srgb[4] === undefined ? 1 : Number(srgb[4])];
  }
  throw new Error(`Color computado sin interpretar: ${texto}`);
}

/** `arriba` (con su alfa) compuesto sobre el color opaco `abajo`. */
const sobre = (arriba: Rgba, abajo: number[]) => [0, 1, 2].map((i) => arriba[i] * arriba[3] + abajo[i] * (1 - arriba[3]));

/** La opacidad acumulada de cada capa: la suya por la de todos sus ancestros. */
const acumuladas = (capas: Capa[]) => {
  const resultado = new Array<number>(capas.length);
  let producto = 1;
  for (let i = capas.length - 1; i >= 0; i--) {
    producto *= capas[i].opacidad;
    resultado[i] = producto;
  }
  return resultado;
};

/**
 * El fondo opaco sobre el que se ve el elemento de `capas[0]`: las capas se componen desde la raíz (sobre blanco) hacia
 * el elemento, cada una con su alfa multiplicado por la opacidad acumulada de su elemento. Una imagen de fondo no se
 * compone: si el elemento que la lleva (o un ancestro) tiene opacidad menor que 1, el resultado no se puede calcular y
 * la medida falla con un error que lo dice, en lugar de dar un contraste inventado. Sin opacidad, la imagen se ignora:
 * la única de la hoja es la franja de 4px de la tarjeta del menú, que no queda debajo de ningún texto.
 */
export function fondoEfectivo(capas: Capa[]): number[] {
  const opacidades = acumuladas(capas);
  let fondo = [255, 255, 255];
  for (let i = capas.length - 1; i >= 0; i--) {
    const { fondo: color, imagen, etiqueta } = capas[i];
    if (imagen !== 'none' && opacidades[i] < 1) {
      throw new Error(`No se puede medir el contraste: ${etiqueta} tiene una imagen de fondo con opacidad ${opacidades[i]}.`);
    }
    const capa = aRgba(color);
    if (capa[3] > 0) {
      fondo = sobre([capa[0], capa[1], capa[2], capa[3] * opacidades[i]], fondo);
    }
  }
  return fondo;
}

const luminancia = ([r, g, b]: number[]) => {
  const canal = (valor: number) => {
    const c = valor / 255;
    return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
  };
  return 0.2126 * canal(r) + 0.7152 * canal(g) + 0.0722 * canal(b);
};

/** La relación de contraste WCAG 2.1 entre dos colores opacos (r, g, b de 0 a 255). */
export function contraste(a: number[], b: number[]): number {
  const [clara, oscura] = [luminancia(a), luminancia(b)].sort((x, y) => y - x);
  return (clara + 0.05) / (oscura + 0.05);
}

/**
 * El contraste de cada muestra: el color del texto, con su alfa multiplicado por la opacidad acumulada de su elemento
 * (la descripción de la tarjeta lleva 0,8), compuesto sobre su fondo efectivo.
 */
export function contrastes(muestras: Muestra[]): { texto: string; contraste: number }[] {
  return muestras.map(({ texto, color, capas }) => {
    const fondo = fondoEfectivo(capas);
    const [r, g, b, a] = aRgba(color);
    const visto = sobre([r, g, b, a * acumuladas(capas)[0]], fondo);
    return { texto, contraste: contraste(visto, fondo) };
  });
}

/**
 * Espera la marca aplicada y que terminen las transiciones (150 ms): la hoja arranca con el primario de ejemplo y
 * MarcaProvider lo cambia al de la marca del harness (#0903A0), así que sin esperar se mediría un color intermedio.
 */
export async function quieta(page: Page) {
  await expect
    .poll(() =>
      page.evaluate(() =>
        getComputedStyle(document.documentElement).getPropertyValue('--color-primario').trim().toUpperCase(),
      ),
    )
    .toBe('#0903A0');
  await page.evaluate(() => Promise.allSettled(document.getAnimations().map((animacion) => animacion.finished)));
}

/** El desplazamiento horizontal de la página: el ancho del documento contra el de la ventana. */
export const anchoDeLaPagina = (page: Page) =>
  page.evaluate(() => ({ documento: document.documentElement.scrollWidth, ventana: document.documentElement.clientWidth }));
