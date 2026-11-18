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

  if (review) return <section className="extras" aria-label="Riepilogo pacchetti">
    <h2>{review.restore ? 'Versioni da reinstallare' : 'Modifiche ai pacchetti'}</h2>
    {review.packages.length ? <ul>{review.packages.map(item => <li key={item.id}>
      {item.name} · {review.restore ? item.version : item.kind === 'Remove' ? 'Da rimuovere'
        : item.before ? `${item.before} → ${item.version}` : `Da installare · ${item.version}`}
    </li>)}</ul> : <p>I pacchetti sono già aggiornati.</p>}
    {review.riskyDowngrade && <label className="package">
      <span>Voglio installare le versioni precedenti indicate.</span>
      <input type="checkbox" checked={acceptRisk} onChange={event => setAcceptRisk(event.target.checked)} />
    </label>}
    <div className="actions">
      <button disabled={busy} onClick={() => setReview(undefined)}>Indietro</button>
      <button className="primary" disabled={busy || !review.packages.length || review.riskyDowngrade && !acceptRisk}
        onClick={() => void run('Applicazione modifiche…', async () => {
          setReview(undefined);
          const result = await request<{ warning?: string }>('applyProjectPackages', { id: review.id, acceptRisk });
          setDraft([]);
          setResults(undefined);
          onApplied(await request<ProjectPackageList>('projectPackages', project.path),
            result.warning ?? (review.restore ? 'Pacchetti ripristinati.' : 'Pacchetti aggiornati.'));
        })}>{review.restore ? 'Conferma ripristino' : 'Applica modifiche'}</button>
    </div>
  </section>;

  return <>
    <div className="package"><h2>Pacchetti VPM</h2>
      {project.canRestore && <button disabled={busy || !!draft.length} onClick={() => reviewChanges(true)}>Ripristina pacchetti</button>}
    </div>
    {project.canEdit && <form className="actions project-tools" onSubmit={event => {
      event.preventDefault();
      void run('Ricerca pacchetti…', async () => setResults(await request<PackageChoice[]>('searchProjectPackages', query)));
    }}>
      <input type="search" aria-label="Cerca nel catalogo VPM" placeholder="Cerca nel catalogo VPM" value={query} onChange={event => setQuery(event.target.value)} />
      <button disabled={busy || !query.trim()}>Cerca</button>
    </form>}
    {results && <section className="extras" aria-label="Risultati catalogo">
      <div className="package"><h2>Risultati</h2><button onClick={() => setResults(undefined)}>Chiudi risultati</button></div>
      {results.length ? results.map(item => <div className="package" key={item.id}>
        <span>{item.name}</span><button disabled={busy} onClick={() => chooseVersion(item)}>Scegli versione</button>
      </div>) : <p>Nessun pacchetto trovato.</p>}
    </section>}
    {choice && <section className="extras">
      <label className="package">{choice.package.name}
        <select aria-label="Versione da installare" value={version} onChange={event => setVersion(event.target.value)}>
          {!choice.options.versions.length && <option value="">Nessuna versione disponibile</option>}
          {choice.options.latest && <option value="latest">Ultima versione ({choice.options.latest})</option>}
          {choice.options.versions.map(value => <option key={value}>{value}</option>)}
        </select>
      </label>
      <div className="actions"><button disabled={busy} onClick={() => setChoice(undefined)}>Annulla</button>
        <button disabled={busy || !version} onClick={() => stage(choice.package, version)}>Seleziona versione</button></div>
    </section>}
    {draft.length > 0 && <section className="extras">
      <h2>Modifiche selezionate</h2>
      {draft.map(item => <div className="package" key={item.id}><span>{item.name} · {item.version === null ? 'Da rimuovere' : item.version === 'latest' ? 'Ultima versione' : item.version}</span>
        <button disabled={busy} onClick={() => setDraft(current => current.filter(x => x.id !== item.id))}>Annulla scelta</button>
      </div>)}
      <button className="primary" disabled={busy} onClick={() => reviewChanges(false)}>Rivedi modifiche</button>
    </section>}
    {project.packages.length ? <table className="project-table">
      <thead><tr><th>Pacchetto</th><th>Installata</th><th>Registrata</th>{project.canEdit && <th scope="col" aria-label="Azioni" />}</tr></thead>
      <tbody>{project.packages.map(item => <tr key={item.id}><th scope="row">{item.name}</th>
        <td>{item.installed ?? 'Mancante'}</td><td>{item.locked ?? '—'}</td>
        {project.canEdit && <td><div className="actions project-actions">
          <button disabled={busy} onClick={() => chooseVersion(item)}>Versione</button>
          {item.required ? <span>Richiesto</span> : <button disabled={busy} onClick={() => stage(item, null)}>Rimuovi</button>}
        </div></td>}
      </tr>)}</tbody>
    </table> : <p>Nessun pacchetto VPM.</p>}
  </>;
}
