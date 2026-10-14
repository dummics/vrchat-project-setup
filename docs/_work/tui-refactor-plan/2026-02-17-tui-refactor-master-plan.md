# TUI Refactor Master Plan (No Implementation Yet)

Date: 2026-02-17
Workspace: `C:\Users\domix\.scriptsdum\_unityprojectsetup`
Status: Draft for owner approval

## 1) Obiettivo

Portare la UX terminale a un livello piu robusto e moderno (menu, input, loading/progress, stato step), riducendo flicker, coupling e fragilita su host diversi, senza riscrivere la logica di business.

## 2) Non-Obiettivi

- Nessuna implementazione in questa fase.
- Nessun cambio ai flussi business `wizard`/`installer` oltre all integrazione UI.
- Nessun cambio release/distribuzione finche il piano non e approvato.

## 3) Stato Attuale (As-Is)

Punti principali nel codice:

- `setup-scripts/lib/menu.ps1`
- `setup-scripts/lib/progress.ps1`
- `setup-scripts/commands/wizard.ps1`
- `setup-scripts/commands/installer.ps1`

Osservazioni:

- La UI e gia in parte centralizzata (`Show-Menu`, `Show-MenuFilter`, `Show-ChecklistPaged`, `Show-ProcessProgress`).
- Ci sono molte `Read-Host` e `Write-Host` direttamente nel wizard, quindi la separazione non e completa.
- `Write-Progress` dipende molto dal terminal host, con fallback best-effort.
- Buona base per migrazione incrementale con adapter, senza big-bang rewrite.

## 4) Opzioni Tecnologiche

### Opzione A: Python `Textual` (sidecar app)

Pro:

- UI molto ricca, moderna, layout/event loop robusti.
- Ottime primitive per progress, loading, schermate dinamiche.

Contro:

- Introduce runtime Python + packaging cross-platform da gestire.
- Integrazione con backend PowerShell da progettare (IPC/process bridge).
- Aumenta complessita operativa e superficie di failure.

### Opzione B: `Spectre.Console` via PowerShell/.NET (Recommended)

Pro:

- Rimane nello stack .NET/PowerShell attuale.
- Prompt, table, progress, spinner gia pronti e stabili.
- Migrazione piu semplice e meno rischio operativo.

Contro:

- Minor liberta rispetto a Textual su layout avanzato.
- Alcune schermate complesse richiedono compromessi UX.

### Opzione C: `Terminal.Gui`

Pro:

- Esperienza "desktop in terminale" con widget completi.

Contro:

- Integrazione e manutenzione piu impegnative.
- Overkill per workflow principalmente lineare/wizard.

### Decisione Proposta

Procedere con Opzione B (Spectre.Console adapter-first).
Tenere Textual come opzione futura solo se serve una vera app TUI separata dal backend.

## 5) Architettura Target (To-Be)

## 5.1 UI Adapter Layer

Introdurre un layer unico (esempio `lib/ui.ps1`) con API stabili:

- `Show-UiMenu`
- `Show-UiFilterMenu`
- `Show-UiChecklist`
- `Show-UiPromptText`
- `Show-UiConfirm`
- `Show-UiNotice`
- `Show-UiProgressStart`
- `Show-UiProgressUpdate`
- `Show-UiProgressEnd`

Strategia:

- Backend `wizard/installer` chiamano solo il layer UI.
- Il layer UI decide provider:
- `legacy` (attuale)
- `modern` (Spectre)

Selezione provider tramite env/config:

- `VRCSETUP_UI_MODE=legacy|modern`
- fallback automatico a `legacy` su host non compatibili.

## 5.2 Event Model per Progress

Uniformare gli aggiornamenti progress con evento strutturato:

`{ step, phase, elapsed, cpu, ram, status, tail, cancellable, percent }`

Benefici:

- progress coerenti tra processi.
- facile logging/diagnostica.
- future estensioni (export stato, telemetry).

## 6) Piano d Azione Incrementale

## Fase 0 - Baseline e freeze di comportamento

Deliverable:

- Mappa completa funzioni UI correnti e chiamanti.
- Lista schermate/input/progress con expected behavior.

Done quando:

- Esiste checklist baseline approvata.

Commit target (quando implementiamo):

- `docs: baseline tui behavior map`

## Fase 1 - Introduzione facade UI senza cambiare UX

Deliverable:

- Nuovo layer UI (facade) che wrappa funzioni legacy.
- Nessun cambio visuale/funzionale percepito.

Done quando:

- `wizard` e `installer` usano facade invece di chiamate sparse dirette.

Commit target:

- `refactor: add ui facade with legacy adapter`
- `refactor: route wizard ui calls through facade`
- `refactor: route installer progress through facade`

## Fase 2 - Adapter moderno menu/input

Deliverable:

- Implementazione provider `modern` per menu/filter/checklist/prompt.
- Supporto fallback automatico a `legacy`.

Done quando:

- Menu principali e schermate ad alta frequenza usano renderer moderno.

Commit target:

