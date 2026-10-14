# Risk Register

Date: 2026-02-18
Status: Active
Scope: refactor TUI adapter-first con fallback legacy

## Scale

- Probabilita: Low / Medium / High
- Impatto: Low / Medium / High
- Priorita: P1 alta, P2 media

## Risk Items

## RSK-01 Incoerenza cancel semantics tra schermate

- Probabilita: High
- Impatto: Medium
- Priorita: P1
- Evidenza:
- `setup-scripts/commands/wizard.ps1:1148`
- `setup-scripts/commands/wizard.ps1:1082`
- `setup-scripts/commands/wizard.ps1:1126`
- Mitigazione:
- copy contestuale e linee guida uniformi cancel/esc
- test dedicato F9 nel piano anti-regressione
- Exit criterion:
- tutti i confirm critici hanno messaggistica coerente con comportamento reale

## RSK-02 Prompt cancel ambiguo su delete nei processi lunghi

- Probabilita: High
- Impatto: High
- Priorita: P1
- Evidenza:
- `setup-scripts/lib/progress.ps1:59`
- Mitigazione:
- parametro esplicito per tipo cancel action
- test F8 + F9 su host matrix
- Exit criterion:
- prompt cancel allineato al contesto (step-only vs delete)

## RSK-03 Regressioni su drag-and-drop quick path

- Probabilita: Medium
- Impatto: High
- Priorita: P1
- Evidenza:
- `setup-scripts/commands/wizard.ps1:1013`
- `setup-scripts/commands/wizard.ps1:1117`
- `setup-scripts/commands/wizard.ps1:470`
- Mitigazione:
- helper centralizzato path prompt
- casi F10 + checklist 28/30 del test plan
- Exit criterion:
- stesso comportamento funzionale, meno attrito operazionale

## RSK-04 Over-coupling UI/business durante migrazione

- Probabilita: Medium
- Impatto: Medium
- Priorita: P2
- Evidenza:
- `Read-Host` e `Write-Host` diffusi in `setup-scripts/commands/wizard.ps1`
- Mitigazione:
- fase 1 facade legacy-only prima di qualsiasi restyling
- commit piccoli e reversibili
- Exit criterion:
- entrypoint wizard/installer usano API facade per i principali touchpoint UI

## RSK-05 Variabilita host terminale (render/flicker/progress)

- Probabilita: Medium
- Impatto: Medium
- Priorita: P2
- Evidenza:
- doppio modello render (`lib/menu.ps1`, `lib/progress.ps1`)
- Mitigazione:
- fallback legacy sempre attivo
- test host matrix H1-H5
- Exit criterion:
- nessun blocco/failure nei 5 host/mode target

## RSK-06 Regressione di semplicita operativa

- Probabilita: Medium
- Impatto: Medium
- Priorita: P2
- Evidenza:
- rischio da arricchimento UI eccessivo
- Mitigazione:
- principio "same high-level flow"
- no step aggiuntivi obbligatori in happy-path
- Exit criterion:
- checklist anti-regressione punto 30 passata

## Gate di Controllo Rischi

Gate A (post audit, pre implementazione):

- conferma owner su P1 priorities e branch strategy

Gate B (fine fase 1):

- nessuna regressione flussi core
- facade introdotta con comportamento invariato

Gate C (fine fase 3):

- cancel/progress risks mitigati e testati

Gate D (chiusura):

- nessun P1 aperto
- P2 residui documentati e accettati

