const topics = [
  ['Come importo un avatar?', 'Trascina i file UnityPackage nella schermata Importa. Scegli piattaforma e preset, poi controlla i pacchetti prima di avviare.'],
  ['Come riprendo un’importazione?', 'Apri Importa e cerca il progetto nell’elenco delle importazioni. Scegli Riprendi importazione. Se hai spostato il file, seleziona di nuovo l’originale.'],
  ['Come riparo un pacchetto?', 'Apri Progetti, seleziona Pacchetti accanto al progetto e scegli Ripristina pacchetti. Unity deve essere chiuso.'],
  ['Dove trovo il log?', 'Apri Attività, cerca l’operazione e scegli Apri log. Da lì puoi anche salvare la diagnostica in un file ZIP.']
];

export function Help({ onReport }: { onReport(): void }) {
  return <section className="help">
    <h2>Guida</h2>
    {topics.map(([title, answer]) => <details key={title}>
      <summary>{title}</summary>
      <p>{answer}</p>
    </details>)}
    <button onClick={onReport}>Segnala un problema</button>
  </section>;
}
