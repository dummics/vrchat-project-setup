import { useEffect, useState } from 'react';
import { createRoot } from 'react-dom/client';
import { ProjectPackages, type ProjectPackageList } from './ProjectPackages';
import { Presets, type PresetLibrary } from './Presets';
import { AppearanceForm, type Appearance, type Palettes, useAppearance } from './appearance';
import { Activity, type ActivityEntry } from './Activity';
import { Help } from './Help';
import './style.css';

type Packages = Record<string, string>;
type Source = { id: string; file: string; name: string; target: string; suggestions: { id: string; name: string; version: string }[] };
type Draft = Source & { preset: string | null; packages: Packages };
type Job = {
  id: string; name: string; projectPath: string; state: string; progress: number; active: boolean; recoverable: boolean;
  hasCompilationBlock: boolean; unityCompilationNeedsAttention: boolean; packageContentsNeedRestore: boolean;
  creatorCompanionPending: boolean; creatorCompanionUnknown: boolean; canRetryRegistration: boolean; packageResolutionFailed: boolean;
  historySaveFailed: boolean; canDismiss: boolean; recoveryNoticeDismissed: boolean; discardRequested: boolean; discardCompleted: boolean;
  canDiscard: boolean; discardRemovesFolder: boolean;
};
type Project = {
  name: string; path: string; unityVersion: string; lastUpdatedUtc: string; createdUtc: string;
  incomplete: boolean; open: boolean; importing: boolean; recoveryId?: string;
};
type Review = { id: string; continuation: boolean; projects: {
  name: string; path: string; target?: string; packages: { id: string; name: string; version?: string }[]; removals: string[];
}[] };
type ImportPreferences = {
  maxParallelImports: number; autoHideCompletedImports: boolean; checkUpdatesOnStartup: boolean;
  automaticUpdates: boolean; includeBetaUpdates: boolean; includeAlphaUpdates: boolean;
  canEditPaths: boolean; unityEditorPath: string; unityProjectsRoot: string; unityPackagesFolder?: string;
  appearance: Appearance; palettes: Palettes;
};
const workspaceOptions = [
  ['unityProjectsRoot', 'projects', 'Cartella progetti'],
  ['unityEditorPath', 'editor', 'Editor Unity'],
  ['unityPackagesFolder', 'extras', 'Importazioni aggiuntive']
] as const;
type UpdateView = { current: string; enabled: boolean; installed: boolean; available?: string; ready?: string };
const updateOptions = [
  ['checkUpdatesOnStartup', 'Controlla automaticamente gli aggiornamenti'],
  ['automaticUpdates', 'Prepara gli aggiornamenti per il prossimo avvio'],
  ['includeBetaUpdates', 'Includi le versioni beta'],
  ['includeAlphaUpdates', 'Includi le versioni alpha sperimentali']
] as const;
type State = { root: string; packages: Packages; presets: Record<string, Packages>; jobs: Job[]; initial: Source[]; waitingForMemory: boolean; appearance: Appearance; version: string };
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

function jobStatus(job: Job) {
  if (job.historySaveFailed) return 'Stato da salvare';
  if (job.discardCompleted) return 'Annullata';
  if (job.discardRequested) return 'Annullamento in corso';
  if (job.hasCompilationBlock || job.unityCompilationNeedsAttention) return 'Compilazione da verificare';
  if (job.packageContentsNeedRestore) return 'Pacchetti da ripristinare';
  if (job.packageResolutionFailed) return 'Pacchetti da controllare';
  if (job.creatorCompanionPending || job.creatorCompanionUnknown) return 'Completata con avviso';
  const labels: Record<string, string> = {
    Queued: 'In coda', Running: 'In corso', Pausing: 'Messa in pausa…', Cancelling: 'Arresto in corso…',
    Paused: 'In pausa', Interrupted: 'Interrotta', Cancelled: 'Fermata', Failed: 'Da riprendere',
    Completed: 'Completata', Recovered: 'Completata'
  };
  return labels[job.state] ?? 'Stato da verificare';
}

