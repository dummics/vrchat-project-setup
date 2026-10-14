# Audit Findings

Date: 2026-02-18
Status: Completed (static audit)
Policy: nessuna implementazione in questa fase

## Severity Legend

- P0: blocker alto rischio (stop implementazione finche non chiarito)
- P1: rischio alto/prioritario da trattare nella prima parte del refactor
- P2: miglioramento importante ma non bloccante

## Findings

## FND-01 (P1) Semantica `Esc/Cancel` non uniforme

Evidenza:

- Header wizard: `ESC cancels` (`setup-scripts/commands/wizard.ps1:1148`)
- Confirm importanti con `AllowCancel $false`:
- `setup-scripts/commands/wizard.ps1:1082`
- `setup-scripts/commands/wizard.ps1:1126`
- `setup-scripts/commands/wizard.ps1:1176`

Impatto:

- Riduce prevedibilita UX.
- Aumenta errori d uso in schermate critiche.

Raccomandazione:

- In fase facade, introdurre copy contestuale per schermata:
- quando `Esc` non e ammesso, esplicitarlo nel testo.
- mantenere comportamento attuale (non-breaking), chiarendo il contratto.

## FND-02 (P1) Prompt cancel progress ambiguo rispetto all azione

Evidenza:

- Prompt fisso: `Cancel this step and delete the created project? (y/N)` (`setup-scripts/lib/progress.ps1:59`)
- Reuse su contesti in cui la delete non e sempre semanticamente applicabile.

Impatto:

- Percezione rischio delete non voluta.
- Peggiora fiducia nel cancel path.

Raccomandazione:

- Parameterizzare il messaggio di cancel in `Show-ProcessProgress`.
- Default compatibile; copy specifico per:
- `cancel step only`
- `cancel + delete created project`

## FND-03 (P1) Drag-and-drop rapido non centralizzato

Evidenza:

- Prompt path distribuiti:
- `setup-scripts/commands/wizard.ps1:1013`
- `setup-scripts/commands/wizard.ps1:1117`
- `setup-scripts/commands/wizard.ps1:470`

Impatto:

- Flusso veloce meno consistente.
- Error handling non sempre omogeneo tra schermate.

Raccomandazione:

- Introdurre helper unico per input path (normalize/hint/retry/cancel).
- Stessa UX alta-level, meno attriti per uso ripetitivo.

## FND-04 (P2) UI coupling elevato nel wizard

Evidenza:

- Uso diffuso di `Read-Host`/`Write-Host` in `setup-scripts/commands/wizard.ps1`.
- Componenti `lib/menu.ps1` usati ma non come unico adapter.

Impatto:

- Refactor TUI piu costoso.
- Rischio regressione in fasi di migrazione.

Raccomandazione:

- Prima milestone tecnica: facade UI unica (adapter legacy-first).
- Reindirizzare progressivamente i prompt diretti verso API uniche.

## FND-05 (P2) Shortcut numeriche limitate nei menu lunghi

Evidenza:

- Numeric selection per singola cifra in `setup-scripts/lib/menu.ps1:316`, `setup-scripts/lib/menu.ps1:319`.

Impatto:

- Efficienza ridotta per liste >9 elementi.

Raccomandazione:

- Aggiungere multi-digit input buffering o indicare limite nel help.
- Backward-compatible.

## FND-06 (P2) Enter senza match nel filter menu puo propagare typo

Evidenza:

- `EnterReturnsFilterWhenNoMatch = $true` (`setup-scripts/lib/menu.ps1:347`)
- Return testo typed senza match (`setup-scripts/lib/menu.ps1:550`)

Impatto:

- Errori evitabili (poi respinti in validazione).

Raccomandazione:

- Conferma intermedia per input non matchato prima del submit finale.

## FND-07 (P2) Pause bloccanti frequenti rallentano happy-path

Evidenza:

- Esempi: `setup-scripts/commands/wizard.ps1:1111`, `setup-scripts/commands/wizard.ps1:1132`

Impatto:

- Riduce rapidita operativa per utenti esperti.

Raccomandazione:

- Conservare pause nei punti ad alto rischio.
- Ridurre pause ridondanti su esiti positivi.

## FND-08 (P2) Doppia pipeline render (menu custom + write-progress host)

Evidenza:

- Menu custom in `setup-scripts/lib/menu.ps1`
- Progress host in `setup-scripts/lib/progress.ps1` + `setup-scripts/commands/installer.ps1`

Impatto:

- Possibili differenze di resa tra host terminale.

Raccomandazione:

- Unified event model + renderer abstraction in fase progress refactor.

## Valutazione Finale Audit

- Bloccanti P0: nessuno emerso nello static audit.
- P1 da affrontare subito nel refactor: FND-01, FND-02, FND-03.
- P2 da pianificare in hardening: FND-04..FND-08.

Go/No-Go raccomandato:

- `GO con condizioni`: avviare implementazione solo con gate fase 1 (facade legacy) e test anti-regressione attivi.

