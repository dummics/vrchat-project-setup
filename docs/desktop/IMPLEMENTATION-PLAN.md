# VRCSETUP Desktop: piano GUI Windows e core condiviso

Data: 14 ottobre 2026. Stato: approvato da Dom per implementazione completa e commit/push sul branch dedicato. Le sezioni di baseline descrivono lo stato al momento dell'analisi; vedere PROGRESS.md per lo stato verificato di esecuzione.

## Decisione proposta

Realizzare VRCSETUP Desktop in C# con WinUI 3 e Windows App SDK stabile, su .NET 10. Trasferire la logica applicativa in un core C# condiviso con una CLI/TUI .NET. Conservare creazione da UnityPackage, pacchetti, preset, import, bulk editing e recovery dell'attuale tool. La GUI rende questi flussi più accessibili e mantiene i controlli avanzati disponibili.

Questa consegna contiene analisi, inventario e piano. Non sono stati creati branch, compilati prototipi, avviati Unity/VCC o modificati progetti, configurazioni e installazione. La conferma di Dom autorizzerà l'avvio del lavoro sul nuovo branch; l'intero sviluppo resta da eseguire e verificare.

## 1. Baseline e scope Git

- Repository canonico: C:\Users\domix\.scriptsdum\_unityprojectsetup.
- Repository pubblico indicato dalle fonti locali: dummics/vrchat-project-setup.
- Branch osservato: main, con upstream origin/main; HEAD 653a602.
- Modifiche preesistenti: setup-scripts/lib/progress.ps1, setup-scripts/lib/spectre.ps1, tests/Run-Tests.ps1.
- Directory non tracciate preesistenti: .vpm-validation-cache e docs/_work/tui-refactor-plan.
- Nessun project.json o AGENTS.md locale individuato nei percorsi pertinenti.
- Analisi del working tree attuale, senza incorporare le sue modifiche preesistenti. In particolare la gestione processi con Windows Job Object in progress.ps1 contiene lavoro non committato: va distinto dalla baseline stabile.
- Nessun fetch eseguito: lo stato remoto non è stato aggiornato; l'upstream locale non prova l'allineamento remoto attuale.

Dopo conferma: creare feature/winui-desktop in una worktree dedicata sotto .work/worktrees/, dalla baseline main verificata in quel momento. Se main cambia, confrontare lo scope prima di procedere. Non trasferire automaticamente i file sporchi; incorporare eventualmente solo cambiamenti già integrati e verificati. Il branch resta separato per sviluppo e review: nessun merge automatico in main o sostituzione dell'installazione quotidiana.

L'artifact di questo piano vive in .work/artifacts/, già esclusa da Git. Dopo approvazione, promuovere piano e matrice pertinenti nelle docs del nuovo branch, con commit mirati.

## 2. Breve ricerca sul Creator Companion

