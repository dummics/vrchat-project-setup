import { useEffect, useState } from 'react';
import { createRoot } from 'react-dom/client';
import { ProjectPackages, type ProjectPackageList } from './ProjectPackages';
import { NewProjectPackages } from './NewProjectPackages';
import { Presets, type PresetLibrary } from './Presets';
import { AppearanceForm, type Appearance, type Palettes, useAppearance } from './appearance';
import { Activity, type ActivityEntry } from './Activity';
import { Help } from './Help';
import { Locale, type Language, translator } from './locale';
import './style.css';

type Packages = Record<string, string>;
const displayVersion = (identity: string) => identity.replace(/\+build\.(\d+)$/, ' · build $1');
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
  preferenceLanguage: Language;
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
type State = { root: string; packages: Packages; presets: Record<string, Packages>; jobs: Job[]; initial: Source[]; waitingForMemory: boolean; appearance: Appearance; version: string; language: 'it' | 'en' };
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
  const [selectedPaths, setSelectedPaths] = useState<string[]>([]);
  const [backup, setBackup] = useState<{ id: string; path: string; completedUtc: string }>();
  const [activity, setActivity] = useState<ActivityEntry[]>([]);
  const [projectPackages, setProjectPackages] = useState<ProjectPackageList>();
  const [newProject, setNewProject] = useState<{ name: string; target: string; preset: string; packages?: Packages }>();
  const [newProjectReview, setNewProjectReview] = useState<Review>();
  const [newProjectLibrary, setNewProjectLibrary] = useState<PresetLibrary>();
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
  const [discard, setDiscard] = useState<{ id: string; removeFolder: boolean }>();
  const [log, setLog] = useState<{ id: string; text: string }>();
  const active = drafts.find(draft => draft.id === activeId);
  const locale = state?.language ?? 'it';
  const t = translator(locale);
  useEffect(() => { document.documentElement.lang = locale; }, [locale]);
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
    if (screen !== 'projects' || busy || !projects.length) return;
    let cancelled = false;
    let timer: ReturnType<typeof setTimeout>;
    async function refreshEditors() {
      try {
        const open = new Set(await request<string[]>('projectEditorStates'));
        if (!cancelled) setProjects(current => current.some(project => project.open !== open.has(project.path))
          ? current.map(project => ({ ...project, open: open.has(project.path) }))
          : current);
      } catch { /* Keep displaying the last known editor state until the next poll. */ }
      if (!cancelled) timer = setTimeout(refreshEditors, 2000);
    }
    timer = setTimeout(refreshEditors, 2000);
    return () => { cancelled = true; clearTimeout(timer); };
  }, [screen, busy, projects.length]);
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
      const refreshed = await request<Project[]>('projects');
      setProjects(refreshed);
      setSelectedPaths(current => current.filter(path => refreshed.some(project =>
        project.path === path && !project.importing && !project.incomplete)));
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

  function showProjectPackages(path: string | string[]) {
    void run('Lettura pacchetti…', async () => {
      setProjectPackages(await request<ProjectPackageList>(typeof path === 'string' ? 'projectPackages' : 'batchProjectPackages', path));
      setPackageBack(screen === 'import' ? 'import' : 'projects');
      setNotice('');
      setScreen('project-packages');
    });
  }

  const visibleProjects = projects.filter(project => project.name.toLocaleLowerCase(locale).includes(filter.toLocaleLowerCase(locale)))
    .sort((a, b) => sort === 'name' ? a.name.localeCompare(b.name, locale) : b[sort].localeCompare(a[sort]));
  const date = new Intl.DateTimeFormat(locale === 'it' ? 'it-IT' : 'en-GB');

  return <Locale.Provider value={locale}><main data-ready={!!state} className={dragging ? 'dragging' : ''} onDragOver={event => { event.preventDefault(); setDragging(true); }}
    onDragLeave={event => { if (!event.currentTarget.contains(event.relatedTarget as Node | null)) setDragging(false); }}
    onDrop={event => { event.preventDefault(); setDragging(false); stage([...event.dataTransfer.files]); }}>
    <header><div><span className="brand">VRC Project Setup</span><h1>{screen === 'projects' ? t("Progetti") : screen === 'activity' ? t("Attività") : screen === 'settings' ? t("Impostazioni") : screen === 'presets' ? 'Preset' :
      screen === 'project-packages' ? projectPackages?.name ?? t("Pacchetti dei progetti") : panel === 'packages' ? t("Pacchetti") : panel === 'preset' ? t("Scegli un preset") : t("Importa")}</h1></div>
      {screen !== 'import' ? <button disabled={!!busy || screen === 'presets' && presetDirty} onClick={() => {
        if (screen === 'project-packages') { setNotice(''); setScreen(packageBack); }
        else { setNotice(''); setNewProject(undefined); setNewProjectReview(undefined); setNewProjectLibrary(undefined); setScreen('import'); }
      }}>{screen === 'project-packages' && packageBack === 'projects' ? t("← Progetti") : t("← Importa")}</button> :
        panel === 'import' ? <div className="actions"><button disabled={!!busy || !state || !!review} onClick={showProjects}>{t("Progetti")}</button>
          <button disabled={!!busy || !state || !!review} onClick={showActivity}>{t("Attività")}</button>
          <button disabled={!!busy || !state || !!review} onClick={showPresets}>Preset</button>
          <button disabled={!!busy || !state || !!review} onClick={showSettings}>{t("Impostazioni")}</button>
          <button disabled={!!busy || !state || !!review} onClick={() => stage()}>{t("+ Aggiungi file")}</button></div> :
          <button onClick={() => setPanel('import')}>{t("← Torna all’importazione")}</button>}</header>
    {screen === 'presets' && presetLibrary ? <section className="view" key="presets">
      <Presets library={presetLibrary} busy={!!busy} request={request} run={run} onDirty={setPresetDirty}
        onChange={async next => { setPresetLibrary(next); setState(await request<State>('state')); }} />
    </section> : screen === 'settings' && preferences ? <section className="view settings" key="settings">
      <h2>{t("Importazioni")}</h2>
      <form onSubmit={event => {
        event.preventDefault();
        void run(t("Salvataggio…"), async () => {
          setState(await request<State>('saveImportPreferences', preferences));
          setPreferences(await request<ImportPreferences>('importPreferences'));
          setUpdate(await request<UpdateView>('updateStatus'));
          setSettingsDirty(false);
          setNotice(t("Impostazioni salvate."));
        });
      }}>
        <fieldset disabled={!!busy}>
          {preferences.canEditPaths && <section className="workspace-paths">
            <h2>Unity</h2>
            {workspaceOptions.map(([key, picker, label]) => <div className="workspace-path" key={key}>
              <label htmlFor={key}>{t(label)}</label>
              <input id={key} value={preferences[key] ?? ''} onChange={event => {
                setPreferences({ ...preferences, [key]: event.target.value }); setNotice(''); setSettingsDirty(true);
              }} />
              <button type="button" aria-label={t('Sfoglia: {label}', { label: t(label) })} onClick={() => void run(t("Selezione percorso…"), async () => {
                const path = await request<string | false>('pickSettingsPath', picker);
                if (path) {
                  setPreferences({ ...preferences, [key]: path }); setNotice(''); setSettingsDirty(true);
                }
              })}>{t("Sfoglia")}</button>
            </div>)}
          </section>}
          <AppearanceForm value={preferences.appearance} palettes={preferences.palettes} onChange={appearance => {
            setPreferences({ ...preferences, appearance });
            setNotice('');
            setSettingsDirty(true);
          }} />
          <label className="package">{t('Lingua')}
            <select id="language" value={preferences.preferenceLanguage} onChange={event => {
              setPreferences({ ...preferences, preferenceLanguage: event.target.value as Language });
              setSettingsDirty(true);
              setNotice('');
            }}>
              <option value="system">{t('Sistema')}</option><option value="it">Italiano</option><option value="en">English</option>
            </select>
          </label>
          <label className="package">{t("Nascondi importazioni completate")}
            <input id="hide-completed" type="checkbox" checked={preferences.autoHideCompletedImports} onChange={event => {
              setPreferences({ ...preferences, autoHideCompletedImports: event.target.checked }); setNotice(''); setSettingsDirty(true);
            }} />
          </label>
          <details><summary>{t("Impostazioni avanzate")}</summary>
            <label className="package">{t("Limite importazioni simultanee")}
              <select id="import-limit" value={preferences.maxParallelImports} onChange={event => {
                setPreferences({ ...preferences, maxParallelImports: Number(event.target.value) }); setNotice(''); setSettingsDirty(true);
              }}>
                <option value={0}>{t("Automatico")}</option>
                {Array.from({ length: 8 }, (_, index) => <option value={index + 1} key={index}>{index + 1}</option>)}
              </select>
            </label>
          </details>
          <h2>{t("Aggiornamenti")}</h2>
          {updateOptions.map(([key, label]) => <label className="package" key={key}>{t(label)}
            <input id={key} type="checkbox" checked={preferences[key]} onChange={event => {
              setPreferences({ ...preferences, [key]: event.target.checked }); setNotice(''); setSettingsDirty(true);
            }} />
          </label>)}
          <button type="submit" className="primary">{t("Salva")}</button>
        </fieldset>
      </form>
      {update?.enabled && <section className="extras" aria-label={t("Versione applicazione")}>
        <p>{t('Versione {version}', { version: displayVersion(update.current) })}{update.ready ? t(' · {version} pronta', { version: displayVersion(update.ready) })
          : update.available ? t(' · {version} disponibile', { version: displayVersion(update.available) }) : ''}</p>
        <div className="actions">
          <button data-update-action="check" disabled={!!busy || settingsDirty} onClick={() => void run(t("Ricerca aggiornamenti…"), async () => {
            const status = await request<UpdateView>('checkUpdate');
            setUpdate(status);
            setNotice(status.available ? '' : t("L’app è aggiornata."));
          })}>{t("Controlla aggiornamenti")}</button>
          {update.installed && update.available && !update.ready && <button data-update-action="prepare" className="primary" disabled={!!busy || settingsDirty}
            onClick={() => void run(t("Preparazione aggiornamento…"), async () => setUpdate(await request<UpdateView>('prepareUpdate', update.available)))}>
            {t("Prepara aggiornamento")}
          </button>}
          {update.installed && update.ready && <button data-update-action="apply" className="primary" disabled={!!busy || settingsDirty}
            onClick={() => void run(t("Avvio aggiornamento…"), async () => { await request('applyUpdate'); })}>{t("Aggiorna e riavvia")}</button>}
        </div>
      </section>}
      <Help onReport={() => void run(t("Apertura assistenza…"), async () => { await request('openSupport'); })} />
    </section> : screen === 'project-packages' && projectPackages ? <section className="view" key="project-packages">
      <ProjectPackages project={projectPackages} busy={!!busy} request={request} run={run}
        onApplied={(project, message) => { setProjectPackages(project); setNotice(message); }} />
    </section> : screen === 'activity' ? <section className="view activity" key="activity">
      <Activity entries={activity} busy={!!busy} date={date} request={request} run={run} refresh={showActivity}
        onExported={() => setNotice(t("Diagnostica esportata."))} />
    </section> : screen === 'projects' ? <section className="view" key="projects">
      <div className="actions project-tools"><input aria-label={t("Cerca progetti")} placeholder={t("Cerca progetti")} value={filter} onChange={event => setFilter(event.target.value)} />
        <button disabled={!!busy} onClick={showProjects}>{t("Aggiorna")}</button>
        <button disabled={!!busy || !!newProject} onClick={() => void run(t("Apertura progetto…"), async () => {
          const project = await request<ProjectPackageList | false>('pickExistingProject');
          if (project) { setProjectPackages(project); setPackageBack('projects'); setScreen('project-packages'); }
        })}>{t("Apri progetto esistente")}</button>
        <button disabled={!!busy || !!newProject} onClick={() => setNewProject({ name: '', target: 'StandaloneWindows64', preset: '' })}>{t("Nuovo progetto")}</button>
      </div>
      {selectedPaths.length > 0 && <div className="actions batch-actions">
        <button className="primary" disabled={!!busy} onClick={() => showProjectPackages(selectedPaths.length === 1 ? selectedPaths[0] : selectedPaths)}>
          {t("Gestisci pacchetti · {count} selezionati", { count: selectedPaths.length })}
        </button>
        <button disabled={!!busy} onClick={() => setSelectedPaths([])}>{t("Deseleziona")}</button>
      </div>}
      {newProject && <section className="extras settings" aria-label={t("Nuovo progetto")}>
        <h2>{t("Nuovo progetto")}</h2>
        {newProjectReview ? <>
          {newProjectReview.projects.map(project => <div key={project.path}>
            <div className="package"><strong>{project.name}</strong><span>{project.target === 'Android' ? 'Quest' : project.target === 'iOS' ? 'iOS' : 'PC'}</span></div>
            <p className="path">{project.path}</p>
            <dl className="new-project-review-packages">{project.packages.map(item => <div className="package" key={item.id}>
              <dt>{item.name}</dt><dd>{item.version}</dd>
            </div>)}</dl>
          </div>)}
          <div className="actions">
            <button disabled={!!busy} onClick={() => setNewProjectReview(undefined)}>{t("Indietro")}</button>
            <button className="primary" disabled={!!busy} onClick={() => void run(t("Creazione progetto…"), async () => {
              setState(await request<State>('createProject', newProjectReview.id));
              setNewProject(undefined); setNewProjectReview(undefined); setNewProjectLibrary(undefined); setPanel('import'); setScreen('import');
            })}>{t("Crea progetto")}</button>
          </div>
        </> : <><form className="new-project-form" onSubmit={event => {
          event.preventDefault();
          void run(t("Preparazione progetto…"), async () => setNewProjectReview(await request('reviewNewProject', { ...newProject, preset: newProject.preset || null })));
        }}>
          <label className="package">{t("Nome progetto")} <input aria-label={t("Nome nuovo progetto")} value={newProject.name} onChange={event => setNewProject({ ...newProject, name: event.target.value })} /></label>
          <label className="package">{t("Piattaforma")}
            <select value={newProject.target} onChange={event => setNewProject({ ...newProject, target: event.target.value })}>
              <option value="StandaloneWindows64">PC</option><option value="Android">Quest</option><option value="iOS">iOS</option>
            </select>
          </label>
          <label className="package">Preset
            <select aria-label="Preset" value={newProject.preset} onChange={event => setNewProject({ ...newProject, preset: event.target.value, packages: undefined })}>
              <option value="">{t("Predefinito")}</option>
              {Object.keys(state?.presets ?? {}).map(name => <option key={name}>{name}</option>)}
            </select>
          </label>
          <div className="actions">
            <button type="button" disabled={!!busy} aria-expanded={!!newProjectLibrary} onClick={() => {
              if (newProjectLibrary) setNewProjectLibrary(undefined);
              else void run(t("Lettura pacchetti…"), async () => setNewProjectLibrary(await request('presets')));
            }}>{newProjectLibrary ? t("Chiudi pacchetti") : t("Personalizza pacchetti")}</button>
            <button type="button" disabled={!!busy} onClick={() => { setNewProject(undefined); setNewProjectLibrary(undefined); }}>{t("Annulla")}</button>
            <button className="primary" disabled={!!busy || !newProject.name.trim()}>{t("Rivedi progetto")}</button>
          </div>
        </form>
        {newProjectLibrary && <NewProjectPackages library={newProjectLibrary} busy={!!busy} request={request} run={run}
          selection={newProject.packages ?? (newProject.preset ? newProjectLibrary.presets[newProject.preset] ?? {} : newProjectLibrary.defaults)}
          onChange={packages => setNewProject({ ...newProject, packages })}
          onSaved={async library => {
            setNewProjectLibrary(library);
            setNewProject({ ...newProject, preset: library.selected ?? '', packages: undefined });
            setState(await request<State>('state'));
          }} />}
        </>}
      </section>}
      {visibleProjects.length ? <table className="project-table">
        <thead><tr><th className="project-select" scope="col" aria-label={t("Selezione")} /><th><button onClick={() => setSort('name')}>{t("Nome")}</button></th>
          <th><button onClick={() => setSort('lastUpdatedUtc')}>{t("Modifica")}</button></th>
          <th><button onClick={() => setSort('createdUtc')}>{t("Creazione")}</button></th><th scope="col" aria-label={t("Azioni")} /></tr></thead>
        <tbody>{visibleProjects.map(project => <tr key={project.path}>
          <td className="project-select">
            {!project.importing && !project.incomplete && <input type="checkbox" checked={selectedPaths.includes(project.path)}
              disabled={!!busy} aria-label={t("Seleziona {name}", { name: project.name })}
              onChange={event => setSelectedPaths(current => event.target.checked
                ? [...current, project.path] : current.filter(path => path !== project.path))} />}
          </td>
          <th scope="row">{project.open && !project.importing && <span className="open-indicator" role="img" aria-label={t("Aperto in Unity")} title={t("Aperto in Unity")} />}{project.name}<small>{project.unityVersion} · {project.path}</small></th>
          <td>{date.format(new Date(project.lastUpdatedUtc))}</td><td>{date.format(new Date(project.createdUtc))}</td>
          <td>{project.importing ? t("In importazione") : <div className="actions project-actions">
            <button disabled={!!busy} onClick={() => showProjectPackages(project.path)}>{t("Pacchetti")}</button>
            {
            project.recoveryId ? <button onClick={() => { setScreen('import'); setPanel('import'); setTimeout(() => document.getElementById('job-' + project.recoveryId)?.scrollIntoView(), 0); }}>{t("Vedi importazione")}</button> :
            <button disabled={!!busy || project.open || project.incomplete} onClick={() => void run(t("Apertura di Unity…"), async () => { await request('openProject', project.path); })}>{t("Apri")}</button>}
            <details className="project-options">
              <summary aria-label={t("Opzioni per {name}", { name: project.name })}>···</summary>
              <button disabled={!!busy} onClick={event => {
                const options = event.currentTarget.closest('details');
                if (options) options.open = false;
                void run(t("Ricerca backup…"), async () => setBackup(await request('reviewProjectBackup', project.path)));
              }}>{t("Ripristina backup")}</button>
            </details>
          </div>}</td>
        </tr>)}</tbody>
      </table> : <p>{projects.length ? t("Nessun progetto corrispondente.") : t("Nessun progetto nella cartella selezionata.")}</p>}
      {backup && <dialog className="extras backup-dialog" aria-labelledby="backup-title"
        ref={node => { if (node && !node.open) node.showModal(); }}
        onCancel={event => { event.preventDefault(); setBackup(undefined); }}>
        <h2 id="backup-title">{t("Ripristina backup")}</h2>
        <p className="path">{backup.path}</p>
        <p>{t("Backup del {date}", { date: date.format(new Date(backup.completedUtc)) })}</p>
        <p>{t("I file attuali verranno conservati in una cartella di recupero.")}</p>
        <div className="actions">
          <button autoFocus onClick={() => setBackup(undefined)}>{t("Annulla")}</button>
          <button className="primary" onClick={() => {
            setBackup(undefined);
            void run(t("Ripristino backup…"), async () => {
              setProjects(await request<Project[]>('restoreProjectBackup', backup.id));
              setNotice(t("Backup ripristinato."));
            });
          }}>{t("Conferma ripristino")}</button>
        </div>
      </dialog>}
    </section> :
    <section className="view" key={panel}>
      {panel === 'import' && <>
        {!active ? <button className="drop" disabled={!!busy || !state} onClick={() => stage()}><span aria-hidden="true">＋</span>{t("Trascina qui i file .unitypackage")}</button> :
          <>
            <div className="tabs" aria-label={t("File da importare")}>
              {drafts.map(draft => <button key={draft.id} className={draft.id === activeId ? 'selected' : ''} aria-pressed={draft.id === activeId}
                onClick={() => setActiveId(draft.id)}>{draft.file}</button>)}
            </div>
            <div className="fields">
              <input aria-label={t("Nome progetto")} value={active.name} onChange={event => edit({ name: event.target.value })} />
              <div className="platforms" aria-label={t("Piattaforma")}>
                {([['StandaloneWindows64', 'PC'], ['Android', 'Quest'], ['iOS', 'iOS']] as const).map(([target, label]) =>
                  <button key={target} aria-pressed={active.target === target} onClick={() => edit({ target })}>{label}</button>)}
              </div>
            </div>
            <p className="path">{state?.root ? state.root + '\\' + active.name : t("Configura Unity e scegli la cartella dei progetti")}</p>
            <button className="row" onClick={() => setPanel('preset')}>Preset <span>{active.preset ?? t("Predefinito")} ›</span></button>
            <button className="row" onClick={() => setPanel('packages')}>{t("Pacchetti")} <span>{Object.keys(active.packages).length} {t("selezionati ›")}</span></button>
            {active.suggestions.some(item => !active.packages[item.id]) && <div className="extras">
              <strong>{t("＋ Pacchetti facoltativi")}</strong>
              {active.suggestions.filter(item => !active.packages[item.id]).map(item => <div className="package" key={item.id}>{item.name}
                <button onClick={() => edit({ packages: { ...active.packages, [item.id]: item.version } })}>{t("Aggiungi")}</button></div>)}</div>}
          </>}
        {state?.jobs.length ? <section className="jobs"><div className="package"><h2>{t("Importazioni")}</h2>
          <button disabled={!!busy || !!review} onClick={() => void run(t("Pulizia elenco…"), async () => setState(await request<State>('clearFinished')))}>{t("Pulisci lista")}</button></div>
          {state.jobs.map(job => <article key={job.id} id={'job-' + job.id}>
          <div className="package"><strong>{job.name}</strong><span>{t(jobStatus(job))}</span></div>
          <progress max="100" value={job.progress} aria-label={t(jobStatus(job))} />
          {job.state === 'Queued' && state.waitingForMemory && <p>{t("In attesa di memoria. L’importazione partirà automaticamente.")}</p>}
          {!job.recoveryNoticeDismissed && (job.hasCompilationBlock || job.unityCompilationNeedsAttention) &&
            <p>{t("Apri il progetto in Unity per controllare gli errori negli script.")}</p>}
          {!job.recoveryNoticeDismissed && job.state === 'Paused' && <p>{t("Il progetto è conservato. Puoi riprendere quando vuoi.")}</p>}
          {!job.recoveryNoticeDismissed && job.state === 'Interrupted' && <p>{t("L’app si è chiusa prima della fine. Puoi riprendere l’importazione.")}</p>}
          {!job.recoveryNoticeDismissed && job.packageResolutionFailed && <p>{t("Controlla le versioni e le fonti dei pacchetti, poi riprendi l’importazione.")}</p>}
          {job.creatorCompanionPending && <p>{t("Il progetto è pronto. Aggiungilo a Creator Companion da qui oppure dalla sua lista progetti.")}</p>}
          {job.creatorCompanionUnknown && <p>{t("Il progetto è pronto. Controlla se compare nella lista di Creator Companion.")}</p>}
          <div className="actions">
            {job.active && !job.discardRequested && <button disabled={!!busy || !!review || ['Pausing', 'Cancelling'].includes(job.state)} onClick={() => void run(t("Pausa…"), async () => setState(await request<State>('pause', job.id)))}>{t("Pausa")}</button>}
            {!job.active && !job.discardRequested && !job.historySaveFailed && (job.hasCompilationBlock || job.unityCompilationNeedsAttention) &&
              <button disabled={!!busy || !!review} onClick={() => void run(t("Apertura di Unity…"), async () => { await request('openImportProject', job.id); })}>{t("Apri in Unity")}</button>}
            {!job.active && !job.discardRequested && job.packageContentsNeedRestore && <button disabled={!!busy || !!review}
              onClick={() => showProjectPackages(job.projectPath)}>{t("Gestisci pacchetti")}</button>}
            {job.recoverable && !job.hasCompilationBlock && !job.packageContentsNeedRestore && !job.historySaveFailed && <button disabled={!!busy || !!review}
              onClick={() => void run(t("Preparazione…"), async () => {
                const next = await request<Review | false>('reviewContinuation', job.id);
                if (next) setReview(next);
              })}>
              {job.state === 'Failed' ? t("Rivedi e riprova") : t("Riprendi importazione")}</button>}
            {job.historySaveFailed && <button disabled={!!busy || !!review} onClick={() => void run(t("Salvataggio…"), async () => setState(await request<State>('retrySave', job.id)))}>{t("Salva stato")}</button>}
            {job.canRetryRegistration && <button disabled={!!busy || !!review} onClick={() =>
              void run(t("Aggiunta a Creator Companion…"), async () => setState(await request<State>('retryRegistration', job.id)))}>
              {t("Aggiungi a Creator Companion")}
            </button>}
            {job.canDismiss && <button disabled={!!busy || !!review} onClick={() => void run(t("Aggiornamento…"), async () => setState(await request<State>('dismiss', job.id)))}>{job.recoverable ? t("Nascondi avviso") : t("Rimuovi dalla lista")}</button>}
            {job.canDiscard && discard?.id !== job.id && <button disabled={!!busy || !!review} onClick={() =>
              setDiscard({ id: job.id, removeFolder: job.discardRemovesFolder })}>{job.discardRequested ? t("Completa annullamento") : job.discardRemovesFolder ? t("Annulla ed elimina progetto") : t("Annulla importazione")}</button>}
            <button disabled={!!busy} onClick={() => void run(t("Apertura log…"), async () => setLog(log?.id === job.id ? undefined : { id: job.id, text: await request<string>('log', job.id) }))}>{log?.id === job.id ? t("Chiudi log") : t("Apri log")}</button></div>
          {discard?.id === job.id && <section className="discard-confirmation" aria-label={t("Conferma annullamento")}>
            <p>{discard.removeFolder ? t("Verrà eliminata la cartella creata per questa importazione.") : t("L’importazione verrà rimossa dalla coda.")}</p>
            {discard.removeFolder && <p className="path">{job.projectPath}</p>}
            <div className="actions">
              <button autoFocus disabled={!!busy} onClick={() => setDiscard(undefined)}>{t("Indietro")}</button>
              <button disabled={!!busy || !!review || !job.canDiscard} onClick={() => void run(t("Annullamento…"), async () => {
                try { setState(await request<State>('discard', discard)); }
                catch (error) { setState(await request<State>('state')); throw error; }
                finally { setDiscard(undefined); }
              })}>{discard.removeFolder ? t("Elimina progetto") : t("Conferma annullamento")}</button>
            </div>
          </section>}
          {log?.id === job.id && <pre>{log.text || t("Nessun log disponibile.")}</pre>}</article>)}</section> : null}
      </>}
      {active && panel === 'preset' && <div className="presets">
        {[[null, state?.packages ?? {}], ...Object.entries(state?.presets ?? {})].map(([name, packages]) =>
          <button key={(name as string | null) ?? ''} className="row" aria-pressed={active.preset === name}
            onClick={() => { edit({ preset: name as string | null, packages: { ...packages as Packages } }); setPanel('import'); }}>
            {(name as string | null) ?? t("Predefinito")}<span>{active.preset === name ? '✓' : '›'}</span>
          </button>)}
      </div>}
      {active && panel === 'packages' && <div className="editor">
        {Object.entries(active.packages).map(([id, version]) => <div className="package" key={id}><span>{id}</span>
          <input aria-label={t("Versione") + id} value={version} onChange={event => edit({ packages: { ...active.packages, [id]: event.target.value } })}/>
          <button onClick={() => { const packages = { ...active.packages }; delete packages[id]; edit({ packages }); }}>{t("Escludi")}</button></div>)}
        {Object.entries(active.preset ? state?.presets[active.preset] ?? {} : state?.packages ?? {}).filter(([id]) => !(id in active.packages))
          .map(([id, version]) => <div className="package" key={id}><span>{id}</span><button onClick={() => edit({ packages: { ...active.packages, [id]: version } })}>{t("Includi")}</button></div>)}
      </div>}
      {review && <section className="extras" aria-label={review.continuation ? t("Riprendi importazione") : t("Rivedi importazione")}>
        <h2>{review.continuation ? t("Riprendi importazione") : t("Rivedi importazione")}</h2>
        {review.projects.map(project => <article key={project.path}>
          <strong>{project.name}</strong>{project.target && <span> · {project.target === 'Android' ? 'Quest' : project.target === 'iOS' ? 'iOS' : 'PC'}</span>}
          <p className="path">{project.path}</p>
          <details open={project.removals.length > 0}>
            <summary>{t("Pacchetti ·")} {project.packages.length}</summary>
            <ul>{project.packages.map(item => <li key={item.id}>{item.name} {item.version}</li>)}</ul>
          </details>
          {project.removals.length > 0 && <p>{t("Da rimuovere ·")} {project.removals.join(', ')}</p>}
        </article>)}
        <div className="actions"><button onClick={() => setReview(undefined)}>{t("Indietro")}</button>
          <button className="primary" disabled={!!busy} onClick={() => void run(t("Avvio importazioni…"), async () => {
            setState(await request<State>('start', review.id)); setReview(undefined);
            if (!review.continuation) { setDrafts([]); setActiveId(''); }
          })}>{review.continuation ? t('Riprendi') : review.projects.length === 1 ? t('Avvia importazione')
            : t('Avvia {count} importazioni', { count: review.projects.length })}</button></div></section>}
    </section>}
    <footer>
      <div role="status">{t(error || busy || notice) || (screen === 'import' && drafts.length ? drafts.length + ' file' : '')}</div>
      {screen === 'import' && <div className="actions">
        {active && panel === 'import' && !review && <button disabled={!!busy} onClick={() => void run(t("Rimozione…"), async () => {
          await request('remove', active.id); const left = drafts.filter(draft => draft.id !== active.id); setDrafts(left); setActiveId(left[0]?.id ?? '');
        })}>{t("Rimuovi file")}</button>}
        {panel === 'import' && !state?.root && <button disabled={!!busy || !state} onClick={() => void run(t("Ricerca di Unity…"), async () => setState(await request<State>('workspace')))}>{t("Configura Unity")}</button>}
        {active && panel === 'import' && !review && state?.root &&
          <button className="primary" disabled={!!busy} onClick={() => void run(t("Preparazione…"), async () => setReview(await request('review', drafts)))}>{t("Rivedi e importa")}</button>}
      </div>}
      {state && <span className="app-version" aria-label={t('Versione {version}', { version: displayVersion(state.version) })}>{displayVersion(state.version)}</span>}
    </footer>
  </main></Locale.Provider>;
}

createRoot(document.getElementById('root')!).render(<ImportPage />);
