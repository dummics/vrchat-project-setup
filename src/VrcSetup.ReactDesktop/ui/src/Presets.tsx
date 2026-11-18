import { useState } from 'react';

type Policy = Record<string, string>;
type CatalogPackage = { id: string; name: string; latest?: string };
export type PresetLibrary = {
  revision: string;
  selected?: string;
  defaults: Policy;
  presets: Record<string, Policy>;
  favorites: string[];
  required: string[];
  catalog: CatalogPackage[];
};
type Props = {
  library: PresetLibrary;
  busy: boolean;
  request<T>(method: string, data?: unknown): Promise<T>;
  run(label: string, action: () => Promise<void>): Promise<void>;
  onChange(library: PresetLibrary): Promise<void>;
  onDirty(dirty: boolean): void;
};

export function Presets({ library, busy, request, run, onChange, onDirty }: Props) {
  const t = useText();
  const [selected, setSelected] = useState(library.selected ?? '');
  const [draft, setDraft] = useState<Policy>();
  const [nameEdit, setNameEdit] = useState<'create' | 'rename'>();
  const [newName, setNewName] = useState('');
  const [deleting, setDeleting] = useState(false);
  const [query, setQuery] = useState('');
  const [favoritesOnly, setFavoritesOnly] = useState(false);
  const [searchResults, setSearchResults] = useState<CatalogPackage[]>();
  const [versionPicker, setVersionPicker] = useState<{ item: CatalogPackage; versions: string[]; latest?: string }>();
  const [selectedVersion, setSelectedVersion] = useState('latest');
  const saved = selected ? library.presets[selected] ?? {} : library.defaults;
  const choices = draft ?? saved;
  const results = searchResults ?? library.catalog;
  const catalog = new Map([...library.catalog, ...results].map(item => [item.id, item]));
  const available = results.filter(item => (!favoritesOnly || library.favorites.includes(item.id)) && !(item.id in choices));
  const protectedIds = new Set(library.required.map(id => id.toLowerCase()));

  function setChoices(value?: Policy) {
    const same = value && Object.keys(value).length === Object.keys(saved).length
      && Object.entries(value).every(([id, version]) => saved[id] === version);
    setDraft(same ? undefined : value);
    onDirty(!!value && !same);
  }

  function edit(action: string, fields: object = {}) {
    void run(t('Salvataggio preset…'), async () => {
      const next = await request<PresetLibrary>('editPreset', { revision: library.revision, action, name: selected || null, ...fields });
      await onChange(next);
      setSelected(next.selected ?? '');
      if (action === 'save') setChoices(undefined);
      setNameEdit(undefined);
      setDeleting(false);
    });
  }

  function openVersion(item: CatalogPackage) {
    void run('Lettura versioni…', async () => {
      const options = await request<{ versions: string[]; latest?: string }>('projectPackageVersions', item.id);
      const current = choices[item.id];
      const versions = options.versions.includes(current) || !current || current === 'latest'
        ? options.versions : [current, ...options.versions];
      setSelectedVersion(current ?? (options.latest ? 'latest' : versions[0] ?? ''));
      setVersionPicker({ item, versions, latest: options.latest });
    });
  }

  return <div className="settings">
    <div className="actions project-tools">
      <label>Preset <select aria-label="Preset" value={selected} disabled={busy || !!draft} onChange={event => {
        setSelected(event.target.value); setDeleting(false); setNameEdit(undefined); setVersionPicker(undefined);
      }}>
        <option value="">{t("Predefinito")}</option>
        {Object.keys(library.presets).sort((a, b) => a.localeCompare(b)).map(name => <option key={name}>{name}</option>)}
      </select></label>
      <button disabled={busy || !!draft} onClick={() => { setNameEdit('create'); setNewName(''); setDeleting(false); }}>{t("Nuovo preset")}</button>
      {selected && <button disabled={busy || !!draft} onClick={() => { setNameEdit('rename'); setNewName(selected); setDeleting(false); }}>{t("Rinomina")}</button>}
      {selected && <button disabled={busy || !!draft} onClick={() => { setDeleting(true); setNameEdit(undefined); }}>{t("Elimina")}</button>}
    </div>
    {nameEdit && <form className="actions project-tools" onSubmit={event => { event.preventDefault(); edit(nameEdit, { newName }); }}>
      <input aria-label={t("Nome preset")} maxLength={80} value={newName} onChange={event => setNewName(event.target.value)} />
      <button disabled={busy || !newName.trim() || nameEdit === 'rename' && newName.trim() === selected}>{t("Conferma nome")}</button>
      <button type="button" onClick={() => setNameEdit(undefined)}>{t("Annulla")}</button>
    </form>}
    {deleting && <section className="extras">
      <p>{t('Eliminare «{name}»?', { name: selected })}</p>
      <div className="actions"><button onClick={() => setDeleting(false)}>{t("Annulla")}</button>
        <button disabled={busy} onClick={() => edit('delete')}>{t("Elimina preset")}</button></div>
    </section>}
    <div className="actions project-tools">
      <button disabled={busy || !!draft} onClick={() => void run(t("Importazione preset…"), async () => {
        const next = await request<PresetLibrary | false>('importPreset');
        if (next) { await onChange(next); setSelected(next.selected ?? ''); setVersionPicker(undefined); }
      })}>{t("Importa preset")}</button>
      <button disabled={busy || !Object.keys(choices).length} onClick={() => void run(t("Esportazione preset…"), async () => {
        await request('exportPreset', { name: selected || t("Predefinito"), packages: choices });
      })}>{t("Esporta preset")}</button>
      {draft && <>
        <button className="primary" disabled={busy || !Object.keys(choices).length} onClick={() => edit('save', { packages: choices })}>{t("Salva preset")}</button>
        <button disabled={busy} onClick={() => setChoices(undefined)}>{t("Scarta modifiche")}</button>
      </>}
    </div>
    {versionPicker && <section className="extras">
      <label className="package">{versionPicker.item.name}
        <select aria-label={t("Versione preset")} value={selectedVersion} onChange={event => setSelectedVersion(event.target.value)}>
          {(versionPicker.latest || selectedVersion === 'latest') &&
            <option value="latest">{t("Ultima versione")}{versionPicker.latest && ` (${versionPicker.latest})`}</option>}
          {!versionPicker.versions.length && !versionPicker.latest && selectedVersion !== 'latest' && <option value="">{t("Nessuna versione disponibile")}</option>}
          {versionPicker.versions.map(version => <option key={version}>{version}</option>)}
        </select>
      </label>
      <div className="actions"><button onClick={() => setVersionPicker(undefined)}>{t("Annulla")}</button>
        <button disabled={busy || !selectedVersion} onClick={() => {
          setChoices({ ...choices, [versionPicker.item.id]: selectedVersion }); setVersionPicker(undefined);
        }}>{t("Usa versione")}</button></div>
    </section>}
    <h2>{t("Pacchetti inclusi")}</h2>
    {Object.entries(choices).map(([id, version]) => {
      const item = catalog.get(id) ?? { id, name: id };
      return <div className="package" key={id} data-package-id={id}><span>{item.name} · {version === 'latest' ? t('Ultima versione') + (item.latest ? ' (' + item.latest + ')' : '') : version}</span>
        <div className="actions">
          <button disabled={busy} onClick={() => openVersion(item)}>{t("Versione")}</button>
          {protectedIds.has(id.toLowerCase()) ? <span>{t("Richiesto")}</span> : <button disabled={busy} onClick={() => {
            const next = { ...choices }; delete next[id]; setChoices(next);
          }}>{t("Escludi")}</button>}
        </div>
      </div>;
    })}
    <h2>{t("Catalogo")}</h2>
    <form className="actions project-tools" onSubmit={event => {
      event.preventDefault();
      void run(t("Ricerca pacchetti…"), async () => setSearchResults(await request<CatalogPackage[]>('searchProjectPackages', query)));
    }}>
      <input type="search" aria-label={t("Cerca pacchetti per il preset")} placeholder={t("Cerca pacchetti")} value={query} onChange={event => {
        setQuery(event.target.value); if (!event.target.value.trim()) setSearchResults(undefined);
      }} />
      <button disabled={busy || !query.trim()}>{t("Cerca")}</button>
      <label><input type="checkbox" checked={favoritesOnly} onChange={event => setFavoritesOnly(event.target.checked)} /> {t("Preferiti")}</label>
    </form>
    {available.map(item => <div className="package" key={item.id} data-package-id={item.id}><span>{item.name}</span><div className="actions">
      <button disabled={busy} aria-pressed={library.favorites.includes(item.id)} aria-label={t('Preferito: {name}', { name: item.name })}
        onClick={() => edit('favorite', { package: item.id })}>{library.favorites.includes(item.id) ? '★' : '☆'}</button>
      <button disabled={busy} onClick={() => openVersion(item)}>{t("Includi")}</button>
    </div></div>)}
  </div>;
}
import { useText } from './locale';
