import { useEffect, useState } from 'react';
import { createRoot } from 'react-dom/client';
import './style.css';

type Packages = Record<string, string>;
type Source = { id: string; file: string; name: string; target: string; suggestions: { id: string; name: string; version: string }[] };
type Draft = Source & { preset: string | null; packages: Packages };
type Job = { id: string; name: string; state: string; step: string; progress: number; active: boolean };
type State = { root: string; packages: Packages; presets: Record<string, Packages>; jobs: Job[]; initial: Source[] };
type Reply = { id: string; result?: unknown; error?: string };
type Host = { postMessage(message: unknown): void; postMessageWithAdditionalObjects(message: unknown, files: File[]): void; addEventListener(type: 'message', callback: (event: MessageEvent<Reply>) => void): void };
const host = (window as Window & { chrome?: { webview?: Host } }).chrome?.webview;
const requests = new Map<string, (reply: Reply) => void>();
host?.addEventListener('message', ({ data }) => requests.get(data.id)?.(data));

function request<T>(method: string, data: unknown = null, files?: File[]): Promise<T> {
  if (!host) return Promise.reject(new Error('Apri la candidata desktop per continuare.'));
  const id = crypto.randomUUID();
  return new Promise((resolve, reject) => {
    const timer = window.setTimeout(() => {
      requests.delete(id);
      reject(new Error('L’operazione sta impiegando più tempo del previsto.'));
    }, 15 * 60 * 1000);
    requests.set(id, reply => {
      clearTimeout(timer);
      requests.delete(id);
      if (reply.error) reject(new Error(reply.error));
      else resolve(reply.result as T);
    });
    try {
      if (files) host.postMessageWithAdditionalObjects({ id, method, data }, files);
      else host.postMessage({ id, method, data });
    } catch (error) {
      clearTimeout(timer);
      requests.delete(id);
      reject(error);
    }
  });
}