Ricerca sulle fonti ufficiali, non prova pratica della GUI installata. VCC è lo strumento ufficiale VRChat per creare e aggiornare progetti Unity; VPM è il suo gestore di pacchetti. [FAQ ufficiale](https://vcc.docs.vrchat.com/guides/faq/)

Il flusso documentato è: configurare Unity, entrare in Projects, scegliere New, selezionare un template Avatar/World o personalizzato, impostare nome e cartella, creare, gestire i pacchetti e aprire Unity. Per progetti esistenti si usa Add Existing Project e poi Manage Project. La gestione pacchetti espone versione installata, selezione di versione e aggiornamenti. [Package Manager](https://vcc.docs.vrchat.com/vpm/), [Getting Started](https://vcc.docs.vrchat.com/guides/getting-started/)

VCC supporta già template personalizzati: sono cartelle di progetto con metadata e dipendenze. Supporta anche repository community, aggiunti tramite URL o pulsante del creator. Quindi template e versioni precise non sono capacità esclusive di VRCSETUP. [Templates](https://vcc.docs.vrchat.com/vpm/templates/), [Community Repositories](https://vcc.docs.vrchat.com/guides/community-repositories/)

L'obiettivo UX dedotto dalla richiesta è conservare un modello familiare di libreria → progetto → pacchetti → Unity, facendo della creazione da UnityPackage e della selezione di preset un flusso unico. I vantaggi da valorizzare sono staging di molte modifiche, review unica, preset con pin/latest, sincronizzazione esatta esplicita, import aggiuntivi e controllo su gruppi di progetti.

### Confine del prodotto

Il nuovo client gestisce preparazione e pacchetti dei progetti. VCC/VPM restano integrazioni ufficiali. Non rientrano nello scope account VRChat, login, upload, conversione automatica dei materiali PC per mobile, sostituzione di Unity Hub o pubblicazione di repository. Registrare un progetto avviene tramite vpm add project, mantenendo VCC aperto. [VPM CLI](https://vcc.docs.vrchat.com/vpm/cli/)

### Preset, template e copia: termini distinti

- Preset pacchetti: mappa di package ID → versione o latest; esiste già.
- Selezione da progetto: copia i pacchetti installati o la policy salvata; esiste già. L'azione attuale Copy project non clona cartelle, scene o avatar.
- Template di creazione VRCSETUP: proposta aggiuntiva piccola, salvare una ricetta con preset, piattaforme, regole di nome e import aggiuntivi. Può lasciare il UnityPackage principale da scegliere ogni volta. Non contiene gli asset del progetto e non modifica il preset originale.
- Template di progetto VCC: cartella completa di partenza; non è attualmente una capacità del nostro motore. Integrazione e creazione senza UnityPackage restano una fase futura distinta, non una funzione già esistente né un requisito implicito per chiudere la parità.

La conferma di questo piano include le ricette di creazione; la clonazione completa di progetti resta fuori scope.

## 3. Inventario funzionale e matrice di parità

Tutte le righe A sono obbligatorie per considerare conclusa la migrazione. Le righe B indicano miglioramenti espliciti della GUI/core. I riferimenti commands/, lib/, maintenance/, bin/ e vrc-setup-script.ps1 sono relativi a setup-scripts/; tests/ è relativo alla root del repository. La matrice verrà aggiornata con stato ed evidenze reali durante l'implementazione.

| ID | Funzione o comportamento verificato nel codice/docs | Destinazione GUI/core | Fonte principale |
| --- | --- | --- | --- |
| A01 | Primo avvio, scoperta Unity, validazione Unity.exe e root progetti; gestione pacchetti su cartella diretta anche senza root configurata | Onboarding breve; prerequisiti verificati in base all'azione | commands/wizard.ps1: Invoke-FirstRunSetup, Setup-ProjectFlow; lib/config.ps1 |
| A02 | Libreria con scansione limitata di sottocartelle, esclusione di Assets/Library/cache, fingerprint e cache incrementale | Projects con ricerca, refresh e lista virtualizzata | lib/projects.ps1 |
| A03 | Nome, percorso, tipo Avatar/World/Unity/misto, Unity version, numero pacchetti, stato e data; fallback da package.json e csproj marcato come recuperato | Dati distinti tra canonici, recuperati, ignoti e invalidi | lib/projects.ps1: Get-VrcSetupProjectMetadata |
| A04 | Ricerca progetti e ordinamento recente/nome persistito, cartella fuori dalla root | Filtri e ordinamento visibili, aggiunta cartella | commands/wizard.ps1; lib/spectre.ps1; commands/cli.ps1 |
| A05 | Creazione da .unitypackage; suggerimento nome, personalizzazione, collisione destinazione e percorso finale | Drop/file picker, review unica, nessuna sovrascrittura implicita | commands/installer.ps1: Start-Installer; commands/wizard.ps1 |
| A06 | Naming: prefisso, suffisso, regex di rimozione, nomi ricordati per file, ultimo nome/percorso; nomi riservati Windows | Settings e modifica nome nel flusso di creazione | lib/config.ps1; lib/utils.ps1: Test-VrcSetupProjectName; commands/wizard.ps1 |
| A07 | Defaults e preset creator, scelta esatta del set iniziale senza ereditare optional esclusi; customization limitata al nuovo progetto | Selettore preset e editor prima della creazione | lib/presets.ps1; commands/wizard.ps1 |
| A08 | Piattaforma Unity attiva distinta da piattaforme SDK pubblicazione; Windows, Android/Quest, iOS, selezioni multiple | Controlli separati e review esplicita | lib/import-platform.ps1; commands/installer.ps1; lib/unity/VrcSetupPostImport.cs |
| A09 | Suggerimento Android solo per parola Quest/Android, override; controllo moduli build prima della creazione; progetti esistenti conservano piattaforma | Preflight con errore azionabile e link Unity Hub | lib/import-platform.ps1; tests/Run-ImportPlatformTests.ps1 |
| A10 | Preparazione NUnit/Test Framework, VPM prima degli asset, import principale seguito dagli extra senza duplicare il principale | Pipeline core unica, fasi visibili | lib/utils.ps1: Install-NUnitPackage; commands/installer.ps1 |
| A11 | Import/finalizzazione nella stessa sessione Unity, loop editor vivo, domain reload, attesa compilazione e timeout; settle aggiuntivo solo dopo repair | Orchestratore Unity con evidenza di completamento | lib/unity/VrcSetupPostImport.cs; commands/installer.ps1 |
| A12 | Import extra configurati da cartella, default disabilitato; percorsi relativi risolti secondo la base prevista; extra anche su progetto esistente | Lista ordinata e preview dei file realmente importati | commands/installer.ps1; commands/wizard.ps1 |
| A13 | Registrazione progetto riuscito in VCC senza restart; fallimento registrazione è warning e preserva il progetto | Risultato completo con warning ed eventuale retry esplicito | commands/installer.ps1: Register-VrcSetupCreatorCompanionProject |
| A14 | Workspace pacchetti unificato singolo/bulk, add/update/remove in staging; installazione solo al salvataggio | Una tabella con versione installata, scelta e azione prevista | lib/bulk-packages.ps1; commands/wizard.ps1 |
| A15 | Ricerca con nomi leggibili e ID, selezione singola/multipla, pacchetti già inclusi, empty/error states | Catalogo con ricerca e aggiunta al piano | lib/vrcget.ps1; lib/package-ui.ps1; lib/package-catalog.ps1 |
| A16 | Versioni SemVer, ricerca per versione/prefisso, prerelease opzionali, inserimento esatto, pin/latest, cache di sessione | Picker versione avanzato, controllo inline per uso normale | lib/utils.ps1; lib/vpm.ps1; lib/vrcget.ps1; commands/wizard.ps1 |
| A17 | Base e SDK Avatar/World collegati, intersezione versioni; foundation protetta, resolver richiesto; optional rimovibili | SDK presentato come intenzione unica, vincoli applicati nel core | lib/vpm.ps1: Resolve-VrcSetupSdkPackageSet; lib/config.ps1 |
| A18 | Prerelease esatte tramite vrc-get, install/upgrade/downgrade scelto dalla versione risolta; stabile tramite percorso VPM | Adapter coerente con strategia attuale | commands/installer.ps1: Get-VrcSetupVrcGetPackageAction |
| A19 | Lettura dipendenze dirette e locked richiesti; versioni manifest stringa/oggetto; transitive non promosse indiscriminatamente | Editor separato dai dettagli dipendenze; validazione finale | lib/vpm.ps1: Get-VpmProjectPackageSet; commands/installer.ps1 |
| A20 | Skip versioni esatte già presenti, anche come dipendenza; latest richiede aggiornamento | Piano con differenza tra policy e versione risolta | commands/installer.ps1: Install-PackagesInProject |
| A21 | Defaults separati da preset nominati; creare, editare, salvare/aggiornare, eliminare, annullare; salvare set da progetto | Packages & presets con working copy | lib/presets.ps1; commands/cli.ps1 |
| A22 | Applicazione preset overlay conserva custom; exact sync rimuove optional fuori set dopo review | Modalità Keep custom/Match exactly esplicite | lib/bulk-packages.ps1; lib/presets.ps1 |
| A23 | Favoriti persistenti e quick-add senza nuova interrogazione di tutti i repository | Favorites nel catalogo | lib/config.ps1; commands/wizard.ps1 |
| A24 | Registro noto da defaults, preset, favoriti, ricerche e progetti; refresh background annullabile, indisponibili mantenuti, scelte utente preservate | Catalogo immediato con aggiornamento asincrono | lib/package-catalog.ps1 |
| A25 | Profilo .vrcsetup/package-profile.json: preset, policy, installato e definizione; rilevamento modifiche esterne e preset cambiato | Stato di provenienza leggibile senza auto-sync | lib/package-catalog.ps1: Save/Get-VrcSetupProjectPackageProfile |
| A26 | Bulk selezione progetti, versioni miste conservate finché non scelte; target diversi per singolo pacchetto | Editor bulk con gruppo progetti per riga | lib/bulk-packages.ps1 |
| A27 | Bulk da preset/reference project, set installato o policy salvata; undo degli intenti | Riutilizza set e Reset pending changes | lib/bulk-packages.ps1 |
| A28 | Review per progetto; progetti aperti o cambiati dopo review saltati; errore singolo non interrompe gli altri; cancel ferma il resto | Preview espandibile e esiti Saved/Unchanged/Skipped/Failed/Cancelled | lib/bulk-packages.ps1: Invoke-VrcSetupBulkPlan |
| A29 | Report JSON bulk con progetti non processati e log per progetto | Riepilogo ed esportazione | lib/bulk-packages.ps1: Write-VrcSetupBulkReport |
| A30 | Backup manifest prima dei batch; ripristino e resolve su failure; verifica metadata su disco | Backup service e recovery con esito parziale esplicito | commands/installer.ps1: Restore-VrcSetupPackageBatch, Get-VpmManifestValidationResult |
| A31 | Easy Login: repository Vulpine Vault quando necessario, legacy Assets/EASY LOGIN e meta fuori da Assets in backup verificato | Migrazione mostrata nella review e gestita dal core | commands/installer.ps1: Move-LegacyEasyLoginPackage; lib/vpm.ps1 |
| A32 | Legacy package dichiarato ma già assente: messaggio informativo con verifica metadata; output grezzo conservato | Warning/error basati su esito e postcondition, log tecnico fedele | commands/installer.ps1: Write-VrcSetupCommandOutput |
| A33 | Stato schemaVersion/steps/completed; individuazione incomplete; retry/edit mantenendo scelte; cleanup con conferma e gestione cartella in uso | Activity & recovery, nessuna eliminazione implicita | lib/project-state.ps1; commands/wizard.ps1; commands/installer.ps1 |
| A34 | Cancellazione Q/Esc, log timestamped, progress scorrevole, dettagli finali; processi Unity dell'operazione | Cancel/Retry, activity drawer e log consultabile | lib/progress.ps1; lib/spectre.ps1 |
| A35 | CLI projects/packages/presets/create/setup/help, Json/DryRun/Test/Refresh/Sort/Name/Package/Preset; repair/uninstall e --yes | CLI/TUI .NET e launcher compatibili | commands/cli.ps1; vrc-setup-script.ps1; bin/vrcsetup.cmd |
| A36 | Percorsi assoluti/relativi, quote/drag-drop, env vars, spazi, parentesi quadre, &, accenti; CWD esterna | Normalizzatore unico con base esplicita | commands/cli.ps1; commands/wizard.ps1; tests/Run-Tests.ps1 |
| A37 | Portable/installato, quattro BAT leggibili, marker install, alias PATH utente, shortcut, repair/uninstall, preservazione config; DLL bloccata accettata solo se hash identico | Lifecycle condiviso e payload versionato | maintenance/*.ps1; tests/Run-Tests.ps1 |
| A38 | VPM mutex globale/retry lock, cache repository VCC con lettura sicura e fallback vrc-get | Tool runner e lock interprocesso | lib/vpm.ps1: Invoke-VpmCapture, Read-VccRepoJsonSafe; lib/vrcget.ps1 |
| B01 | Ricette di creazione nominabili da salvare e riutilizzare | Templates di setup; schema separato dai preset | Nuovo, approvazione tramite questo piano |
| B02 | GUI asincrona, theme, keyboard/accessibilità, DPI, drop files, attività persistente consultabile | WinUI 3 e MVVM | Nuovo client |
| B03 | Blocchi e controllo stato estesi anche a operazioni singole, manifest fingerprint prima dell'apply, config scritta atomicamente | Invarianti core comuni a tutti i client | Rafforzamento dei controlli esistenti |

## 4. Architettura proposta

### Componenti e dipendenze

~~~text
VrcSetup.Desktop (WinUI 3, MVVM) ─┐
                                ├─> VrcSetup.Core ─> porte per file/processi/cataloghi
VrcSetup.Cli (CLI + TUI) ─────────┘           ↑
                                 VrcSetup.Infrastructure
                                 file/config/VPM/vrc-get/Unity/Windows

VrcSetup.UnityBridge → sorgente Editor compatibile con Unity, eseguito da Unity
~~~

Struttura iniziale proposta, con responsabilità precise:

~~~text
src/
  VrcSetup.Core/             modelli, validazione, planning, use case
  VrcSetup.Infrastructure/   config/cache, processi, tool esterni, Windows
  VrcSetup.Cli/              parser comandi, JSON, Spectre.Console TUI
  VrcSetup.Desktop/          WinUI, viewmodel, navigation, theme
  VrcSetup.UnityBridge/      sorgente Editor e schema dei file di scambio
tests/
  VrcSetup.Core.Tests/
  VrcSetup.IntegrationTests/
  VrcSetup.Cli.ContractTests/
  VrcSetup.Desktop.Tests/    solo comportamento testabile dei viewmodel
docs/                       architettura, migrazione, uso e verifiche
setup-scripts/              tool PowerShell corrente durante migrazione
.work/artifacts/            payload, prove e screenshot non versionati
~~~

Core net10.0, privo di dipendenze WinUI/Spectre e di prompt. Desktop con target Windows compatibile con WinUI e net10; adapter Windows isolati senza promettere portabilità multipiattaforma. CommunityToolkit.Mvvm per viewmodel/commands; System.Text.Json per dati; logging/progress strutturati e DI solo al composition root. Parser CLI mantenuto semplice e selezionato dopo lo spike sui contratti esistenti. Nessun server HTTP, servizio residente, database o plugin system richiesto.

Le UI chiamano gli stessi use case, non si chiamano a vicenda e non eseguono gli script come motore permanente. Il core possiede regole di preset, SDK, diff, bulk, naming e completamento; Infrastructure fornisce I/O e integrazioni. Eventuali facade servono un caso reale, senza duplicare planner o resolver.

### .NET 10 e WinUI

.NET 10 è LTS, con supporto indicato fino al 14 novembre 2028. Nell'ambiente sono già presenti SDK 10.0.112 e 10.0.401. La presenza degli SDK non prova ancora la build WinUI: toolchain XAML, Windows SDK, restore e packaging saranno verificati nella fase zero. [Policy .NET](https://dotnet.microsoft.com/en-us/platform/support/policy)

WinUI 3 è il framework UI nativo Windows scelto per il brief. Usare il Windows App SDK stabile e supportato al momento della build, con versione esatta fissata nel repository; la pagina Microsoft consultata elenca 2.5.1 come stabile al 14 ottobre 2026. Non basarsi su preview/experimental. [WinUI 3](https://learn.microsoft.com/en-us/windows/apps/winui/winui3/), [Canali Windows App SDK](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/stable-channel)

Prima release Windows x64, progettata per Windows 11; mantenere compatibilità Windows 10 nei limiti dell'intersezione supportata da .NET, WinUI e prerequisiti VRChat, dichiarando una build minima concreta dopo lo spike. Supporto ARM64 e build multipiattaforma non richiesti.

### Confine Unity/VPM

Il sorgente Editor Unity non può dipendere dalla runtime .NET 10 del desktop: deve restare compilabile dal relativo Unity Editor. Il bridge condivide schemi/file di comando ed esito, non DLL net10. Preservare SessionState, domain reload e controllo compilazione del bridge attuale.

Per VPM e vrc-get mantenere inizialmente i processi ufficiali tramite adapter C# con argv, stdout/stderr, timeout e result tipizzati. Non riscrivere l'ecosistema di dependency resolution. La documentazione VPM CLI indica .NET 8 come prerequisito: controllare separatamente runtime/tool installato; il publish self-contained dell'app non rende automaticamente self-contained VPM. [CLI ufficiale](https://vcc.docs.vrchat.com/vpm/cli/)

Nessuna installazione globale o aggiunta repository durante una semplice ricerca. La registrazione repository richiesta da una reale installazione deve essere esplicita nel piano. La cache VCC è letta tramite adapter; la lista progetti ufficiale si aggiorna con VPM, evitando editing diretto del settings file VCC.

## 5. Contratto operativo condiviso

Il modello fondamentale è Read → Stage → Plan → Review → Apply → Verify → Report.

- ProjectSnapshot: metadata, dipendenze dirette, locked necessari, Unity version, piattaforma, fingerprint e stato busy/incomplete.
- PackageSelection: policy latest/pin, origine preset/project/manuale e target progetti. La versione installata è distinta dalla policy richiesta.
- OperationPlan immutabile: input normalizzati, baseline, versioni risolte, aggiunte/cambi/rimozioni per progetto, import ordinati, precondizioni, backup e side effect esterni previsti.
- Execution: apply dello stesso piano da CLI e GUI, cancellation token, eventi tipizzati per fase/progetto, warning e risultato.
- OperationResult: Completed, CompletedWithWarnings, Failed, Cancelled, Skipped, Unchanged; dettagli su recupero, log, backup e progetti non processati. I client mappano questi stati nei contratti esistenti senza confonderli con i codici CLI.

### Invarianti

1. Nessuna modifica ai progetti prima di Apply. Dry-run può consultare rete e usare cache/log/scratch locali dichiarati, ma non creare il progetto finale, modificare manifest, importare asset, registrare repository/progetti VCC o cancellare cartelle.
2. Fondazione SDK coerente con tipo progetto e versioni compatibili; optional rimovibili. Contenuti World già presenti non autorizzano una conversione silenziosa in Avatar.
3. Alla review risolvere latest e mostrare la versione concreta; all'apply applicare quella scelta. Se metadata/config/snapshot invalidano il piano, richiedere nuova review anziché scegliere versioni diverse in silenzio.
4. Prima di ogni mutazione controllare progetto aperto/lock e fingerprint dei file pertinenti; applicare il controllo anche fuori dal bulk. In bulk skip con motivo e continuazione per gli altri.
5. Lock per progetto canonico tra istanze GUI/CLI; serializzare operazioni VPM che condividono stato e rispettare i lock delle versioni legacy. Il lock VRCSETUP non impedisce a Unity/VCC di scrivere: verifica esterna prima della mutazione e postcondition dopo.
6. Backup riuscito come precondizione della mutazione. Se il rollback manifest+resolve è incompleto, riportare recovery necessario. Manifest backup non equivale al backup completo degli asset.
7. Per import su progetto esistente predisporre un punto di ripristino coerente con i file importati prima dell'esecuzione; se non si può garantire recuperabilità, esplicitare il limite e bloccare il percorso automatico fino a una scelta consapevole. Non promettere annullamento atomico delle modifiche Unity.
8. Cancel termina solo i processi avviati dalla nostra operazione e ferma la coda. Conserva default progetto/log/checkpoint; eliminazione del nuovo progetto parziale è un'azione separata, con destinazione e conseguenza visibili. Non applicare il callback legacy di cancellazione cartella senza una scelta esplicita.
9. Un problema durante import/compilazione/finalizzazione lascia incomplete. Registrazione VCC fallita non trasforma la creazione verificata in fallimento né cancella il risultato.
10. Avvio background senza console/flash per tool child. Argomenti tramite ProcessStartInfo.ArgumentList, niente concatenazioni shell. La CLI mantiene output testuale/JSON coerente con redirect.
11. Normalizzare i path una sola volta con base esplicita; verificare containment, reparse point e cartella marker prima di move/delete. Cleanup solo su progetti scelti e confermati; niente eliminazioni per solo nome simile.
12. Config e dati condivisi scritti atomicamente, con lock/revisione; refresh catalogo aggiorna metadata ma conserva le scelte più recenti dell'utente.

Il retry non significa resume cieco di qualsiasi import. Il journal registra fase e singoli import; prima di riprendere va riconciliato con filesystem e stato Unity. Per operazioni non idempotenti, offrire verifica/repair o ripartenza consapevole. Lo stato legacy oggi individua incomplete e offre cleanup/retry: un resume arbitrario non è già implementato.

## 6. UX della GUI

Stile Windows Fluent, dark/light/system, titolo e sidebar nativi, controlli leggibili e focus visibile. Palette propria di VRCSETUP; il riferimento VCC è il flusso, non una replica grafica o tecnica. Testi di prodotto inizialmente in inglese come il tool corrente, documentazione per Dom in italiano. Nessun sottotitolo generico, titolo duplicato o spiegazione sistematica di controlli evidenti.

### Navigazione principale

- Projects: lista ricercabile con stato, Unity, tipo e data. New project e Add existing visibili. Selezione multipla attiva l'editor bulk.
- Packages & presets: defaults, preset creator, favoriti, registro noto e ricette di creazione.
- Activity: operazioni e recovery; disponibile anche come drawer mentre si lavora.
- Settings: Unity/root progetti; naming ed import extra nei relativi gruppi; lifecycle dell'app in sezione dedicata.

### Creare un progetto

1. Trascinare/selezionare UnityPackage oppure scegliere una ricetta di creazione.
2. Nella stessa schermata: nome proposto e cartella finale, preset, piattaforma attiva e piattaforme publishing. Dettagli pacchetti/import extra apribili quando servono.
3. Review esatta di ciò che verrà creato/installato/importato, moduli build verificati e side effect VCC.
4. Create con progress per fasi, cancel e log consultabile senza navigare fuori dal flusso.
5. Risultato: progetto pronto, warning eventuali, Open in Unity/Open folder/Show log. Apertura Unity solo tramite azione dell'utente.

Non sono necessari wizard lunghi per riconfermare defaults già validi. Errori e collisioni devono comparire vicino al campo. Overwrite non è la soluzione predefinita: proporre altro nome o gestire la destinazione con conseguenza esplicita.

### Gestire un progetto o molti progetti

Tabella con nome pacchetto, versione installata, scelta e azione pending. Per uso normale l'ID tecnico è secondario; resta copiabile nei dettagli. Versioni miste nel bulk non vengono appiattite automaticamente. Search/Add, preset, favorites e copy package selection operano sullo stesso working set.

Apply changes rimane un'azione unica. Review aggregata con dettagli per progetto; exact sync rende visibili le rimozioni. I target per pacchetto aprono un selettore dei progetti coinvolti. Lasciare la schermata con modifiche pending permette di conservarle o scartarle. Dopo failure restano selezioni, piano e log per retry/edit.

### Accessibilità e prestazioni

Keyboard completo, Tab order, screen reader, high contrast, errori non affidati al solo colore. Verifica a 1366×768 e DPI 100/125/150/200%, resize senza perdere azioni. Tabelle virtualizzate, ricerca locale immediata, nessun refresh rete bloccante all'apertura delle liste.

Progress reale per fasi e conteggi; Unity texture/shader import usa stato indeterminato quando non esiste una misura affidabile. Non inventare percentuali o tempi residui. Log aggiornato incrementale leggendo solo modifiche, buffer UI limitato e log completo su disco.

## 7. CLI e migrazione dati

### CLI/TUI finale

La CLI rimane prodotto di prima classe. Portare projects, packages list/search/add/remove, presets list/save/remove/apply, create, setup, help, repair e uninstall. Preservare flag PowerShell esistenti come alias riconosciuti, positional arguments e comportamento dei launcher. Aggiungere soltanto le opzioni necessarie a esporre i nuovi casi condivisi: piattaforme, exact preset, bulk e recipe, con help e dry-run.

vrcsetup senza argomenti continua ad aprire una TUI; la GUI parte dal collegamento Windows/BAT o da vrcsetup gui. In input/output rediretto evitare prompt e controlli terminale; azioni che richiedono interazione falliscono con istruzione utile. Conservare --yes per uninstall unattended e la conferma interattiva.

Compatibilità da bloccare con test prima del porting: schema JSON array e campi, DateTime UTC, output senza decorazioni su -Json, codici per successo/failure/cancel e ricerca senza risultati. Oggi il valore 2 compare sia come no-results in packages search sia come cancel nell'installer: preservare la semantica per comando, usando stati interni distinti. Non introdurre un nuovo schema o nuovi exit code senza migrazione documentata.

Windows PowerShell 5.1 e PowerShell 7 devono poter invocare lo stesso vrcsetup .NET. Nel prodotto finale PowerShell non contiene un secondo motore di setup.

### Migrazione incrementale

Durante lo sviluppo il tool PowerShell corrente resta funzionante nel checkout/installazione stabile. I nuovi binari usano un nome/percorso esplicito di preview. Non si aggiorna l'alias quotidiano fino alla parità verificata e alla decisione di promozione.

Ogni famiglia di operazioni viene trasferita una sola volta in C#: prima planner/lettura, poi executor. GUI e CLI nuove usano fin dall'inizio lo stesso C# core per i percorsi già portati. I percorsi ancora legacy restano dichiarati incompleti nella matrice; una milestone intermedia non chiude il mandato di tutte le feature.

Non serve un bridge PowerShell permanente. Un adapter temporaneo può essere usato solo per una verifica delimitata e poi rimosso insieme ai caller nello scope; non diventa una dipendenza finale della GUI.

### Configurazione e stato

- Importare vrcsetup.json legacy preservando defaults, RequiredPackages, FavoritePackages, PackagePresets, Naming, SavedProjectNames, ultime scelte, sort e UnityPackagesFolder. Supportare VpmPackages in formato array o mappa.
- Config finale condivisa da CLI e GUI installate sotto %LOCALAPPDATA%\VrcSetup\; portable in directory data/ accanto al payload. Origine legacy rilevata e migration preview verificabile, backup prima di scrivere.
- Conservare la semantica dei percorsi relativi legacy: risolverli con la vecchia base e migrare un valore equivalente, senza reinterpretarli dalla nuova directory.
- Separare dati durevoli (config/preset/ricette), cache rigenerabili (projects/catalog), logs e installation metadata. Il registro noto conserva le selezioni salvate anche quando si aggiorna la cache metadata.
- Schema versionato per nuovi dati; lettura e riconciliazione di .vrcsetup/state.json, package-profile.json e platforms.json esistenti. File invalidi non riscritti silenziosamente.
- Profile policy conserva latest e pin mentre installed descrive versioni risolte; preset modificato genera informazione, non sincronizzazione automatica.
- Preview isolata da config installata durante sviluppo e test; condivisione intenzionale solo dopo migrazione verificata. Nessuna scrittura a progetti reali per provare la migrazione.

## 8. Distribuzione e manutenzione

Prima consegna distribuibile: ZIP Windows x64 con GUI, CLI e dipendenze .NET/WinUI self-contained, verificato fuori dalla macchina di sviluppo. Deve funzionare senza SDK .NET o Visual Studio sul PC del creator; Unity/VCC/VPM restano prerequisiti distinti. Portable unpackaged più installer per utente è il candidato iniziale. MSIX/firma/Store restano scelte successive. [Deployment Windows App SDK](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/deploy-unpackaged-apps)

Conservare i quattro BAT root e il flusso cliccabile. Install sotto %LOCALAPPDATA%\Programs\VrcSetup, Start menu e PATH utente, marker versione/payload. Update/repair validano payload e hash, preservano dati utente e gestiscono binari bloccati senza sovrascritture parziali. Uninstall non elimina progetti Unity e distingue dati da conservare e programma.

Il source ZIP pubblico deve restare utilizzabile durante tutta la transizione. Alla promozione .NET, documentare chiaramente run/build da sorgente e download del payload compilato; non fare un launcher che scarica o sostituisce binari arbitrari in silenzio. Il passaggio dall'attuale source ZIP pronto all'uso al nuovo rilascio è un criterio di verifica esplicito, da risolvere prima di cambiare il default pubblico.

Durante sviluppo installare eventuale preview in destinazione separata con shortcut distinta. Non toccare la copia VRCSETUP giornaliera prima della promozione approvata. Licenze di Spectre/vrc-get/runtime e dipendenze incluse nel payload.

## 9. Fasi di implementazione e criteri di uscita

| Fase | Outcome | Verifica richiesta prima di procedere |
| --- | --- | --- |
| 0 — Baseline e fattibilità | Branch/worktree, docs approvate, contratti attuali acquisiti; scaffold minimo WinUI + CLI net10 e publish | Build, app nativa, CLI da PS5.1/PS7 e smoke portable self-contained in percorso esterno. Nessun cambio al tool installato |
| 1 — Fondazione comune | Modelli, path/config migration, scanner/cache, catalogo, preset/profili e planner | Fixture realistiche e golden contract CLI; config/pin/percorsi conservati; no-write ai progetti durante planning |
| 2 — Primo flusso completo | Progetto esistente: lettura, add/version/remove, review, apply, verifica e report dal medesimo core | GUI e CLI producono lo stesso diff/esito; prova VPM su progetto disposable, failure/rollback/cancel/busy |
| 3 — Creazione/import | UnityPackage, NUnit/VPM, naming, piattaforme, extra, bridge Unity, finalize, postcheck e registrazione VCC | Creazione reale da entrambe le superfici su progetti diversi ma equivalenti, metadata/asset/log, errori e incomplete |
| 4 — Parità avanzata | Bulk con target per riga, preset overlay/exact, reference policy, favorites/catalog refresh, recovery e ricette | Tutte le righe A funzionali; test misti PC/Quest/World, snapshot cambiato, unavailable repo, cancellation e report parziali |
| 5 — Prodotto Windows | UX rifinita, accessibilità, perf e lifecycle install/update/repair/uninstall | Prova del payload scaricato/estratto in cartella diversa, PC/VM senza SDK, path speciali, config preservata, zero console GUI |
| 6 — Chiusura migrazione | Nuovi launcher/CLI come entrypoint, docs aggiornate, motore duplicato ritirato dopo verifica | Matrice A/B conclusa con prove; legacy source ancora coerente; review del risultato sul branch prima della promozione |

La migrazione non è una conversione meccanica di file .ps1: regole di dominio, interazione e tool invocation sono oggi mescolate anche dentro wizard/installer. La sequenza sopra serve a trasferire comportamento senza duplicarlo nelle due UI. Non assegno una scadenza fittizia prima dello spike: la maggiore variabile è import Unity e packaging, non la costruzione della sidebar.

## 10. Piano di verifica

Le suite esistenti sono evidenza dei comportamenti da coprire, non prove già eseguite sul nuovo sistema. In questa fase di pianificazione non sono state lanciate, perché alcune installano/disinstallano e creano dati di test.

### Test proporzionati

- Unit/fixture: naming e Windows path, SemVer/prerelease, SDK comune Avatar/World, protected foundation, preset new/overlay/exact, required locked, policy distinta da installed, bulk targeting/versioni miste, migration schema/path.
- Contract CLI: comandi, alias flag, JSON array anche vuoto/singolo, stdout/stderr, exit code per comando, redirect, invocazione da PS5.1 e PS7 e CWD diversa. Bloccare i contratti da script attuale prima di riscriverli.
- Integration con tool runner controllato: file lock/retry, VPM exit zero con error output, legacy removal già assente, backup impossibile, rollback/resolve incompleto, cancellation, crash e progetti cambiati dopo review. Queste prove coprono percorsi di errore senza rischiare avatar reali.
- E2E reali su disposable: GUI e CLI, creazione/import e add/update/downgrade/remove con versioni stabili/esatte, SDK linked, Easy Login legacy, extra package e post-import repair. Unity log/manifest/package.json/state verificati, non solo exit zero.
- Piattaforme: Windows e Android con moduli presenti; iOS almeno validazione/missing-module e selezione publishing. Nessuna pretesa di build iOS completa da Windows; controlli e preparazione dipendono dalle capacità Unity effettive.
- Recovery: processo chiuso durante creazione/import, checkpoint incoerente, import failure e compile failure, incomplete conservato, retry riconciliato e cleanup esplicito con path verificato.
- Bulk: progetti PC/Quest/World con custom diversi, pin, target differenti, busy, cambiato, fallito e invariato; report NotProcessed dopo cancel.
- GUI reale: schermate principali, focus/keyboard, dialog, pending changes, error/empty state, resize/DPI e screen reader; screenshot come evidenza. Usare UI Automation/desktop tool per WinUI nativo, non presumere DOM Playwright.
- Release: ZIP/payload effettivo, portable da percorso speciale, primo avvio offline con dati locali, install/upgrade/repair/uninstall in sandbox; niente SDK utente, config identica salvo migration prevista, BAT/CLI/shortcut coerenti.

### Suite attuali da tradurre o mantenere durante migrazione

Run-Tests.ps1; Run-UnifiedPackageEditorTests.ps1; Run-PackageWorkflowTests.ps1; Run-BulkPackageTests.ps1; Run-CreatorPresetTests.ps1; Run-ImportPlatformTests.ps1; Run-ImportPerformanceTests.ps1; Run-LegacyCleanupFeedbackTests.ps1; Run-NavigationUiTests.ps1; Run-ProgressLayoutTests.ps1; test-validation.ps1.

Conservare test comportamentali e fixture. I test che cercano stringhe nel sorgente TUI non sono contratti dell'engine: sostituirli con verifiche di risultato/state/interaction, senza pretendere gli stessi tasti o layout nella GUI.

## 11. Rischi reali e decisioni

| Rischio | Gestione |
| --- | --- |
| Perdere regole nascoste nel wizard | Matrice A01–A38, baseline contratti e porting per flusso completo |
| Riscrivere un resolver SDK/VPM divergente | Adapter dei tool esistenti; postcondition su disco; SDK linking nel core |
| .NET 10 desktop confuso con runtime Unity o VPM | Tre confini separati e verifica prerequisiti per tool |
| Progetti aperti o modificati dopo preview | Lock, busy check, fingerprint e nuova review/skip |
| Crash o cancel durante import non reversibile | Journal + checkpoint, backup appropriato, niente falso rollback atomico |
| Cache/background refresh sovrascrive pin/latest | Metadata separati da policy, revision check e merge delle selezioni correnti |
| Windows packaging non portabile | Spike iniziale e prova self-contained fuori macchina di sviluppo |
| Pubblico source ZIP perde usability | Vecchio percorso conservato fino a soluzione build/release/launcher verificata |
| Cambi locali TUI inglobati involontariamente | Worktree separata e confronto baseline, nessun reset/stash automatico |

Scelte ordinarie già proposte: C#/.NET 10, WinUI 3 nativo, core unico, x64, GUI e TUI entrambe di prima classe, branch separato, inglese nel prodotto, modalità portable/per-user, ricette di creazione piccole.

Non serve scegliere ora framework aggiuntivi, cloud, database, telemetry o account. Eventuali cambi materiali emersi nello spike verranno presentati con evidenza; per le normali scelte implementative si procede autonomamente dopo conferma.

## 12. Definition of done e prossimo passo

Completamento significa tutte le funzioni A migrate, miglioramenti B verificati, entrambe le UI alimentate dal core C#, CLI usable e contratti conservati, creazione/import reali riusciti, recovery/partial failure onesti, payload Windows installabile/portable verificato, docs e licenze aggiornate, branch con commit mirati ed eccezioni Git documentate.

Non basta una shell GUI, un mockup, una milestone funzionante o un wrapper dei vecchi script. Non si dichiara conclusa la task con pacchetti, bulk, lifecycle o Unity E2E ancora mancanti.

**Prossimo passo dopo conferma di Dom:** verificare baseline, creare feature/winui-desktop nella worktree dedicata, promuovere questa specifica nelle docs del branch e iniziare la fase 0. Nessuna promozione sul main pubblico o installazione quotidiana fino alla review del risultato.
