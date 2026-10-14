# UX Findings Addendum (Audit)

Date: 2026-02-18
Scope: audit aggiuntivo su cancel path, opzioni menu, drag-and-drop quick flow
Constraint: mantenere comportamento funzionale ad alto livello invariato

## Sintesi

L esperienza corrente e funzionale ma presenta attriti UX in 5 aree:

1. semantica cancel incoerente tra schermate
2. copy cancel non sempre contestuale
3. flussi drag-and-drop ripetuti/non uniformi
4. scorciatoie menu limitate in liste lunghe
5. pause bloccanti frequenti su flow di successo

## Findings Dettagliati

## UX-01 `Esc` non sempre coerente con messaggistica

Evidenza:

- Header principale: "Use arrows + Enter. ESC cancels." in `setup-scripts/commands/wizard.ps1:1148`
- Diversi confirm bloccano `Esc` (`AllowCancel $false`), esempio in:
- `setup-scripts/commands/wizard.ps1:1082`
- `setup-scripts/commands/wizard.ps1:1126`
- `setup-scripts/commands/wizard.ps1:1176`

Impatto:

- Confusione su cosa significhi "cancel" nelle diverse schermate.

Proposta non-breaking:

- Mantenere `AllowCancel $false` nei confirm distruttivi.
- Rendere esplicito nel copy: "Seleziona Proceed o Cancel (Esc disabilitato qui)".

Priority: High

## UX-02 Prompt cancel progress ambiguo sul delete

Evidenza:

- Testo fisso: "Cancel this step and delete the created project? (y/N)" in `setup-scripts/lib/progress.ps1:59`
- Lo stesso prompt viene riusato in contesti diversi dove delete non sempre applica.

Impatto:

- Timore di cancellazioni non desiderate.
- Perdita fiducia nel cancel path.

Proposta non-breaking:

- Copy contestuale via parametro (es. `CancelActionLabel`):
- "Cancel this step only?"
- "Cancel and delete created project?"
- Accettare anche conferme locali (`s/si`) oltre `y/yes`.

Priority: High

## UX-03 Drag-and-drop flow frammentato

Evidenza:

- Prompt separati e non uniformi:
- `setup-scripts/commands/wizard.ps1:1013`
- `setup-scripts/commands/wizard.ps1:1117`
- `setup-scripts/commands/wizard.ps1:470`

Impatto:

- Frizione in onboarding rapido.
- Recupero errori non sempre guidato allo stesso modo.

Proposta non-breaking:

- Centralizzare in una utility unica di path prompt:
- normalize + trim quote
- hint standard
- retry/abort coerenti
- eventuale riuso ultimo valore valido dove sensato

Priority: High

## UX-04 Shortcut numeriche limitate a 1 cifra

Evidenza:

- Numeric select usa solo un char digit in `setup-scripts/lib/menu.ps1:316`

Impatto:

- Liste lunghe meno efficienti (niente `10`, `11`, ...).

Proposta non-breaking:

- Aggiungere buffer numerico multi-cifra con timeout breve.
- Oppure mantenere 1..9 ma mostrare chiaramente limite nell help.

Priority: Medium

## UX-05 Inserimento libero da filter menu puo creare errore evitabile

Evidenza:

- `EnterReturnsFilterWhenNoMatch = $true` in `setup-scripts/lib/menu.ps1:347`
- ritorno testo libero su Enter senza match in `setup-scripts/lib/menu.ps1:550`

Impatto:

- typo accettati come package name e poi falliscono piu avanti.

Proposta non-breaking:

- mantenere quick path, ma con confirm intermedio:
- "No exact match. Use typed text '<x>'?"

Priority: Medium

## UX-06 Pause bloccanti frequenti (`Press ENTER`) su happy-path

Evidenza:

- esempi in `setup-scripts/commands/wizard.ps1:1111`, `setup-scripts/commands/wizard.ps1:1132`

Impatto:

- rallenta uso ripetitivo.

Proposta non-breaking:

- mantenere pause su errori/distruttivo
- ridurre pause su esiti successivi quando non necessarie

Priority: Medium

## Backlog Azioni Proposte (ordine)

1. Uniformare semantica e copy `Esc/Cancel` (UX-01, UX-02)
2. Introdurre prompt path centralizzato per drag-and-drop (UX-03)
3. Migliorare menu efficiency (UX-04, UX-05)
4. Ridurre pause bloccanti non necessarie (UX-06)

## Criterio di Compatibilita

Per ogni modifica UX vale il vincolo:

- stessa logica business
- stessi outcome funzionali
- fallback `legacy` sempre disponibile

