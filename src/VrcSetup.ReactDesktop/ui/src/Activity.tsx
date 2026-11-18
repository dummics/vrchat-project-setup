import { useState } from 'react';

export type ActivityEntry = {
  id: string; name: string; date: string; kind: 'project' | 'import' | 'operation'; status: string;
};
type ProjectDetail = {
  name: string; path: string; status: string; packages: { id: string; name: string; version?: string; change: string }[];
};
type Props = {
  entries: ActivityEntry[]; busy: boolean; date: Intl.DateTimeFormat;
  request<T>(method: string, data?: unknown): Promise<T>;
  run(label: string, action: () => Promise<void>): Promise<void>;
  refresh(): void; onExported(): void;
};
const statuses: Record<string, string> = {
  completed: 'Completata', recovered: 'Completata', warning: 'Completata con avviso', failed: 'Non riuscita',
  cancelled: 'Annullata', interrupted: 'Interrotta', paused: 'In pausa', queued: 'In coda', running: 'In corso',
  pausing: 'Messa in pausa…', cancelling: 'Arresto in corso…', unchanged: 'Già aggiornato', skipped: 'Saltato'
};
const changes: Record<string, string> = { add: 'Aggiunta', update: 'Modifica', remove: 'Rimozione' };

export function Activity({ entries, busy, date, request, run, refresh, onExported }: Props) {
  const [details, setDetails] = useState<{ key: string; projects: ProjectDetail[] }>();
  const [log, setLog] = useState<{ key: string; text: string }>();
  return <>
    <div className="actions">
      <button disabled={busy} onClick={() => { setDetails(undefined); setLog(undefined); refresh(); }}>Aggiorna</button>
      <button disabled={busy} onClick={() => void run('Esportazione…', async () => {
        if (await request<boolean>('exportDiagnostics')) onExported();
      })}>Esporta diagnostica</button>
    </div>
    {entries.length ? entries.map(entry => {
      const key = entry.kind + ':' + entry.id;
      const expanded = details?.key === key;
      const logOpen = log?.key === key;
      return <article key={key}>
        <div className="package"><strong>{entry.name}</strong>
          <span>{statuses[entry.status] ?? 'Da verificare'} · {date.format(new Date(entry.date))}</span>
        </div>
        <div className="activity-actions">
          <button disabled={busy} aria-expanded={expanded} onClick={() => {
            if (expanded) { setDetails(undefined); return; }
            void run('Lettura dettagli…', async () => setDetails({
              key, projects: await request<ProjectDetail[]>('activityDetails', { id: entry.id, kind: entry.kind })
            }));
          }}>{expanded ? 'Chiudi dettagli' : 'Dettagli'}</button>
          <button disabled={busy} aria-expanded={logOpen} onClick={() => {
            if (logOpen) { setLog(undefined); return; }
            const method = entry.kind === 'operation' ? 'readOperationLog' : 'log';
            void run('Apertura log…', async () => setLog({ key, text: await request<string>(method, entry.id) }));
          }}>{logOpen ? 'Chiudi log' : 'Apri log'}</button>
        </div>
        {expanded && details.projects.map(project => <section className="activity-detail" key={project.path}>
          <div className="package"><strong>{project.name}</strong>
            <span>{statuses[project.status] ?? 'Da verificare'}</span></div>
          <p className="path">{project.path}</p>
          {project.packages.length > 0 && <ul>
            {project.packages.map(item => <li key={item.id}>{item.name} {item.version}
              {changes[item.change] && <span> · {changes[item.change]}</span>}
            </li>)}
          </ul>}
        </section>)}
        {logOpen && <pre>{log.text || 'Nessun log disponibile.'}</pre>}
      </article>;
    }) : <p>Nessuna attività registrata.</p>}
  </>;
}
