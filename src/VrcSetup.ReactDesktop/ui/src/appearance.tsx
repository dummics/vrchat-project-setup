import { useEffect } from 'react';
import { useText } from './locale';

export type Appearance = {
  theme: 'Dark' | 'Light' | 'System';
  colorPalette: string;
  canvasColor: string;
  surfaceColor: string;
  accentColor: string;
  strokeColor: string;
};
export type Palettes = Record<string, { canvas: string; surface: string; accent: string }>;

type Rgb = [number, number, number];
const white: Rgb = [255, 255, 255];
const black: Rgb = [0, 0, 0];
const fields = [
  ['canvasColor', 'Sfondo'], ['surfaceColor', 'Superfici'],
  ['accentColor', 'Colore principale'], ['strokeColor', 'Bordi']
] as const;

function rgb(value: string, fallback: string): Rgb {
  const hex = /^#[0-9a-f]{6}$/i.test(value) ? value : fallback;
  return [1, 3, 5].map(index => parseInt(hex.slice(index, index + 2), 16)) as Rgb;
}

function mix(from: Rgb, to: Rgb, weight: number): Rgb {
  return from.map((channel, index) => Math.round(channel + (to[index] - channel) * weight)) as Rgb;
}

function limitBrightness(color: Rgb, limit: number): Rgb {
  const peak = Math.max(...color);
  return peak > limit ? mix(color, black, 1 - limit / peak) : color;
}

function luminance(color: Rgb): number {
  const [r, g, b] = color.map(value => {
    const channel = value / 255;
    return channel <= .04045 ? channel / 12.92 : ((channel + .055) / 1.055) ** 2.4;
  });
  return .2126 * r + .7152 * g + .0722 * b;
}

export function useAppearance(value?: Appearance) {
  useEffect(() => {
    if (!value) return;
    const system = matchMedia('(prefers-color-scheme: dark)');
    function apply(settings: Appearance) {
      const dark = settings.theme === 'Dark' || (settings.theme === 'System' && system.matches);
      const root = document.documentElement;
      root.dataset.theme = dark ? 'dark' : 'light';
      const canvas = rgb(settings.canvasColor, '#181A1D');
      const surface = rgb(settings.surfaceColor, '#292D33');
      const accent = rgb(settings.accentColor, '#FFAD66');
      const stroke = rgb(settings.strokeColor, '#626974');
      let accentText = accent;
      while (dark ? luminance(accentText) < .52 : luminance(accentText) > .13)
        accentText = mix(accentText, dark ? white : black, .16);
      const colors = {
        canvas: dark ? limitBrightness(canvas, 75) : mix(canvas, white, .96),
        surface: dark ? limitBrightness(surface, 95) : mix(surface, white, .985),
        line: dark ? stroke : mix(stroke, white, .45),
        accent,
        'accent-text': accentText
      };
      for (const [key, channels] of Object.entries(colors)) {
        root.style.setProperty('--' + key, `rgb(${channels.join(', ')})`);
      }
      root.style.setProperty('--on-accent', luminance(accent) > .179 ? '#000' : '#fff');
    }
    const update = () => apply(value);
    update();
    system.addEventListener('change', update);
    return () => system.removeEventListener('change', update);
  }, [value]);
}

export function AppearanceForm({ value, palettes, onChange }: {
  value: Appearance; palettes: Palettes; onChange(value: Appearance): void;
}) {
  const t = useText();
  function selectPalette(colorPalette: string) {
    const palette = palettes[colorPalette];
    onChange(palette ? {
      ...value, colorPalette, canvasColor: palette.canvas, surfaceColor: palette.surface, accentColor: palette.accent
    } : { ...value, colorPalette });
  }

  return <section className="appearance-settings">
    <h2>{t("Aspetto")}</h2>
    <label className="package">{t("Tema")}
      <select id="appearance-theme" value={value.theme} onChange={event => onChange({ ...value, theme: event.target.value as Appearance['theme'] })}>
        <option value="System">{t("Sistema")}</option><option value="Dark">{t("Scuro")}</option><option value="Light">{t("Chiaro")}</option>
      </select>
    </label>
    <label className="package">{t("Palette colori")}
      <select id="appearance-palette" value={value.colorPalette} onChange={event => selectPalette(event.target.value)}>
        {Object.keys(palettes).map(id => <option value={id} key={id}>{id === 'Orange' ? t("Arancione") : id}</option>)}
        <option value="Custom">{t("Personalizzata")}</option>
      </select>
    </label>
    <div className="appearance-colors">
      {fields.map(([key, label]) => <label key={key}>
        <input id={key} type="color" value={value[key]} onChange={event => onChange({ ...value, [key]: event.target.value, colorPalette: 'Custom' })} />
        <span>{t(label)}</span>
      </label>)}
    </div>
  </section>;
}
