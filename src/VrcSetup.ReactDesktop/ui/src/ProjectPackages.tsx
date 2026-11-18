import { useState } from 'react';
import { useText } from './locale';

type PackageChoice = { id: string; name: string };
type VersionOptions = { versions: string[]; latest: string | null };
export type ProjectPackageList = {
  path: string | null;
  name: string | null;
  targets: { name: string; path: string; fingerprint: string }[];
  canEdit: boolean;
  canRestore: boolean;
  editorOpen: boolean;
  packages: (PackageChoice & { installed?: string; locked?: string; installedCount: number; required: boolean })[];
};
type PackageReview = {
  id: string;
  restore: boolean;
  riskyDowngrade: boolean;
  projects: {
    name: string;
    path: string;
    packages: (PackageChoice & { before?: string; version?: string; kind: string })[];
  }[];
};
type BatchResult = { name: string; path: string; status: string; message?: string };
const batchStatuses: Record<string, string> = {
  Completed: 'Completato', CompletedWithWarnings: 'Completato con avviso',
  Failed: 'Non riuscito', Unchanged: 'Nessuna modifica.', Cancelled: 'Annullato', Skipped: 'Non elaborato'
};
type PendingChoice = PackageChoice & { version: string | null; targetPaths: string[] };
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
  const [batchResults, setBatchResults] = useState<BatchResult[]>();
  const batch = project.targets.length > 1;

  function stage(item: PackageChoice, selectedVersion: string | null) {
    setDraft(current => [...current.filter(x => x.id !== item.id), {
      ...item, version: selectedVersion,
      targetPaths: current.find(x => x.id === item.id)?.targetPaths ?? project.targets.map(target => target.path)
    }]);
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
        targets: Object.fromEntries(project.targets.map(target => [target.path, target.fingerprint])),
        selected: Object.fromEntries(draft.filter(x => x.version !== null).map(x => [x.id, x.version])),
        removed: draft.filter(x => x.version === null).map(x => x.id),
        packageTargets: Object.fromEntries(draft.map(item => [item.id, item.targetPaths]))
      };
      setAcceptRisk(false);
      setReview(await request<PackageReview>(method, data));
    });
  }

  if (review) return <section className="extras" aria-label={t("Riepilogo pacchetti")}>
    <h2>{review.restore ? t("Versioni da reinstallare") : t("Modifiche ai pacchetti")}</h2>
    {review.projects.map(target => <div key={target.path}>
      {batch && <h3 title={target.path}>{target.name}</h3>}
      {target.packages.length ? <ul>{target.packages.map(item => <li key={item.id}>
        {item.name} · {review.restore ? item.version : item.kind === 'Remove' ? t("Da rimuovere")
          : item.before ? `${item.before} → ${item.version}` : t('Da installare · {version}', { version: item.version ?? '' })}
      </li>)}</ul> : <p>{t("Nessuna modifica.")}</p>}
    </div>)}
    {review.riskyDowngrade && <label className="package">
      <span>{t("Voglio installare le versioni precedenti indicate.")}</span>
      <input type="checkbox" checked={acceptRisk} onChange={event => setAcceptRisk(event.target.checked)} />
    </label>}
    <div className="actions">
      <button disabled={busy} onClick={() => setReview(undefined)}>{t("Indietro")}</button>
      <button className="primary" disabled={busy || !review.projects.some(target => target.packages.length) || review.riskyDowngrade && !acceptRisk}
        onClick={() => void run(t("Applicazione modifiche…"), async () => {
          setReview(undefined);
          const result = await request<{ warning?: string; results?: BatchResult[] }>('applyProjectPackages', { id: review.id, acceptRisk });
          setBatchResults(result.results);
          setDraft([]);
          setResults(undefined);
          const refreshed = batch
            ? await request<ProjectPackageList>('batchProjectPackages', project.targets.map(target => target.path))
            : await request<ProjectPackageList>('projectPackages', project.path);
          onApplied(refreshed, batch ? '' : result.warning ?? (review.restore ? t("Pacchetti ripristinati.") : t("Pacchetti aggiornati.")));
        })}>{review.restore ? t("Conferma ripristino") : t("Applica modifiche")}</button>
    </div>
  </section>;

  return <>
    <div className="package"><h2>{t("Pacchetti VPM")}</h2>
      {project.canRestore && <button disabled={busy || !!draft.length || project.editorOpen}
        aria-describedby={project.editorOpen ? 'restore-editor-open' : undefined}
        onClick={() => reviewChanges(true)}>{t("Ripristina pacchetti")}</button>}
    </div>
    {project.canRestore && project.editorOpen && <div className="package">
      <span id="restore-editor-open">{t("Chiudi Unity per ripristinare i pacchetti.")}</span>
      <button disabled={busy} onClick={() => void run(t("Lettura pacchetti…"), async () => {
        onApplied(await request<ProjectPackageList>('projectPackages', project.path), '');
      })}>{t("Aggiorna")}</button>
    </div>}
    {batch && <ul className="batch-targets" aria-label={t("Progetti selezionati")}>{project.targets.map(target =>
      <li key={target.path} title={target.path}>{target.name}</li>)}</ul>}
    {batchResults && <section className="extras" aria-live="polite">
      <h2>{t("Esito delle modifiche")}</h2>
      {batchResults.map(result => <div key={result.path} className="package">
        <span title={result.path}>{result.name}</span>
        <span>{t(batchStatuses[result.status] ?? 'Da verificare')}
          {result.message ? <small>{result.message}</small> : result.status === 'Failed' && <small>{t("Apri il log in Attività.")}</small>}
        </span>
      </div>)}
    </section>}
    {project.canEdit && <form className="actions project-tools" onSubmit={event => {
      event.preventDefault();
      void run(t("Ricerca pacchetti…"), async () => setResults(await request<PackageChoice[]>('searchProjectPackages', query)));
    }}>
      <input type="search" aria-label={t("Cerca nel catalogo VPM")} placeholder={t("Cerca nel catalogo VPM")} value={query} onChange={event => setQuery(event.target.value)} />
      <button disabled={busy || !query.trim()}>{t("Cerca")}</button>
    </form>}
    {results && <section className="extras" aria-label={t("Risultati catalogo")}>
      <div className="package"><h2>{t("Risultati")}</h2><button onClick={() => setResults(undefined)}>{t("Chiudi risultati")}</button></div>
      {results.length ? results.map(item => <div className="package" key={item.id} data-package-id={item.id}>
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
      {draft.map(item => <div key={item.id}>
        <div className="package"><span>{item.name} · {item.version === null ? t("Da rimuovere") : item.version === 'latest' ? t("Ultima versione") : item.version}</span>
          <button disabled={busy} onClick={() => setDraft(current => current.filter(x => x.id !== item.id))}>{t("Annulla scelta")}</button>
        </div>
        {batch && <details className="batch-package-targets">
          <summary>{t("Progetti · {count} / {total}", { count: item.targetPaths.length, total: project.targets.length })}</summary>
          <div className="batch-targets">{project.targets.map(target => <label key={target.path} title={target.path}>
            <input type="checkbox" checked={item.targetPaths.includes(target.path)} disabled={busy}
              onChange={event => {
                const enabled = event.target.checked;
                setDraft(current => current.map(entry => entry.id !== item.id ? entry : {
                  ...entry, targetPaths: enabled ? [...entry.targetPaths, target.path] : entry.targetPaths.filter(path => path !== target.path)
                }));
              }} /> {target.name}
          </label>)}</div>
        </details>}
      </div>)}
      <button className="primary" disabled={busy || draft.some(item => !item.targetPaths.length)} onClick={() => reviewChanges(false)}>{t("Rivedi modifiche")}</button>
    </section>}
    {project.packages.length ? <table className="project-table">
      <thead><tr><th>{t("Pacchetto")}</th><th>{batch ? t("Installato in") : t("Installata")}</th>{!batch && <th>{t("Registrata")}</th>}{project.canEdit && <th scope="col" aria-label={t("Azioni")} />}</tr></thead>
      <tbody>{project.packages.map(item => <tr key={item.id} data-package-id={item.id}><th scope="row">{item.name}</th>
        <td>{batch ? `${item.installedCount} / ${project.targets.length}` : item.installed ?? t("Mancante")}</td>{!batch && <td>{item.locked ?? '—'}</td>}
        {project.canEdit && <td><div className="actions project-actions">
          <button disabled={busy} onClick={() => chooseVersion(item)}>{t("Versione")}</button>
          {item.required ? <span>{t("Richiesto")}</span> : <button disabled={busy} onClick={() => stage(item, null)}>{t("Rimuovi")}</button>}
        </div></td>}
      </tr>)}</tbody>
    </table> : <p>{t("Nessun pacchetto VPM.")}</p>}
  </>;
}
