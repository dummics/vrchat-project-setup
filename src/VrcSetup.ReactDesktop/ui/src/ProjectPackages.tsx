import { useState } from 'react';

type PackageChoice = { id: string; name: string };
type VersionOptions = { versions: string[]; latest: string | null };
export type ProjectPackageList = {
  path: string;
  name: string;
  fingerprint: string;
  canEdit: boolean;
  canRestore: boolean;
  packages: (PackageChoice & { installed?: string; locked?: string; required: boolean })[];
};
type PackageReview = {
  id: string;
  restore: boolean;
  riskyDowngrade: boolean;
  packages: (PackageChoice & { before?: string; version?: string; kind: string })[];
};
type PendingChoice = PackageChoice & { version: string | null };
type Props = {
  project: ProjectPackageList;
  busy: boolean;
  request<T>(method: string, data: unknown): Promise<T>;
  run(label: string, action: () => Promise<void>): Promise<void>;
  onApplied(project: ProjectPackageList, notice: string): void;
};

export function ProjectPackages({ project, busy, request, run, onApplied }: Props) {
  const t = useText();
  const [draft, setDraft] = useState<PendingChoice[]>([]);
  const [query, setQuery] = useState('');
  const [results, setResults] = useState<PackageChoice[]>();
  const [choice, setChoice] = useState<{ package: PackageChoice; options: VersionOptions }>();
  const [version, setVersion] = useState('');
  const [review, setReview] = useState<PackageReview>();
  const [acceptRisk, setAcceptRisk] = useState(false);

  function stage(item: PackageChoice, selectedVersion: string | null) {
    setDraft(current => [...current.filter(x => x.id !== item.id), { ...item, version: selectedVersion }]);
    setChoice(undefined);
  }

  function chooseVersion(item: PackageChoice) {
    void run('Lettura versioni…', async () => {
      const options = await request<VersionOptions>('projectPackageVersions', item.id);
      const selected = draft.find(x => x.id === item.id)?.version;
      setVersion(selected ?? (options.latest ? 'latest' : options.versions[0] ?? ''));
      setChoice({ package: item, options });
    });
  }

  function reviewChanges(restore: boolean) {
    void run('Preparazione modifiche…', async () => {
      const method = restore ? 'reviewPackageRestore' : 'reviewProjectPackages';
      const data = restore ? project.path : {
        path: project.path,
        fingerprint: project.fingerprint,
        selected: Object.fromEntries(draft.filter(x => x.version !== null).map(x => [x.id, x.version])),
        removed: draft.filter(x => x.version === null).map(x => x.id)
      };
      setAcceptRisk(false);
      setReview(await request<PackageReview>(method, data));
    });
  }

  if (review) return <section className="extras" aria-label={t("Riepilogo pacchetti")}>
    <h2>{review.restore ? t("Versioni da reinstallare") : t("Modifiche ai pacchetti")}</h2>
    {review.packages.length ? <ul>{review.packages.map(item => <li key={item.id}>
      {item.name} · {review.restore ? item.version : item.kind === 'Remove' ? t("Da rimuovere")
        : item.before ? `${item.before} → ${item.version}` : t('Da installare · {version}', { version: item.version ?? '' })}
    </li>)}</ul> : <p>{t("I pacchetti sono già aggiornati.")}</p>}
    {review.riskyDowngrade && <label className="package">
      <span>{t("Voglio installare le versioni precedenti indicate.")}</span>
      <input type="checkbox" checked={acceptRisk} onChange={event => setAcceptRisk(event.target.checked)} />
    </label>}
    <div className="actions">
      <button disabled={busy} onClick={() => setReview(undefined)}>{t("Indietro")}</button>
      <button className="primary" disabled={busy || !review.packages.length || review.riskyDowngrade && !acceptRisk}
        onClick={() => void run(t("Applicazione modifiche…"), async () => {
          setReview(undefined);
          const result = await request<{ warning?: string }>('applyProjectPackages', { id: review.id, acceptRisk });
          setDraft([]);
          setResults(undefined);
          onApplied(await request<ProjectPackageList>('projectPackages', project.path),
            result.warning ?? (review.restore ? t("Pacchetti ripristinati.") : t("Pacchetti aggiornati.")));
        })}>{review.restore ? t("Conferma ripristino") : t("Applica modifiche")}</button>
    </div>
  </section>;

  return <>
    <div className="package"><h2>{t("Pacchetti VPM")}</h2>
      {project.canRestore && <button disabled={busy || !!draft.length} onClick={() => reviewChanges(true)}>{t("Ripristina pacchetti")}</button>}
    </div>
    {project.canEdit && <form className="actions project-tools" onSubmit={event => {
      event.preventDefault();
      void run(t("Ricerca pacchetti…"), async () => setResults(await request<PackageChoice[]>('searchProjectPackages', query)));
    }}>
      <input type="search" aria-label={t("Cerca nel catalogo VPM")} placeholder={t("Cerca nel catalogo VPM")} value={query} onChange={event => setQuery(event.target.value)} />
      <button disabled={busy || !query.trim()}>{t("Cerca")}</button>
    </form>}
    {results && <section className="extras" aria-label={t("Risultati catalogo")}>
      <div className="package"><h2>{t("Risultati")}</h2><button onClick={() => setResults(undefined)}>{t("Chiudi risultati")}</button></div>
      {results.length ? results.map(item => <div className="package" key={item.id}>
        <span>{item.name}</span><button disabled={busy} onClick={() => chooseVersion(item)}>{t("Scegli versione")}</button>
      </div>) : <p>{t("Nessun pacchetto trovato.")}</p>}
    </section>}
    {choice && <section className="extras">
      <label className="package">{choice.package.name}
        <select aria-label={t("Versione da installare")} value={version} onChange={event => setVersion(event.target.value)}>
          {!choice.options.versions.length && <option value="">{t("Nessuna versione disponibile")}</option>}
          {choice.options.latest && <option value="latest">{t('Ultima versione')} ({choice.options.latest})</option>}
          {choice.options.versions.map(value => <option key={value}>{value}</option>)}
        </select>
      </label>
      <div className="actions"><button disabled={busy} onClick={() => setChoice(undefined)}>{t("Annulla")}</button>
        <button disabled={busy || !version} onClick={() => stage(choice.package, version)}>{t("Seleziona versione")}</button></div>
    </section>}
    {draft.length > 0 && <section className="extras">
      <h2>{t("Modifiche selezionate")}</h2>
      {draft.map(item => <div className="package" key={item.id}><span>{item.name} · {item.version === null ? t("Da rimuovere") : item.version === 'latest' ? t("Ultima versione") : item.version}</span>
        <button disabled={busy} onClick={() => setDraft(current => current.filter(x => x.id !== item.id))}>{t("Annulla scelta")}</button>
      </div>)}
      <button className="primary" disabled={busy} onClick={() => reviewChanges(false)}>{t("Rivedi modifiche")}</button>
    </section>}
    {project.packages.length ? <table className="project-table">
      <thead><tr><th>{t("Pacchetto")}</th><th>{t("Installata")}</th><th>{t("Registrata")}</th>{project.canEdit && <th scope="col" aria-label={t("Azioni")} />}</tr></thead>
      <tbody>{project.packages.map(item => <tr key={item.id}><th scope="row">{item.name}</th>
        <td>{item.installed ?? t("Mancante")}</td><td>{item.locked ?? '—'}</td>
        {project.canEdit && <td><div className="actions project-actions">
          <button disabled={busy} onClick={() => chooseVersion(item)}>{t("Versione")}</button>
          {item.required ? <span>{t("Richiesto")}</span> : <button disabled={busy} onClick={() => stage(item, null)}>{t("Rimuovi")}</button>}
        </div></td>}
      </tr>)}</tbody>
    </table> : <p>{t("Nessun pacchetto VPM.")}</p>}
  </>;
}
import { useText } from './locale';
