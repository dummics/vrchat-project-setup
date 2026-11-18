import { useState } from 'react';
import { useText } from './locale';
import type { PresetLibrary } from './Presets';

type Policy = Record<string, string>;
type CatalogPackage = { id: string; name: string; latest?: string };
type VersionPicker = { item: CatalogPackage; versions: string[]; latest?: string };

type Props = {
  library: PresetLibrary;
  selection: Policy;
  busy: boolean;
  request<T>(method: string, data?: unknown): Promise<T>;
  run(label: string, action: () => Promise<void>): Promise<void>;
  onChange(selection: Policy): void;
  onSaved(library: PresetLibrary): Promise<void>;
};

export function NewProjectPackages({ library, selection, busy, request, run, onChange, onSaved }: Props) {
  const t = useText();
  const [query, setQuery] = useState('');
  const [results, setResults] = useState<CatalogPackage[]>();
  const [picker, setPicker] = useState<VersionPicker>();
  const [version, setVersion] = useState('');
  const [presetName, setPresetName] = useState<string>();
  const catalog = new Map([...library.catalog, ...(results ?? [])].map(item => [item.id, item]));
  const available = (results ?? []).filter(item => !(item.id in selection));

  function openVersion(item: CatalogPackage) {
    void run(t('Lettura versioni…'), async () => {
      const options = await request<{ versions: string[]; latest?: string }>('projectPackageVersions', item.id);
      const current = selection[item.id];
      const versions = current && current !== 'latest' && !options.versions.includes(current)
        ? [current, ...options.versions] : options.versions;
      setPicker({ item, versions, latest: options.latest });
      setVersion(current ?? (options.latest ? 'latest' : versions[0] ?? ''));
    });
  }

  return <section aria-label={t("Pacchetti del nuovo progetto")} className="new-project-packages">
    <div className="package">
      <strong>{t("Pacchetti del nuovo progetto")}</strong>
      <button disabled={busy || !Object.keys(selection).length} onClick={() => setPresetName('')}>{t("Salva come preset")}</button>
    </div>
    {presetName !== undefined && <form className="actions project-tools" onSubmit={event => {
      event.preventDefault();
      void run(t('Salvataggio preset…'), async () => {
        const next = await request<PresetLibrary>('editPreset', {
          revision: library.revision, action: 'create', newName: presetName, packages: selection
        });
        await onSaved(next);
        setPresetName(undefined);
      });
    }}>
      <input aria-label={t("Nome preset")} maxLength={80} value={presetName} onChange={event => setPresetName(event.target.value)} />
      <button disabled={busy || !presetName.trim()}>{t("Salva")}</button>
      <button type="button" onClick={() => setPresetName(undefined)}>{t("Annulla")}</button>
    </form>}
    <form className="actions project-tools" onSubmit={event => {
      event.preventDefault();
      void run(t('Ricerca pacchetti…'), async () => setResults(await request('searchProjectPackages', query)));
    }}>
      <input type="search" aria-label={t("Cerca nel catalogo VPM")} placeholder={t("Cerca nel catalogo VPM")}
        value={query} onChange={event => setQuery(event.target.value)} />
      <button disabled={busy || !query.trim()}>{t("Cerca")}</button>
    </form>
    {results && <div aria-label={t("Risultati catalogo")}>
      {available.length ? available.map(item => <div className="package" data-package-id={item.id} key={item.id}>
        <span>{item.name}</span>
        <button disabled={busy} onClick={() => openVersion(item)}>{t("Scegli versione")}</button>
      </div>) : <p>{t("Nessun pacchetto disponibile da aggiungere.")}</p>}
    </div>}
    {picker && <div className="package">
      <label>{picker.item.name} <select aria-label={t("Versione da installare")} value={version}
        onChange={event => setVersion(event.target.value)}>
        {!picker.versions.length && <option value="">{t("Nessuna versione disponibile")}</option>}
        {picker.latest && <option value="latest">{t("Ultima versione")} ({picker.latest})</option>}
        {picker.versions.map(value => <option key={value}>{value}</option>)}
      </select></label>
      <div className="actions">
        <button disabled={busy} onClick={() => setPicker(undefined)}>{t("Annulla")}</button>
        <button disabled={busy || !version} onClick={() => {
          onChange({ ...selection, [picker.item.id]: version }); setPicker(undefined);
        }}>{t("Seleziona versione")}</button>
      </div>
    </div>}
    {Object.entries(selection).map(([id, choice]) => {
      const item = catalog.get(id) ?? { id, name: id };
      const required = library.required.includes(id);
      return <div className="package" key={id} data-package-id={id}>
        <span>{item.name} · {choice === 'latest' ? [t("Ultima versione"), item.latest].filter(Boolean).join(' ') : choice}</span>
        <div className="actions">
          <button disabled={busy} onClick={() => openVersion(item)}>{t("Versione")}</button>
          {required ? <span>{t("Richiesto")}</span> : <button disabled={busy} onClick={() => {
            const next = { ...selection }; delete next[id]; onChange(next);
            if (picker?.item.id === id) setPicker(undefined);
          }}>{t("Escludi")}</button>}
        </div>
      </div>;
    })}
  </section>;
}