function ImportPage() {
  const [state, setState] = useState<State>();
  const [drafts, setDrafts] = useState<Draft[]>([]);
  const [activeId, setActiveId] = useState('');
  const [panel, setPanel] = useState<'import' | 'preset' | 'packages'>('import');
  const [screen, setScreen] = useState<'import' | 'projects' | 'activity' | 'project-packages' | 'settings' | 'presets'>('import');
  const [presetLibrary, setPresetLibrary] = useState<PresetLibrary>();
  const [presetDirty, setPresetDirty] = useState(false);
  const [projects, setProjects] = useState<Project[]>([]);
  const [activity, setActivity] = useState<ActivityEntry[]>([]);
  const [projectPackages, setProjectPackages] = useState<ProjectPackageList>();
  const [newProject, setNewProject] = useState<{ name: string; target: string; preset: string }>();
  const [newProjectReview, setNewProjectReview] = useState<{ id: string; destination: string }>();
  const [packageBack, setPackageBack] = useState<'import' | 'projects'>('projects');
  const [notice, setNotice] = useState('');
  const [preferences, setPreferences] = useState<ImportPreferences>();
  const [settingsDirty, setSettingsDirty] = useState(false);
  const [update, setUpdate] = useState<UpdateView>();
  const [filter, setFilter] = useState('');
  const [sort, setSort] = useState<'name' | 'lastUpdatedUtc' | 'createdUtc'>('lastUpdatedUtc');
  const [busy, setBusy] = useState('');
  const [error, setError] = useState('');
  const [dragging, setDragging] = useState(false);
  const [review, setReview] = useState<Review>();
  const [log, setLog] = useState<{ id: string; text: string }>();
  const active = drafts.find(draft => draft.id === activeId);
  useAppearance(screen === 'settings' ? preferences?.appearance ?? state?.appearance : state?.appearance);
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
    if (busy || review || screen !== 'import') return;
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

  function showProjects() {
    void run('Ricerca progetti…', async () => {
      setProjects(await request<Project[]>('projects'));
      setScreen('projects');
    });
  }

  function showActivity() {
    void run('Lettura attività…', async () => {
      setActivity(await request<ActivityEntry[]>('activity'));
      setLog(undefined);
      setScreen('activity');
    });
  }

  function showSettings() {
    void run('Lettura impostazioni…', async () => {
      setPreferences(await request<ImportPreferences>('importPreferences'));
      setUpdate(await request<UpdateView>('updateStatus'));
      setSettingsDirty(false);
      setNotice('');
      setScreen('settings');
    });
  }

  function showPresets() {
    void run('Lettura preset…', async () => {
      setPresetLibrary(await request<PresetLibrary>('presets'));
      setPresetDirty(false);
      setScreen('presets');
    });
  }

  function showProjectPackages(path: string) {
    void run('Lettura pacchetti…', async () => {
      setProjectPackages(await request<ProjectPackageList>('projectPackages', path));
      setPackageBack(screen === 'import' ? 'import' : 'projects');
      setNotice('');
      setScreen('project-packages');
    });
  }

  const visibleProjects = projects.filter(project => project.name.toLocaleLowerCase('it').includes(filter.toLocaleLowerCase('it')))
    .sort((a, b) => sort === 'name' ? a.name.localeCompare(b.name, 'it') : b[sort].localeCompare(a[sort]));
  const date = new Intl.DateTimeFormat('it-IT');

  return <main data-ready={!!state} className={dragging ? 'dragging' : ''} onDragOver={event => { event.preventDefault(); setDragging(true); }}
    onDragLeave={event => { if (!event.currentTarget.contains(event.relatedTarget as Node | null)) setDragging(false); }}
    onDrop={event => { event.preventDefault(); setDragging(false); stage([...event.dataTransfer.files]); }}>
    <header><div><span className="brand">VRC Project Setup</span><h1>{screen === 'projects' ? 'Progetti' : screen === 'activity' ? 'Attività' : screen === 'settings' ? 'Impostazioni' : screen === 'presets' ? 'Preset' :
      screen === 'project-packages' ? projectPackages?.name : panel === 'packages' ? 'Pacchetti' : panel === 'preset' ? 'Scegli un preset' : 'Importa'}</h1></div>
      {screen !== 'import' ? <button disabled={!!busy || screen === 'presets' && presetDirty} onClick={() => {
        if (screen === 'project-packages') { setNotice(''); setScreen(packageBack); }
        else { setNotice(''); setNewProject(undefined); setNewProjectReview(undefined); setScreen('import'); }
      }}>{screen === 'project-packages' && packageBack === 'projects' ? '← Progetti' : '← Importa'}</button> :
        panel === 'import' ? <div className="actions"><button disabled={!!busy || !state || !!review} onClick={showProjects}>Progetti</button>
          <button disabled={!!busy || !state || !!review} onClick={showActivity}>Attività</button>
          <button disabled={!!busy || !state || !!review} onClick={showPresets}>Preset</button>
          <button disabled={!!busy || !state || !!review} onClick={showSettings}>Impostazioni</button>
          <button disabled={!!busy || !state || !!review} onClick={() => stage()}>+ Aggiungi file</button></div> :
          <button onClick={() => setPanel('import')}>← Torna all’importazione</button>}</header>
    {screen === 'presets' && presetLibrary ? <section className="view" key="presets">
      <Presets library={presetLibrary} busy={!!busy} request={request} run={run} onDirty={setPresetDirty}
        onChange={async next => { setPresetLibrary(next); setState(await request<State>('state')); }} />
    </section> : screen === 'settings' && preferences ? <section className="view settings" key="settings">
      <h2>Importazioni</h2>
      <form onSubmit={event => {
        event.preventDefault();
        void run('Salvataggio…', async () => {
          setState(await request<State>('saveImportPreferences', preferences));
          setPreferences(await request<ImportPreferences>('importPreferences'));
          setUpdate(await request<UpdateView>('updateStatus'));
          setSettingsDirty(false);
          setNotice('Impostazioni salvate.');
        });
      }}>
        <fieldset disabled={!!busy}>
          {preferences.canEditPaths && <section className="workspace-paths">
            <h2>Unity</h2>
            {workspaceOptions.map(([key, picker, label]) => <div className="workspace-path" key={key}>
              <label htmlFor={key}>{label}</label>
              <input id={key} value={preferences[key] ?? ''} onChange={event => {
                setPreferences({ ...preferences, [key]: event.target.value }); setNotice(''); setSettingsDirty(true);
              }} />
              <button type="button" aria-label={`Sfoglia: ${label}`} onClick={() => void run('Selezione percorso…', async () => {
                const path = await request<string | false>('pickSettingsPath', picker);
                if (path) {
                  setPreferences({ ...preferences, [key]: path }); setNotice(''); setSettingsDirty(true);
                }
              })}>Sfoglia</button>
            </div>)}
          </section>}
          <AppearanceForm value={preferences.appearance} palettes={preferences.palettes} onChange={appearance => {
            setPreferences({ ...preferences, appearance });
            setNotice('');
            setSettingsDirty(true);
          }} />
          <label className="package">Nascondi importazioni completate
            <input id="hide-completed" type="checkbox" checked={preferences.autoHideCompletedImports} onChange={event => {
              setPreferences({ ...preferences, autoHideCompletedImports: event.target.checked }); setNotice(''); setSettingsDirty(true);
            }} />
          </label>
          <details><summary>Impostazioni avanzate</summary>
            <label className="package">Limite importazioni simultanee
              <select id="import-limit" value={preferences.maxParallelImports} onChange={event => {
                setPreferences({ ...preferences, maxParallelImports: Number(event.target.value) }); setNotice(''); setSettingsDirty(true);
              }}>
                <option value={0}>Automatico</option>
                {Array.from({ length: 8 }, (_, index) => <option value={index + 1} key={index}>{index + 1}</option>)}
              </select>
            </label>
          </details>
          <h2>Aggiornamenti</h2>
          {updateOptions.map(([key, label]) => <label className="package" key={key}>{label}
            <input type="checkbox" checked={preferences[key]} onChange={event => {
              setPreferences({ ...preferences, [key]: event.target.checked }); setNotice(''); setSettingsDirty(true);
            }} />
          </label>)}
          <button type="submit" className="primary">Salva</button>
        </fieldset>
      </form>
      {update?.enabled && <section className="extras" aria-label="Versione applicazione">
        <p>Versione {update.current}{update.ready ? ` · ${update.ready} pronta` : update.available ? ` · ${update.available} disponibile` : ''}</p>
        <div className="actions">
          <button disabled={!!busy || settingsDirty} onClick={() => void run('Ricerca aggiornamenti…', async () => {
            const status = await request<UpdateView>('checkUpdate');
            setUpdate(status);
            setNotice(status.available ? '' : 'L’app è aggiornata.');
          })}>Controlla aggiornamenti</button>
          {update.installed && update.available && !update.ready && <button className="primary" disabled={!!busy || settingsDirty}
            onClick={() => void run('Preparazione aggiornamento…', async () => setUpdate(await request<UpdateView>('prepareUpdate', update.available)))}>
            Prepara aggiornamento
          </button>}
          {update.installed && update.ready && <button className="primary" disabled={!!busy || settingsDirty}
            onClick={() => void run('Avvio aggiornamento…', async () => { await request('applyUpdate'); })}>Aggiorna e riavvia</button>}
        </div>
      </section>}
      <Help onReport={() => void run('Apertura assistenza…', async () => { await request('openSupport'); })} />
    </section> : screen === 'project-packages' && projectPackages ? <section className="view" key="project-packages">
      <ProjectPackages project={projectPackages} busy={!!busy} request={request} run={run}
        onApplied={(project, message) => { setProjectPackages(project); setNotice(message); }} />
    </section> : screen === 'activity' ? <section className="view activity" key="activity">
      <Activity entries={activity} busy={!!busy} date={date} request={request} run={run} refresh={showActivity}
        onExported={() => setNotice('Diagnostica esportata.')} />
    </section> : screen === 'projects' ? <section className="view" key="projects">
      <div className="actions project-tools"><input aria-label="Cerca progetti" placeholder="Cerca progetti" value={filter} onChange={event => setFilter(event.target.value)} />
        <button disabled={!!busy} onClick={showProjects}>Aggiorna</button>
        <button disabled={!!busy || !!newProject} onClick={() => void run('Apertura progetto…', async () => {
          const project = await request<ProjectPackageList | false>('pickExistingProject');
          if (project) { setProjectPackages(project); setPackageBack('projects'); setScreen('project-packages'); }
        })}>Apri progetto esistente</button>
        <button disabled={!!busy || !!newProject} onClick={() => setNewProject({ name: '', target: 'StandaloneWindows64', preset: '' })}>Nuovo progetto</button>
      </div>
      {newProject && <section className="extras settings">
        <h2>Nuovo progetto</h2>
        {newProjectReview ? <>
          <p className="path">{newProjectReview.destination}</p>
          <div className="actions">
            <button disabled={!!busy} onClick={() => setNewProjectReview(undefined)}>Indietro</button>
            <button className="primary" disabled={!!busy} onClick={() => void run('Creazione progetto…', async () => {
              setState(await request<State>('createProject', newProjectReview.id));
              setNewProject(undefined); setNewProjectReview(undefined); setPanel('import'); setScreen('import');
            })}>Crea progetto</button>
          </div>
        </> : <form onSubmit={event => {
          event.preventDefault();
          void run('Preparazione progetto…', async () => setNewProjectReview(await request('reviewNewProject', { ...newProject, preset: newProject.preset || null })));
        }}>
          <label className="package">Nome progetto <input aria-label="Nome nuovo progetto" value={newProject.name} onChange={event => setNewProject({ ...newProject, name: event.target.value })} /></label>
          <label className="package">Piattaforma
            <select value={newProject.target} onChange={event => setNewProject({ ...newProject, target: event.target.value })}>
              <option value="StandaloneWindows64">PC</option><option value="Android">Quest</option><option value="iOS">iOS</option>
            </select>
          </label>
          <label className="package">Preset
            <select value={newProject.preset} onChange={event => setNewProject({ ...newProject, preset: event.target.value })}>
              <option value="">Predefinito</option>
              {Object.keys(state?.presets ?? {}).map(name => <option key={name}>{name}</option>)}
            </select>
          </label>
          <div className="actions">
            <button type="button" disabled={!!busy} onClick={() => setNewProject(undefined)}>Annulla</button>
            <button className="primary" disabled={!!busy || !newProject.name.trim()}>Rivedi progetto</button>
          </div>
        </form>}
      </section>}
      {visibleProjects.length ? <table className="project-table">
        <thead><tr><th><button onClick={() => setSort('name')}>Nome</button></th>
          <th><button onClick={() => setSort('lastUpdatedUtc')}>Modifica</button></th>
          <th><button onClick={() => setSort('createdUtc')}>Creazione</button></th><th scope="col" aria-label="Azioni" /></tr></thead>
        <tbody>{visibleProjects.map(project => <tr key={project.path}>
          <th scope="row">{project.open && <span className="open-indicator" role="img" aria-label="Aperto in Unity" title="Aperto in Unity" />}{project.name}<small>{project.unityVersion} · {project.path}</small></th>
          <td>{date.format(new Date(project.lastUpdatedUtc))}</td><td>{date.format(new Date(project.createdUtc))}</td>
          <td>{project.importing ? 'In importazione' : <div className="actions project-actions">
            <button disabled={!!busy} onClick={() => showProjectPackages(project.path)}>Pacchetti</button>
            {
            project.recoveryId ? <button onClick={() => { setScreen('import'); setPanel('import'); setTimeout(() => document.getElementById('job-' + project.recoveryId)?.scrollIntoView(), 0); }}>Vedi importazione</button> :
            <button disabled={!!busy || project.open || project.incomplete} onClick={() => void run('Apertura di Unity…', async () => { await request('openProject', project.path); })}>Apri</button>}
          </div>}</td>
        </tr>)}</tbody>
      </table> : <p>{projects.length ? 'Nessun progetto corrispondente.' : 'Nessun progetto nella cartella di prova.'}</p>}
    </section> :
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
        {state?.jobs.length ? <section className="jobs"><div className="package"><h2>Importazioni</h2>
          <button disabled={!!busy || !!review} onClick={() => void run('Pulizia elenco…', async () => setState(await request<State>('clearFinished')))}>Pulisci lista</button></div>
          {state.jobs.map(job => <article key={job.id} id={'job-' + job.id}>
          <div className="package"><strong>{job.name}</strong><span>{jobStatus(job)}</span></div>
          <progress max="100" value={job.progress} aria-label={jobStatus(job)} />
          {job.state === 'Queued' && state.waitingForMemory && <p>In attesa di memoria. L’importazione partirà automaticamente.</p>}
          {!job.recoveryNoticeDismissed && (job.hasCompilationBlock || job.unityCompilationNeedsAttention) &&
            <p>Apri il progetto in Unity per controllare gli errori negli script.</p>}
          {!job.recoveryNoticeDismissed && job.state === 'Paused' && <p>Il progetto è conservato. Puoi riprendere quando vuoi.</p>}
          {!job.recoveryNoticeDismissed && job.state === 'Interrupted' && <p>L’app si è chiusa prima della fine. Puoi riprendere l’importazione.</p>}
          {!job.recoveryNoticeDismissed && job.packageResolutionFailed && <p>Controlla le versioni e le fonti dei pacchetti, poi riprendi l’importazione.</p>}
          {job.creatorCompanionPending && <p>Il progetto è pronto. Aggiungilo a Creator Companion da qui oppure dalla sua lista progetti.</p>}
          {job.creatorCompanionUnknown && <p>Il progetto è pronto. Controlla se compare nella lista di Creator Companion.</p>}
          <div className="actions">
            {job.active && !job.discardRequested && <button disabled={!!busy || !!review || ['Pausing', 'Cancelling'].includes(job.state)} onClick={() => void run('Pausa…', async () => setState(await request<State>('pause', job.id)))}>Pausa</button>}
            {!job.active && !job.discardRequested && !job.historySaveFailed && (job.hasCompilationBlock || job.unityCompilationNeedsAttention) &&
              <button disabled={!!busy || !!review} onClick={() => void run('Apertura di Unity…', async () => { await request('openImportProject', job.id); })}>Apri in Unity</button>}
            {!job.active && !job.discardRequested && job.packageContentsNeedRestore && <button disabled={!!busy || !!review}
              onClick={() => showProjectPackages(job.projectPath)}>Gestisci pacchetti</button>}
            {job.recoverable && !job.hasCompilationBlock && !job.packageContentsNeedRestore && !job.historySaveFailed && <button disabled={!!busy || !!review}
              onClick={() => void run('Preparazione…', async () => {
                const next = await request<Review | false>('reviewContinuation', job.id);
                if (next) setReview(next);
              })}>
              {job.state === 'Failed' ? 'Rivedi e riprova' : 'Riprendi importazione'}</button>}
            {job.historySaveFailed && <button disabled={!!busy || !!review} onClick={() => void run('Salvataggio…', async () => setState(await request<State>('retrySave', job.id)))}>Salva stato</button>}
            {job.canRetryRegistration && <button disabled={!!busy || !!review} onClick={() =>
              void run('Aggiunta a Creator Companion…', async () => setState(await request<State>('retryRegistration', job.id)))}>
              Aggiungi a Creator Companion
            </button>}
            {job.canDismiss && <button disabled={!!busy || !!review} onClick={() => void run('Aggiornamento…', async () => setState(await request<State>('dismiss', job.id)))}>{job.recoverable ? 'Nascondi avviso' : 'Rimuovi dalla lista'}</button>}
            {job.canDiscard && <button disabled={!!busy || !!review} onClick={() => void run('Annullamento…', async () => {
              try { setState(await request<State>('discard', job.id)); }
              catch (error) { setState(await request<State>('state')); throw error; }
            })}>{job.discardRequested ? 'Completa annullamento' : job.discardRemovesFolder ? 'Annulla ed elimina progetto' : 'Annulla importazione'}</button>}
            <button disabled={!!busy} onClick={() => void run('Apertura log…', async () => setLog(log?.id === job.id ? undefined : { id: job.id, text: await request<string>('log', job.id) }))}>{log?.id === job.id ? 'Chiudi log' : 'Apri log'}</button></div>
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
      {review && <section className="extras" aria-label={review.continuation ? 'Riprendi importazione' : 'Rivedi importazione'}>
        <h2>{review.continuation ? 'Riprendi importazione' : 'Rivedi importazione'}</h2>
        {review.projects.map(project => <article key={project.path}>
          <strong>{project.name}</strong>{project.target && <span> · {project.target === 'Android' ? 'Quest' : project.target === 'iOS' ? 'iOS' : 'PC'}</span>}
          <p className="path">{project.path}</p>
          <details open={project.removals.length > 0}>
            <summary>Pacchetti · {project.packages.length}</summary>
            <ul>{project.packages.map(item => <li key={item.id}>{item.name} {item.version}</li>)}</ul>
          </details>
          {project.removals.length > 0 && <p>Da rimuovere · {project.removals.join(', ')}</p>}
        </article>)}
        <div className="actions"><button onClick={() => setReview(undefined)}>Indietro</button>
          <button className="primary" disabled={!!busy} onClick={() => void run('Avvio importazioni…', async () => {
            setState(await request<State>('start', review.id)); setReview(undefined);
            if (!review.continuation) { setDrafts([]); setActiveId(''); }
          })}>{review.continuation ? 'Riprendi' : `Avvia ${review.projects.length === 1 ? 'importazione' : review.projects.length + ' importazioni'}`}</button></div></section>}
    </section>}
    <footer>
      <div role="status">{error || busy || notice || (screen === 'import' && drafts.length ? drafts.length + ' file' : '')}</div>
      {screen === 'import' && <div className="actions">
        {active && panel === 'import' && !review && <button disabled={!!busy} onClick={() => void run('Rimozione…', async () => {
          await request('remove', active.id); const left = drafts.filter(draft => draft.id !== active.id); setDrafts(left); setActiveId(left[0]?.id ?? '');
        })}>Rimuovi file</button>}
        {panel === 'import' && !state?.root && <button disabled={!!busy || !state} onClick={() => void run('Ricerca di Unity…', async () => setState(await request<State>('workspace')))}>Configura Unity</button>}
        {active && panel === 'import' && !review && state?.root &&
          <button className="primary" disabled={!!busy} onClick={() => void run('Preparazione…', async () => setReview(await request('review', drafts)))}>Rivedi e importa</button>}
      </div>}
      {state && <span className="app-version" aria-label={`Versione ${state.version}`}>{state.version}</span>}
    </footer>
  </main>;
}

createRoot(document.getElementById('root')!).render(<ImportPage />);