function ImportPage() {
  const [state, setState] = useState<State>();
  const [drafts, setDrafts] = useState<Draft[]>([]);
  const [activeId, setActiveId] = useState('');
  const [panel, setPanel] = useState<'import' | 'preset' | 'packages'>('import');
  const [busy, setBusy] = useState('');
  const [error, setError] = useState('');
  const [dragging, setDragging] = useState(false);
  const [review, setReview] = useState<{ id: string; destinations: string[] }>();
  const [log, setLog] = useState<{ id: string; text: string }>();
  const active = drafts.find(draft => draft.id === activeId);
  useEffect(() => {
    void request<State>('state').then(value => {
      setState(value);
      setDrafts(value.initial.map(source => ({ ...source, preset: null, packages: { ...value.packages } })));
      setActiveId(value.initial[0]?.id ?? '');
    }, error => setError(error.message));
  }, []);
  useEffect(() => {
    if (busy || !state?.jobs.some(job => job.active)) return;
    const timer = setInterval(async () => {
      try {
        setState(await request<State>('state'));
        if (log) {
          const text = await request<string>('log', log.id);
          setLog(current => current?.id === log.id ? { id: log.id, text } : current);
        }
      } catch { /* A user action can take precedence over a polling tick. */ }
    }, 1200);
    return () => clearInterval(timer);
  }, [busy, state?.jobs.some(job => job.active), log?.id]);
  useEffect(() => {
    const escape = (event: KeyboardEvent) => {
      if (event.key !== 'Escape' || busy) return;
      if (review) setReview(undefined); else setPanel('import');
    };
    window.addEventListener('keydown', escape);
    return () => window.removeEventListener('keydown', escape);
  }, [busy, review]);

  async function run(label: string, action: () => Promise<void>) {
    setBusy(label); setError('');
    try { await action(); } catch (error) { setError(error instanceof Error ? error.message : String(error)); }
    finally { setBusy(''); }
  }

  function stage(files?: File[]) {
    if (busy || review) return;
    void run('Verifica pacchetti…', async () => {
      const sources = await request<Source[]>(files ? 'drop' : 'pick', null, files);
      if (!sources.length) return;
      setDrafts(current => [...current, ...sources.map(source => ({ ...source, preset: null, packages: { ...state?.packages } }))]);
      setActiveId(sources[0].id); setPanel('import');
    });
  }

  function edit(change: Partial<Draft>) {
    if (busy) return;
    setReview(undefined);
    setDrafts(current => current.map(draft => draft.id === activeId ? { ...draft, ...change } : draft));
  }

  return <main data-ready={!!state} className={dragging ? 'dragging' : ''} onDragOver={event => { event.preventDefault(); setDragging(true); }}
    onDragLeave={event => { if (!event.currentTarget.contains(event.relatedTarget as Node | null)) setDragging(false); }}
    onDrop={event => { event.preventDefault(); setDragging(false); stage([...event.dataTransfer.files]); }}>
    <header><div><span className="brand">VRC Project Setup</span><h1>{panel === 'packages' ? 'Pacchetti' : panel === 'preset' ? 'Scegli un preset' : 'Importa'}</h1></div>
      {panel === 'import' ? <button disabled={!!busy || !state || !!review} onClick={() => stage()}>+ Aggiungi file</button> :
        <button onClick={() => setPanel('import')}>← Torna all’importazione</button>}</header>
    <section className="view" key={panel}>
      {panel === 'import' && <>
        {!active ? <button className="drop" disabled={!!busy || !state} onClick={() => stage()}><span aria-hidden="true">＋</span>Trascina qui i file .unitypackage</button> :
          <>
            <div className="tabs" aria-label="File da importare">
              {drafts.map(draft => <button key={draft.id} className={draft.id === activeId ? 'selected' : ''} aria-pressed={draft.id === activeId}
                onClick={() => setActiveId(draft.id)}>{draft.file}</button>)}
            </div>
            <div className="fields">
              <input aria-label="Nome progetto" value={active.name} onChange={event => edit({ name: event.target.value })} />
              <div className="platforms" aria-label="Piattaforma">
                {([['StandaloneWindows64', 'PC'], ['Android', 'Quest'], ['iOS', 'iOS']] as const).map(([target, label]) =>
                  <button key={target} aria-pressed={active.target === target} onClick={() => edit({ target })}>{label}</button>)}
              </div>
            </div>
            <p className="path">{state?.root ? state.root + '\\' + active.name : 'Configura Unity per usare la cartella di prova'}</p>
            <button className="row" onClick={() => setPanel('preset')}>Preset <span>{active.preset ?? 'Predefinito'} ›</span></button>
            <button className="row" onClick={() => setPanel('packages')}>Pacchetti <span>{Object.keys(active.packages).length} selezionati ›</span></button>
            {active.suggestions.some(item => !active.packages[item.id]) && <div className="extras">
              <strong>＋ Pacchetti facoltativi</strong>
              {active.suggestions.filter(item => !active.packages[item.id]).map(item => <div className="package" key={item.id}>{item.name}
                <button onClick={() => edit({ packages: { ...active.packages, [item.id]: item.version } })}>Aggiungi</button></div>)}</div>}
          </>}
        {state?.jobs.length ? <section className="jobs"><h2>Importazioni</h2>{state.jobs.map(job => <article key={job.id}>
          <div className="package"><strong>{job.name}</strong><span>{job.state}</span></div>
          <progress max="100" value={job.progress} aria-label={job.step} />
          <div className="actions">{job.active && <button onClick={() => void run('Pausa…', async () => setState(await request<State>('pause', job.id)))}>Pausa</button>}
            <button onClick={() => void run('Apertura log…', async () => setLog(log?.id === job.id ? undefined : { id: job.id, text: await request<string>('log', job.id) }))}>{log?.id === job.id ? 'Chiudi log' : 'Apri log'}</button></div>
          {log?.id === job.id && <pre>{log.text || 'Nessun log disponibile.'}</pre>}</article>)}</section> : null}
      </>}
      {active && panel === 'preset' && <div className="presets">
        {[[null, state?.packages ?? {}], ...Object.entries(state?.presets ?? {})].map(([name, packages]) =>
          <button key={(name as string | null) ?? ''} className="row" aria-pressed={active.preset === name}
            onClick={() => { edit({ preset: name as string | null, packages: { ...packages as Packages } }); setPanel('import'); }}>
            {(name as string | null) ?? 'Predefinito'}<span>{active.preset === name ? '✓' : '›'}</span>
          </button>)}
      </div>}
      {active && panel === 'packages' && <div className="editor">
        {Object.entries(active.packages).map(([id, version]) => <div className="package" key={id}><span>{id}</span>
          <input aria-label={'Versione ' + id} value={version} onChange={event => edit({ packages: { ...active.packages, [id]: event.target.value } })}/>
          <button onClick={() => { const packages = { ...active.packages }; delete packages[id]; edit({ packages }); }}>Escludi</button></div>)}
        {Object.entries(active.preset ? state?.presets[active.preset] ?? {} : state?.packages ?? {}).filter(([id]) => !(id in active.packages))
          .map(([id, version]) => <div className="package" key={id}><span>{id}</span><button onClick={() => edit({ packages: { ...active.packages, [id]: version } })}>Includi</button></div>)}
      </div>}
      {review && <section className="extras"><h2>Destinazioni</h2>{review.destinations.map(path => <p key={path}>{path}</p>)}
        <div className="actions"><button onClick={() => setReview(undefined)}>Indietro</button>
          <button className="primary" disabled={!!busy} onClick={() => void run('Avvio importazioni…', async () => {
            setState(await request<State>('start', review.id)); setReview(undefined); setDrafts([]); setActiveId('');
          })}>Avvia {review.destinations.length} importazioni</button></div></section>}
    </section>
    <footer>
      <div role="status">{error || busy || (drafts.length ? drafts.length + ' file' : '')}</div>
      <div className="actions">
        {active && panel === 'import' && !review && <button disabled={!!busy} onClick={() => void run('Rimozione…', async () => {
          await request('remove', active.id); const left = drafts.filter(draft => draft.id !== active.id); setDrafts(left); setActiveId(left[0]?.id ?? '');
        })}>Rimuovi file</button>}
        {panel === 'import' && !state?.root && <button disabled={!!busy || !state} onClick={() => void run('Ricerca di Unity…', async () => setState(await request<State>('workspace')))}>Configura Unity</button>}
        {active && panel === 'import' && !review && state?.root &&
          <button className="primary" disabled={!!busy} onClick={() => void run('Preparazione…', async () => setReview(await request('review', drafts)))}>Rivedi e importa</button>}
      </div>
    </footer>
  </main>;
}

createRoot(document.getElementById('root')!).render(<ImportPage />);
