import { cleanup, render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import { afterEach, describe, expect, it } from 'vitest';
import { MenuInicio } from './MenuInicio';

afterEach(cleanup);

/** Nombre, destino y descripción de una opción. */
type Opcion = readonly [nombre: string, destino: string, descripcion: string];

/** Las secciones y opciones de la spec de FEAT-012 (FR-01), en el orden en que se dibujan. */
const ESPERADO: readonly { titulo: string; opciones: readonly Opcion[] }[] = [
  {
    titulo: 'Ventas',
    opciones: [
      ['Nuevo presupuesto', '/presupuestos/nuevo', 'Armá un presupuesto con artículos del catálogo.'],
      ['Buscar presupuestos', '/presupuestos', 'Encontrá presupuestos por fecha o por cliente.'],
      ['Facturas', '/facturas', 'Buscá facturas emitidas y descargá su PDF.'],
    ],
  },
  {
    titulo: 'Catálogo',
    opciones: [
      ['Artículos', '/articulos', 'Buscá, editá y actualizá los costos del catálogo.'],
      ['Nuevo artículo', '/articulos/nuevo', 'Cargá un artículo con su costo y su margen.'],
    ],
  },
  {
    titulo: 'Sistema',
    opciones: [
      ['Configuración', '/configuracion', 'Alícuota, redondeo, datos del emisor y backup.'],
      ['Cambiar la contraseña', '/cambiar-contrasena', 'Cambiá la contraseña de acceso al sistema.'],
    ],
  },
];

const OPCIONES = ESPERADO.flatMap((seccion) => seccion.opciones);

function dibujar() {
  return render(
    <MemoryRouter>
      <MenuInicio />
    </MemoryRouter>,
  );
}

describe('MenuInicio', () => {
  it('MenuInicio_TresSeccionesConSusOpciones', () => {
    // AC-01: tres secciones con título, cada una con sus opciones.
    dibujar();

    // El contenedor es una navegación con nombre propio, y las tres secciones están dentro.
    const navegacion = screen.getByRole('navigation', { name: 'Menú de Inicio' });
    expect(navegacion.tagName).toBe('NAV');
    expect(navegacion.classList.contains('menu')).toBe(true);
    const secciones = screen.getAllByRole('region');
    expect(secciones.every((seccion) => navegacion.contains(seccion))).toBe(true);
    expect(secciones.map((seccion) => seccion.getAttribute('aria-labelledby') !== null)).toEqual([true, true, true]);
    expect(secciones.map((seccion) => within(seccion).getByRole('heading', { level: 2 }).textContent)).toEqual(
      ESPERADO.map((seccion) => seccion.titulo),
    );
    ESPERADO.forEach((esperada, indice) => {
      const seccion = screen.getByRole('region', { name: esperada.titulo });
      expect(seccion).toBe(secciones[indice]);
      expect(within(seccion).getAllByRole('link').map((enlace) => enlace.textContent)).toEqual(
        esperada.opciones.map(([nombre]) => nombre),
      );
      expect(within(seccion).getAllByRole('listitem')).toHaveLength(esperada.opciones.length);
    });
  });

  it('MenuInicio_UnEnlacePorNombreConSuDestino', () => {
    // AC-03 y AC-02: un solo enlace por opción, con el nombre de hoy y el destino de hoy.
    dibujar();

    expect(screen.getAllByRole('link')).toHaveLength(7);
    for (const [nombre, destino] of OPCIONES) {
      const enlaces = screen.getAllByRole('link', { name: nombre });
      expect(enlaces).toHaveLength(1);
      expect(enlaces[0]?.getAttribute('href')).toBe(destino);
    }
  });

  it('MenuInicio_DescripcionFueraDelNombreDelEnlace', () => {
    // AC-03 y R-01: el enlace contiene solo el nombre; la descripción va afuera, asociada con aria-describedby.
    dibujar();

    for (const [nombre, , descripcion] of OPCIONES) {
      const enlace = screen.getByRole('link', { name: nombre });
      expect(enlace.textContent).toBe(nombre);
      expect(enlace.children).toHaveLength(0);
      expect(enlace.getAttribute('aria-label')).toBeNull();
      expect(enlace.getAttribute('aria-labelledby')).toBeNull();

      const idDescripcion = enlace.getAttribute('aria-describedby');
      expect(idDescripcion).toBeTruthy();
      const parrafo = document.getElementById(idDescripcion!);
      expect(parrafo?.tagName).toBe('P');
      expect(parrafo?.textContent).toBe(descripcion);
      expect(enlace.contains(parrafo)).toBe(false);
      expect(parrafo?.closest('li')).toBe(enlace.closest('li'));
      expect(enlace.closest('li')?.classList.contains('tarjeta-menu')).toBe(true);
      // D-09: las descripciones no usan las palabras que vigilan otras pruebas.
      expect(descripcion.toLowerCase()).not.toMatch(/restaurar|restablecer|punto de venta|certificado/);
    }
    // Los ids salen de useId: no se repiten.
    const ids = screen.getAllByRole('link').map((enlace) => enlace.getAttribute('aria-describedby'));
    expect(new Set(ids).size).toBe(7);
  });

  it('MenuInicio_OrdenDeLosEnlacesVentasCatalogoSistema', () => {
    // AC-04: el orden del DOM (el que recorre Tab) es Ventas, Catálogo y Sistema.
    dibujar();

    expect(screen.getAllByRole('link').map((enlace) => enlace.textContent)).toEqual([
      'Nuevo presupuesto',
      'Buscar presupuestos',
      'Facturas',
      'Artículos',
      'Nuevo artículo',
      'Configuración',
      'Cambiar la contraseña',
    ]);
  });

  it('MenuInicio_IconosDecorativosEnTodasLasOpciones', () => {
    // FR-02 y FR-06: un ícono SVG decorativo por tarjeta, sin texto propio ni estilos en línea, y los 7 distintos.
    const { container } = dibujar();

    const tarjetas = container.querySelectorAll('li.tarjeta-menu');
    expect(tarjetas).toHaveLength(7);
    const dibujos = new Set<string>();
    tarjetas.forEach((tarjeta) => {
      const iconos = tarjeta.querySelectorAll('svg');
      expect(iconos).toHaveLength(1);
      const icono = iconos[0]!;
      expect(icono.getAttribute('aria-hidden')).toBe('true');
      expect(icono.getAttribute('focusable')).toBe('false');
      expect(icono.getAttribute('width')).toBe('24');
      expect(icono.getAttribute('height')).toBe('24');
      expect(icono.getAttribute('viewBox')).toBe('0 0 24 24');
      expect(icono.getAttribute('stroke')).toBe('currentColor');
      expect(icono.querySelector('title, style, desc, text')).toBeNull();
      expect(icono.closest('a')).toBeNull();
      expect(icono.childElementCount).toBeGreaterThan(0);
      dibujos.add(icono.innerHTML);
    });
    expect(dibujos.size).toBe(7);
    // R-02: ningún atributo style ni <style> en todo el menú.
    expect(container.querySelector('[style], style')).toBeNull();
    expect(container.innerHTML).not.toContain('data:');
  });
});
