# Audit + Execution Plan E2E (Pre-Implementation)

Date: 2026-02-17
Status: In attesa approvazione owner
Implementation policy: `NO CODE IMPLEMENTATION` finche non arriva conferma esplicita

## 1) Scopo

Definire l esecuzione completa del refactor TUI in modo controllato:

- Prima audit tecnico strutturato.
- Poi implementazione incrementale su branch separato.
- Commit regolari e reviewabili.
- Qualita garantita da piano anti-regressione dedicato.

Riferimento test plan: `docs/_work/tui-refactor-plan/2026-02-17-anti-regression-test-plan.md`

## 2) Vincoli Operativi

- Nessuna implementazione prima del gate di conferma post-audit.
- Nessun push remoto automatico.
- Nessun cambio distruttivo su flussi business.
- Fallback `legacy` sempre mantenuto durante tutte le fasi.

## 3) Fase A - Audit Tecnico (obbligatoria, pre-codice)

## A.1 Audit Scope

Moduli focus:

- `setup-scripts/lib/menu.ps1`
- `setup-scripts/lib/progress.ps1`
- `setup-scripts/commands/wizard.ps1`
- `setup-scripts/commands/installer.ps1`
- `setup-scripts/vrc-setup-script.ps1`

## A.2 Audit Deliverable

1. Mappa completa chiamate UI:
- menu
- filter menu
- checklist
- prompt input
- confirm dialog
- progress updates

2. Mappa coupling UI/business:
- dove c e `Read-Host`/`Write-Host` fuori layer UI
- dove `Write-Progress` puo degradare output

3. Behavioral baseline:
- expected behavior per flow critico
- differenze per host terminale

4. Risk register iniziale:
- rischi tecnici
- impatto utente
- mitigazioni immediate

5. UX findings aggiuntivi (non-breaking):
- cancel path mancanti/incoerenti
- opzioni poco friendly nei menu complessi
- friction su drag-and-drop rapido

## A.3 Audit Output Files

- `docs/_work/tui-refactor-plan/audit/findings.md`
- `docs/_work/tui-refactor-plan/audit/call-map.md`
- `docs/_work/tui-refactor-plan/audit/risk-register.md`
- `docs/_work/tui-refactor-plan/audit/2026-02-18-ux-findings.md`

## A.4 Gate A - Decisione Owner (stop/go)

Implementazione parte solo dopo conferma su:

1. tecnologia adapter moderno (proposta: `Spectre.Console`)
2. naming branch
3. ordine fasi
4. criteri minimi di passaggio fase

## 4) Fase B - Implementazione Incrementale (dopo conferma)

## B.0 Branching e Commit Cadence

Branch dedicato proposto:

- `feat/tui-adapter-refactor`

Commit discipline:

- commit piccoli per blocco funzionale
- messaggi standard (`feat|refactor|docs|chore`)
- checkpoint regolare ogni mini-milestone

## B.1 Fase 1 - Facade UI Legacy-First

Obiettivo:

- introdurre layer `ui facade` senza cambiare UX.

Output:

- API facade stabile per menu/prompt/confirm/notice/progress.
- tutte le chiamate principali passano da facade.

Gate:

- comportamento invariato rispetto baseline.

## B.2 Fase 2 - Adapter Moderno (menu/input)

Obiettivo:

- implementare provider moderno per menu/filter/checklist/input.

Output:

- switch via config/env (`legacy` / `modern`).
- fallback automatico a `legacy`.

Gate:

- nessuna regressione sui flussi menu critici.

## B.3 Fase 3 - Progress Engine Strutturato

Obiettivo:

- unificare rendering progress step lunghi in pipeline stabile.

Output:

- progress events strutturati (step/phase/status/tail/cancel).
- rendering coerente anche in host degradati.

Gate:

- nessun overlap output nei passi Unity create/import/finalize.

## B.4 Fase 4 - Hardening e Cleanup UI Coupling

Obiettivo:

- ridurre `Read-Host`/`Write-Host` sparsi dove opportuno.
- uniformare prompt, errori e conferme.
- applicare miglioramenti UX non-breaking emersi da audit.

Focus UX hardening:

- semantica cancel consistente (`Esc` vs opzioni esplicite)
- copy contestuale per cancel nei processi lunghi
- path prompt unificato per drag-and-drop rapido
- riduzione pause non necessarie nei flow di successo

Gate:

- check anti-regressione verde.
- docs aggiornate.

## B.5 Fase 5 - Rollout Controllato

Obiettivo:

- mantenere `legacy` default iniziale.
- abilitare `modern` opt-in.
- eventuale switch default solo dopo stabilizzazione.

Gate:

- conferma owner post-feedback.

## 5) Piano Temporale Indicativo

Stima orientativa (dipende da scoperta audit):

1. Audit completo: 0.5-1.5 giorni
2. Fase 1 + Fase 2: 1-2 giorni
3. Fase 3: 1 giorno
4. Fase 4 + Fase 5: 0.5-1 giorno

Totale indicativo: 3-5.5 giorni lavorativi.

## 6) Rischi Principali + Mitigazioni

R1. Regressioni input tastiera tra host diversi

- mitigazione: dual-provider + fallback + matrix host nel test plan

R2. Output progress corrotto in host non VT friendly

- mitigazione: renderer unico + modalita plain + test host

R3. Aumento complessita del codice

- mitigazione: facade API minima + refactor per step + commit piccoli

R4. Dipendenze runtime nuove

- mitigazione: lock versione + fallback legacy always-on

## 7) Definition of Done E2E

- audit completato e approvato
- implementazione chiusa per tutte le fasi pianificate
- anti-regressione passata su flussi critici
- fallback legacy operativo
- documentazione aggiornata (`README`/`setup-scripts/README` + note migrazione)
- UX audit findings ad alta priorita chiusi o esplicitamente accettati in backlog.

## 8) Checkpoint Ufficiali con Owner

1. Fine Audit: conferma go/no-go implementazione
2. Fine Fase 1: conferma architettura facade
3. Fine Fase 2: conferma UX provider moderno
4. Fine Fase 3: conferma progress engine
5. Fine Fase 5: conferma rollout/default mode