- `feat: add modern tui adapter for menu and prompt`
- `feat: enable ui mode switch with safe fallback`

## Fase 3 - Progress engine moderno

Deliverable:

- Progress unificato per step lunghi (`create/import/finalize/install`).
- Event model completo e stato cancellation robusto.

Done quando:

- Nessun overlap/corruzione output durante processi lunghi.

Commit target:

- `feat: add structured progress events`
- `feat: modern progress renderer with cancellation hooks`

## Fase 4 - Hardening + polish

Deliverable:

- Rifiniture UX (messaggi, hint, coerenza colori/stati).
- Riduzione `Write-Host` e `Read-Host` non mediati.
- Documentazione operativa aggiornata.

Done quando:

- Checklist pre-release verde.

Commit target:

- `refactor: unify wizard prompts and notices`
- `docs: update tui usage and troubleshooting`

## Fase 5 - Rollout controllato

Deliverable:

- `legacy` default iniziale, `modern` opt-in.
- Periodo di feedback e bugfix.
- Switch default a `modern` solo dopo stabilita.

Done quando:

- owner approva il cambio default.

Commit target:

- `chore: set modern ui as default after stabilization`

## 7) Strategia Branch e Commit Regolari (Per fase implementativa)

Proposta:

- Branch dedicato unico per il refactor:
- `feat/tui-adapter-refactor`

Regole commit:

- 1 commit per blocco funzionale piccolo e reviewabile.
- Prefissi coerenti: `feat:`, `refactor:`, `docs:`, `chore:`.
- Nessun commit "misc" grande.

Ritmo consigliato:

- Commit ogni 45-90 minuti di lavoro reale o a fine mini-milestone.
- Push e sync solo su richiesta owner.

## 8) Rischi e Mitigazioni

R1: Regressioni input da tastiera tra host PowerShell diversi

- Mitigazione: provider duale + fallback legacy + smoke test manuale su host target.

R2: Progress output instabile con processi Unity lunghi

- Mitigazione: event model unificato + una sola pipeline di render.

R3: Complessita iniziale percepita alta

- Mitigazione: fasi piccole, commit frequenti, nessun big bang.

R4: Dipendenza nuova (Spectre o altro)

- Mitigazione: lock versione e documentazione install/compat.

## 9) Criteri di Accettazione (Definition of Done)

- UX menu e progress piu leggibile e consistente.
- Nessuna perdita di funzionalita rispetto al flusso attuale.
- Fallback `legacy` sempre disponibile.
- Codice UI piu testabile e meno accoppiato a `wizard.ps1`.
- Documentazione aggiornata e chiara.

## 9.1 Addendum UX Audit (2026-02-18)

Findings aggiuntivi emersi dall audit:

- Incoerenza semantica `Esc/Cancel`: header globale dice "ESC cancels", ma diversi confirm bloccano `Esc` (`AllowCancel $false`) in `setup-scripts/commands/wizard.ps1:1148`, `setup-scripts/commands/wizard.ps1:1082`, `setup-scripts/commands/wizard.ps1:1126`.
- Prompt cancel nei job lunghi fuorviante in alcuni contesti: testo fisso "delete the created project" anche quando non sempre applicabile in `setup-scripts/lib/progress.ps1:59`.
- Flusso drag-and-drop rapido non uniforme: prompt path ripetuti in punti diversi con micro-variazioni (`setup-scripts/commands/wizard.ps1:1013`, `setup-scripts/commands/wizard.ps1:1117`, `setup-scripts/commands/wizard.ps1:470`).
- Shortcut numeriche limitate a una sola cifra (`1..9`) in menu lunghi: `setup-scripts/lib/menu.ps1:316`.
- `Show-MenuFilter` puo interpretare Enter senza match come input libero, aumentando errori da typo: `setup-scripts/lib/menu.ps1:347`, `setup-scripts/lib/menu.ps1:550`.
- Troppe pause bloccanti `Press ENTER` rompono la fluidita su happy-path: `setup-scripts/commands/wizard.ps1:1111`, `setup-scripts/commands/wizard.ps1:1132`.

Direzione proposta (non-breaking):

- mantenere comportamenti core invariati
- introdurre copy/hint coerenti
- centralizzare prompt path e cancel copy
- aggiungere UX safeguards opt-in prima di rendere default

## 10) Checkpoint Decisionale Prima di Implementare

Da confermare dall owner:

1. Tecnologia primaria: `Spectre.Console` (recommended) o altra.
2. Nome branch dedicato.
3. Policy rollout: `legacy` default iniziale con `modern` opt-in.
4. Granularita commit (proposta: piccola, per mini-milestone).
5. Conferma backlog UX non-breaking (cancel semantics + drag-drop quick path).

## 11) Prossimi Step (dopo conferma owner)

1. Creazione branch dedicato concordato.
2. Implementazione Fase 1 (facade legacy-only).
3. Commit regolare e checkpoint rapido dopo ogni mini-fase.
4. Proseguire con Fase 2 e Fase 3 solo dopo ok sui checkpoint.
