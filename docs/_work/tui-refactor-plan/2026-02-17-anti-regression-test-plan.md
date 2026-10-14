# Anti-Regression Test Plan (TUI Refactor)

Date: 2026-02-17
Status: Ready for approval
Scope: validazione pre/post refactor TUI su wizard + installer

## 1) Obiettivo Test

Garantire che il refactor UI non introduca regressioni funzionali o UX nei flussi principali:

- setup da UnityPackage
- setup progetto esistente
- gestione pacchetti VPM/versioni
- advanced settings
- cleanup progetti incompleti
- progress/cancel nei job Unity lunghi

## 2) Strategia Test a Livelli

Livello 1 - Smoke rapido per fase

- verifica minima post-commit su flow critici.

Livello 2 - Regressione funzionale completa

- run completo checklist flussi principali.

Livello 3 - Compatibilita host terminale

- verifica su host con comportamento diverso.

Livello 4 - Modalita fallback/degraded

- validazione `legacy` fallback e progress plain.

## 3) Baseline Prima del Refactor

Prima di implementare:

1. Eseguire baseline wizard/installer su stato attuale.
2. Salvare output osservato (key behavior) nel report audit.
3. Registrare differenze note tra host terminale.

Asset baseline:

- `test-validation.ps1`
- script dry-run `-Test`
- log in `setup-scripts/logs/`

## 4) Matrice Flussi Critici

F1. Wizard main menu navigation

- arrows/enter/escape
- selezione opzioni
- rendering senza artefatti persistenti

F2. Setup project - UnityPackage

- input path valid/invalid
- suggerimento nome progetto
- conferme overwrite/esistente
- handoff a installer

F3. Setup project - Existing project

- validazione cartella Unity
- conferma proceed/cancel

F4. VPM package management

- aggiunta package
- change version
- remove package (non-default)
- protezione package default

F5. Advanced settings

- editor path
- projects root
- naming rules
- unitypackages folder

F6. Cleanup incomplete projects

- checklist paginata
- toggle selezioni
- delete confermato/cancellato

F7. Progress pipeline

- step create/import/finalize/install
- stato progress leggibile
- nessuna corruzione/overlap output

F8. Cancel behavior

- `Q`/`Esc` durante processi lunghi
- conferma cancel
- cleanup progetto creato

F9. Cancel semantics consistency

- coerenza tra hint schermata e comportamento reale di `Esc`
- conferme distruttive con messaggi espliciti e non ambigui

F10. Drag-and-drop quick path UX

- inserimento path da drag-and-drop senza attriti
- copy chiara su cosa fare in caso di path non valido
- possibilita di annullare/ritentare senza perdere contesto

## 5) Matrice Host / Ambiente

H1. PowerShell 7 in Windows Terminal
H2. PowerShell 7 in console classica
H3. PowerShell integrato VS Code
H4. Fallback no-VT (`VRCSETUP_TUI_VT` disattivo)
H5. Progress plain (`VRCSETUP_PROGRESS_PLAIN=1`)

Pass criteria:

- nessun blocco input
- no rendering rotto persistente
- fallback funzionante

## 6) Piano Test per Fase Implementativa

## Fase 1 (facade legacy-only)

Test:

- smoke F1/F2/F3/F4/F5
- confrontare behavior con baseline (deve restare equivalente)

## Fase 2 (adapter moderno menu/input)

Test:

- regressione completa F1-F6, F9, F10 su `legacy` e `modern`
- host matrix H1-H4

## Fase 3 (progress engine)

Test:

- regressione F7/F8 su host H1-H5
- cancellazione durante step Unity e verifica cleanup

## Fase 4/5 (hardening + rollout)

Test:

- full regression pack finale
- verifica fallback `legacy`
- verifica docs/config toggle
- verifica UX non-breaking su cancel + drag-and-drop quick path

## 7) Checklist Operativa (Pass/Fail)

1. Menu principale naviga correttamente con tastiera.
2. `Esc` torna indietro dove previsto senza lock.
3. Input path invalid mostra errore coerente e recuperabile.
4. UnityPackage flow non rompe suggerimento naming.
5. Existing project flow valida `Assets` correttamente.
6. Add package non causa ritorni inattesi o errori silenti.
7. Cambio versione package salva valore corretto.
8. Remove package rimuove solo package consentiti.
9. Package default resta protetto.
10. Advanced settings salva editor path valido.
11. Advanced settings salva projects root valido.
12. Regex naming settings non rompono wizard.
13. Checklist cleanup mantiene selezioni e paging.
14. Confirm dialog bloccanti funzionano sempre.
15. Progress step mostrano stato aggiornato.
16. Nessun overlap tra linee menu/progress.
17. Cancel con `Q`/`Esc` apre conferma corretta.
18. Cancel confermato interrompe e fa cleanup previsto.
19. Cancel rifiutato prosegue processo senza corruzione UI.
20. Fallback legacy attivabile e operativo.
21. `VRCSETUP_PROGRESS_PLAIN=1` produce output leggibile.
22. `-Test` dry-run resta funzionante.
23. Backup manifest resta creato quando previsto.
24. Log file resta generato e consultabile.
25. Nessun blocco critico nei 5 host/mode target.
26. Hint su `Esc/Cancel` coerenti con il comportamento effettivo della schermata.
27. Prompt cancel in progress descrive correttamente l impatto (con o senza delete).
28. Flusso drag-and-drop mantiene massimo 1 passaggio di recupero dopo errore path.
29. Happy-path non richiede pause ridondanti non necessarie.
30. Nessuna regressione di semplicità rispetto UX attuale.

## 8) Severity e Regole di Stop

P0 (stop immediato):

- perdita funzionalita core setup/install
- cancel non sicuro (possibile stato corrotto)
- blocco input non recuperabile

P1 (fix prima di avanzare fase):

- regressione UX rilevante su flusso core
- progress illeggibile o instabile in host principali

P2 (backlog breve):

- issue cosmetiche non bloccanti
- testi/hint non coerenti senza impatto funzionale

## 9) Artefatti di Test

Durante implementazione generare:

- `docs/_work/tui-refactor-plan/test-reports/phase-<n>-report.md`
- `docs/_work/tui-refactor-plan/test-reports/host-matrix.md`
- `docs/_work/tui-refactor-plan/test-reports/open-issues.md`

Ogni report contiene:

- Outcome
- Flussi testati
- Esito pass/fail
- Regressioni trovate
- Decisione gate (go/no-go)

## 10) Criteri di Uscita Finali

- tutti i test P0/P1 passati
- checklist operativa completata
- fallback legacy verificato
- no regressioni aperte critiche
- owner approva chiusura refactor
