# Rilevamento affidabile delle dipendenze per VRC Project Setup

## Raccomandazioni prioritarie e trade-off

**Data di consultazione: 28 ottobre 2026.** Ho potuto leggere direttamente il repository e il branch `feature/winui-desktop`. Il branch consultato risultava a `19bf51f740b65396a9c66e82232cb198f3573079`, commit `chore(release): 0.2.6-beta.2`, datato **27 ottobre 2026, 10:56:33 UTC**. fileciteturn26file0L2-L7 fileciteturn26file0L30-L33

La conclusione principale è positiva: **non partirei da zero e non sostituirei il detector esistente**. Il branch ha già quasi tutti i mattoni giusti: scansione locale bounded, parsing `gzip+tar`, grafo di riferimenti GUID, distinzione `Declared / Detected / Possible / Included`, indice firme con provenance, cache, deep scan, indexing esplicito dei package installati, salvaguardia delle selezioni esistenti e delega delle operazioni effettive a `vrc-get`. fileciteturn7file0L2-L2 fileciteturn8file0L2-L2 fileciteturn9file0L2-L2

Le priorità che consiglierei sono queste:

1. **Correggere prima di tutto la distinzione UPM vs VPM nei manifest osservati.** Oggi `UnityPackageInspector.ParseManifest` fonde sia `dependencies` sia `vpmDependencies` nello stesso dizionario. Nel formato VPM, invece, `vpmDependencies` identifica le dipendenze VPM; `dependencies` conserva la semantica UPM di Unity. VRChat documenta espressamente `vpmDependencies` come campo delle dipendenze VPM, mentre Unity definisce `dependencies` come mappa di package UPM con versioni specifiche. fileciteturn7file0L2-L2 citeturn27view0 citeturn29view2  
   Questo non è teorico: il `package.json` corrente di Modular Avatar contiene `com.unity.nuget.newtonsoft-json` in `dependencies`, ma NDMF in `vpmDependencies`. Il modello attuale può quindi classificare un package UPM come se fosse un consiglio VPM installabile. fileciteturn19file0L2-L2  
   **Trade-off:** una piccola modifica al modello dati evita una classe importante di falsi positivi.

2. **Separare completamente “identità rilevata” da “versione da installare”.** Oggi un `Detected` senza dichiarazione diventa `"latest"` attraverso `SuggestedVersion`. È una buona scorciatoia UX, ma è troppo forte semanticamente: un GUID può identificare VRCFury, Modular Avatar o uno shader senza dimostrare che l'ultima versione sia compatibile con il vecchio SDK/preset scelto dall'utente. Il codice stesso riconosce già che osservare lo stesso GUID in più versioni non stabilisce una versione minima compatibile. fileciteturn9file0L2-L2 fileciteturn11file0L2-L2  
   **Raccomandazione:** `Detected → Auto`, non `Detected → latest`. “Auto” significa *root da sottoporre al resolver reale mantenendo immutabili i pin espliciti*, non “ultima versione = compatibile”.

3. **Trattare le scelte esplicite dell'utente come vincoli duri, non suggerimenti.** Il detector può proporre nuovi root mancanti, ma non deve cambiare SDK, package già selezionati o versioni dei preset. Se il resolver non trova una soluzione compatibile, il risultato corretto è **conflitto**, non upgrade/downgrade silenzioso. VRChat documenta che VPM supporta range e dipendenze transitive e che le versioni di Base/Avatars/Worlds sono attualmente legate tra loro; il Resolver è precisamente il componente che deve risolvere queste relazioni. citeturn27view0turn26view3  
   Il branch è già orientato in questa direzione: conserva le selezioni esistenti, non converte un range dichiarato in `latest`, usa `vrc-get` per `add`, `downgrade` e `resolve`, e dispone già di una classificazione del rischio downgrade. fileciteturn9file0L2-L2 fileciteturn16file0L1-L2 fileciteturn34file0L2-L2

4. **Far diventare il post-import un “reconciliation pass”, non un secondo detector che indovina.** Il pre-scan stabilisce evidenze; dopo che il progetto controllato dall'app è stato creato/importato, si confrontano manifest VPM, `locked`, package fisici, `package.json`, manifest UPM e log Unity. Il branch legge già separatamente `vpm-manifest.json` e `Packages/<id>/package.json`; questa distinzione va resa esplicita anche nel modello utente. fileciteturn15file0L1-L2

5. **Non usare un singolo booleano “installato”.** In particolare per Poiyomi Pro servono almeno gli stati distinti `dichiarato`, `locked`, `package fisicamente presente`, `installer presente`, `payload/shader verificato`, `non verificabile`. L'evidenza locale che mi hai fornito su `com.poiyomi.pro@11.0.38` — package VPM che porta un installer e download degli shader successivo all'autenticazione Patreon — **non è stata riprodotta da me in questa sessione** e va registrata come tale, non come comportamento universalmente verificato. L'architettura non deve mai interpretare “package VPM presente” come “shader Pro disponibili”.

6. **Conservare l'approccio offline-first e deterministico.** La scansione attuale dichiara esplicitamente di non invocare Unity, non fare chiamate catalogo e non scrivere nel progetto; i budget sono bounded e il deep scan resta locale. È esattamente la base giusta dal punto di vista privacy e sicurezza. fileciteturn8file0L2-L2 fileciteturn12file0L2-L2  
   Non vedo un motivo convincente per ML nella prima versione: GUID, manifest, package metadata e range sono segnali deterministici più precisi, spiegabili e testabili. ML sarebbe utile, al massimo, per ordinare casi già classificati come `Possible/Unknown`, non per installare automaticamente qualcosa.

### Matrice segnale → evidenza → confidenza → azione

| Segnale osservato | Che cosa prova realmente | Confidenza proposta | Azione | Possibili errori / limiti |
|---|---|---:|---|---|
| `vpmDependencies` in un `package.json` autentico e contestualizzato | Il package dichiara una dipendenza VPM e un range/versione | **Dichiarata / molto alta** | Passare il range al resolver, non convertirlo in `latest` | Manifest vendorizzato, obsoleto o non appartenente realmente al contenuto importato |
| `dependencies` di `package.json` | Dipendenza **UPM**, non automaticamente VPM | **Dichiarata UPM** | Registrarla separatamente; lasciare Unity UPM gestirla | Il codice attuale la fonde con VPM; va corretto. Unity non supporta range UPM nello stesso modo di VPM. citeturn29view2 |
| Riferimento YAML `m_Script` → GUID unico noto | L'asset serializzato fa riferimento a uno script identificato da quel GUID | **Alta** se ownership unica e firma verificata | Suggerire il package proprietario, senza dedurne la versione | GUID copiato, fork, metadato rigenerato, indice incompleto |
| `m_Shader` → GUID shader unico noto | Un materiale/asset usa quello shader | **Alta** se ownership unica | Suggerire shader/package; considerare PC/Quest separatamente | Shader vendorizzato nell'archive, fork, Pro/installer-only |
| GUID generico in YAML | Esiste un riferimento a un asset con quell'ID | **Media/bassa** | Usare per grafo/corroborazione, non auto-installare da solo | Texture, materiale, controller, asset interno, riferimento opzionale |
| Stesso GUID presente nell'archive | Il riferimento può essere soddisfatto dal contenuto importato | **Alta per “incorporato”** | Non richiedere package esterno per quel riferimento | Duplicati/collisioni interne; asset parziale |
| GUID presente nel database con un solo owner verificato | Indizio forte sull'identità del package | **Alta per identità**, **nessuna per versione minima** | `Detected(packageId)` | Il GUID non dimostra quale release sia necessaria |
| GUID associato a due package | Ownership ambigua | **Bassa/ambigua** | Nessun auto-add; mostrare “Possibile” | Fork e package che incorporano lo stesso file |
| Nome/path `VRCFury`, `Poiyomi`, `AudioLink` ecc. | Solo una corrispondenza lessicale | **Bassa** | Corroborazione o “Possibile” | Cartelle rinominate, tutorial, commenti, texture con stesso nome |
| `.asmdef` / assembly name | Il codice è organizzato o riferisce un assembly | **Media** se mappato a package verificato | Corroborare package identity | Assembly vendorizzato, nome replicato, fork |
| `Packages/<id>/package.json` dentro archive | Contenuto package incorporato, se struttura coerente | **Alta per Included** | Segnalare “incluso”, evitare duplicazione automatica | Un `package.json` arbitrario in `Assets/` non dovrebbe ricevere la stessa semantica |
| `legacyFolders`/`legacyFiles` verificati dal publisher | VPM conosce una precedente forma legacy | **Alta per migrazione legacy** | Mostrare “versione legacy rilevata”; lasciare la migrazione al package manager | Path da solo può coincidere; GUID legacy più forte. VRChat supporta esplicitamente path/GUID legacy. citeturn27view0 |
| Archive incompleto / budget esaurito | La scansione non ha coperto tutto | **Non verificabile** | Deep scan o warning, mai “nessuna dipendenza” | Falso negativo |
| Asset binario non analizzato | Nessuna conclusione sulla sua struttura interna | **Non verificabile** | Non decodificare in fast scan; dichiarare copertura | Dipendenza nascosta in formato non testuale |
| `vpm-manifest.json.dependencies` post-import | Package richiesto direttamente dal progetto | **Alta: richiesto** | Conservare intent/version range | Non prova che sia fisicamente presente |
| `vpm-manifest.json.locked` | Versione risolta/locked dal VPM | **Alta: lock VPM** | Confrontare col disco | Non equivale a payload funzionalmente pronto |
| `Packages/<id>/package.json` sul disco | Esiste un package fisico che si identifica con ID/versione | **Alta: presenza fisica** | Confrontare con direct/lock | Non prova autenticazione, inizializzazione o corretto funzionamento |
| `Packages/packages-lock.json` | Risoluzione del **Unity Package Manager** | **Alta UPM**, non VPM | Usare nel dominio UPM | Non confonderlo con `vpm-manifest.json.locked`. citeturn29view3 |
| Errori compilation Unity / missing script dopo import | Il progetto non è sano dopo la risoluzione/import | **Alta per problema, non sempre per causa** | Segnalare e correlare alle evidenze | Un CS error può derivare dal contenuto dell'avatar, non da un package mancante |
| Installer Poiyomi Pro presente | L'installer è presente | **Alta solo per installer** | Stato `InstallerPresent` | Non prova shader scaricati né autenticazione completata |

## Algoritmo e modello dei dati

### Cosa fa già il branch, e cosa cambierei

**Fatto letto dal repository.** `UnityPackageInspector` apre il file come `GZipStream`, lo percorre con `TarReader` e associa entry del tipo `<guid>/pathname`, `<guid>/asset` e `<guid>/asset.meta`. Cerca riferimenti `guid: ...`, distingue `m_Script` e `m_Shader`, raccoglie hint testuali e prova a leggere manifest JSON. Non estrae i file su disco. fileciteturn7file0L2-L2

Qui va fatta una distinzione importante: **questa struttura interna del `.unitypackage` è ciò che il codice implementa e che le fixture del progetto possono verificare; non la presenterei nell'UI o nella documentazione come contratto pubblico garantito da Unity**. La documentazione Unity che ho verificato conferma invece la parte concettuale: ogni asset riceve un ID persistente, memorizzato nel relativo `.meta`, e Unity usa tali ID per mantenere i riferimenti anche quando un asset viene spostato o rinominato. citeturn30view0

Unity documenta inoltre che scene e altri file testuali serializzati usano un subset di YAML e contengono riferimenti `fileID`; i riferimenti tra asset che il detector vede come GUID sono quindi un segnale molto più strutturale di un semplice nome di cartella. citeturn30view1

Il codice ha già una buona strategia di scope: individua prefab/scene candidate, costruisce un grafo delle referenze interne e promuove un riferimento esterno `script`/`shader` a `Detected` solo quando l'archive è completo, il riferimento è pertinente, l'ownership è unica e, con più root, il package è comune ai grafi rilevanti. Più di otto root fanno degradare deliberatamente la certezza. `Samples`, `Examples`, `Demo` ed `Editor` vengono trattati come opzionali. fileciteturn9file0L2-L2

Questa parte la manterrei.

### Pipeline proposta

La pipeline dovrebbe essere composta da quattro fasi deterministiche.

**Observation.** Il parser produce solo fatti osservati:

```text
ArchiveObservation
  archiveComplete
  textComplete
  limitations[]
  assets[]
    archiveGuid
    pathname
    references[]
      targetGuid
      semanticKind       # script, shader, generic...
      serializedField    # quando riconoscibile
    manifests[]
    asmdefs[]
    textHints[]
```

In questa fase **non esiste ancora un “package necessario”**.

**Attribution.** Ogni GUID esterno viene cercato nell'indice:

```text
GuidAttribution
  guid
  candidates[]
    packageId
    observedVersions[]
    assetKind
    sourceIds[]
    ownershipStatus      # unique / collision / historical / local-only
```

La regola più importante è:

> **GUID stabile → possibile identità del package; non → versione minima richiesta.**

È già la filosofia dichiarata nella documentazione del branch e la manterrei rigorosamente. fileciteturn11file0L2-L2

**Classification.** Le evidenze vengono aggregate:

```text
DeclaredVpm
DeclaredUpm
Detected
Possible
Included
Unknown
```

In particolare, cambierei `ObservedManifest` da:

```csharp
ObservedManifest(
    string Name,
    string Version,
    Dictionary<string,string> Dependencies)
```

a qualcosa concettualmente simile a:

```csharp
ObservedPackageManifest(
    string Name,
    string Version,
    IReadOnlyDictionary<string,string> UpmDependencies,
    IReadOnlyDictionary<string,string> VpmDependencies,
    IReadOnlyDictionary<string,string> LegacyFolders,
    IReadOnlyDictionary<string,string> LegacyFiles,
    IReadOnlyList<string> LegacyPackages,
    ManifestContext Context)
```

È una modifica ad alto valore perché rispetta la semantica reale dei due package manager. VRChat aggiunge precisamente `vpmDependencies` sopra il formato Unity; inoltre VPM permette range e dipendenze di dipendenze, mentre Unity UPM tratta il proprio `dependencies` diversamente. citeturn27view0turn29view2

**Reconciliation.** Solo quando esiste un progetto controllato dall'app si incrociano detection, policy utente, stato VPM/UPM e filesystem.

Suggerisco di non rappresentare questo stato con un singolo enum. È più robusto usare assi indipendenti:

```text
ProjectPackageState
  packageId

  intent:
    None | ExplicitUser | Preset | Detected | DirectManifest | Transitive

  declaration:
    None | VpmDirect | VpmLocked | UpmDirect | UpmLocked

  presence:
    Absent | PhysicalPackage | EmbeddedLegacy | IncludedInImport | InstallerOnly

  readiness:
    Unknown | Ready | RequiresUserAction | Failed

  requestedConstraint
  lockedVersion
  physicalVersion

  health:
    Ok | Missing | VersionMismatch | Conflict | Obsolete | Unverifiable

  evidence[]
```

Così:

- `vpm-manifest.dependencies` può dire **richiesto**;
- `locked` può dire **risolto a X**;
- `Packages/<id>/package.json` può dire **fisicamente presente a X**;
- una condizione specifica del package può dire **installer presente ma payload non verificato**.

Il codice attuale ha già i primi tre pezzi: `ProjectWorkspace` legge `dependencies`, `locked` e i `package.json` fisici separatamente. fileciteturn15file0L1-L2

### Cosa non costituisce prova

Un nome di directory, una namespace C#, una stringa `"Poiyomi"` in un materiale, un assembly name, un file chiamato `VRCFurySomething`, un riferimento GUID non presente nell'indice o un generico asset dello stesso publisher **non devono mai produrre da soli un'installazione automatica**. Sono segnali di ranking.

Anche l'assenza di match non prova l'assenza di dipendenze. Un `.meta` rigenerato cambia l'identità osservabile; un fork può conservare GUID originari; un creator può incorporare direttamente uno script; un archive può essere parziale; asset serializzati non testuali possono sfuggire al parser. Unity lega esplicitamente l'identità dell'asset al `.meta`, quindi una perdita/rigenerazione di metadata rende intrinsecamente meno affidabile qualsiasi database storico di GUID. citeturn30view0

## Indice delle firme, versioni e resolver

### Schema consigliato

Il seed attuale è già metadata-only. Lo script `Build-DependencySeed.py` raccoglie GUID da `.meta`, package ID/versione, path relativo e SHA-256 dei metadata, senza distribuire il contenuto dei package; dichiara inoltre esplicitamente che si tratta di **osservazioni locali, non attestazioni del publisher**. fileciteturn27file0L2-L2

Lo evolverei in un indice versionato con provenance di prima classe:

```json
{
  "schema": 2,
  "generatedAt": "2026-10-28T...",
  "sources": [
    {
      "id": "src-...",
      "authority": "publisher-vpm | publisher-repo | local-observation",
      "packageId": "nadena.dev.modular-avatar",
      "version": "1.19.0-alpha.2",
      "commitOrRelease": "...",
      "manifestSha256": "...",
      "observedAt": "...",
      "complete": true
    }
  ],
  "packages": [
    {
      "id": "nadena.dev.modular-avatar",
      "versions": [
        {
          "version": "1.19.0-alpha.2",
          "vpmDependencies": {
            "nadena.dev.ndmf": ">=1.15.0-beta.1 <2.0.0-a"
          },
          "upmDependencies": {
            "com.unity.nuget.newtonsoft-json": "2.0.0"
          },
          "sourceId": "src-..."
        }
      ]
    }
  ],
  "signatures": [
    {
      "guid": "...",
      "kind": "script",
      "packageId": "nadena.dev.modular-avatar",
      "assetPathHash": "...",
      "observedVersions": ["..."],
      "sourceIds": ["src-..."]
    }
  ]
}
```

I VPM repository sono particolarmente adatti a fornire provenance: VRChat definisce il repo come JSON contenente package e versioni, con il manifest di ciascuna versione incorporato nella listing. citeturn26view4

La priorità di trust che userei è:

**manifest/listing ufficiale del publisher → repository publisher a commit/tag fisso → package localmente installato e verificato → euristica testuale.**

Un'osservazione locale rimane utile, ma non deve magicamente diventare “ufficiale”.

### Lifecycle

Il modello migliore rimane **seed bundled + overlay locale**:

- il seed stabile arriva con la release dell'app;
- un overlay viene appreso solo da package che l'utente ha esplicitamente indicizzato o da archive che contengono manifest/package metadata coerenti;
- nessun path, nome privato, asset o credenziale viene caricato;
- le osservazioni cached restano separate dall'indice, così un indice più nuovo può riclassificare un archive senza rieseguire la decompressione.

Questa separazione è già presente nel branch. `PackageInspectionService` combina seed e `signatures.json`, può imparare firme da package inclusi e può indicizzare esplicitamente `Packages/*` di un progetto; non esegue una scansione generale automatica di tutti i progetti. fileciteturn8file0L2-L2

Un futuro update remoto dell'indice può essere utile, ma lo renderei **update di metadata separato dal file-drop**, con snapshot atomico e rollback. Non farei partire un crawler o download di repository quando l'utente trascina un avatar nell'app. È anche coerente con il confine che il repository stesso ha già documentato. fileciteturn11file0L2-L2

### Versioni: il resolver deve essere la fonte di verità

VRChat documenta che VPM supporta dipendenze transitive e range SemVer. Il Resolver usa la stessa VPM Library del VCC per download e dependency resolution e, quando un package richiede altri package, ripristina anche quelli. citeturn27view0turn26view3

Quindi VRC Project Setup **non deve implementare un secondo dependency solver**.

La policy dovrebbe essere:

```text
Explicit selections / preset pins
        ↓ immutable constraints

Declared VPM ranges from recognized manifests
        ↓ constraints

Detected packages without version evidence
        ↓ root identities only

VPM/vrc-get resolver
        ↓

candidate resolved graph

        ↓
compare against immutable user policy

OK / requires confirmation / conflict
```

Il branch usa `vrc-get`, non l'implementazione ufficiale VCC. È importante non attribuire a vrc-get un'equivalenza non dimostrata con VCC. Nel codice di `vrc-get` consultato, `vpm-manifest.json` contiene una mappa `dependencies` di range e una `locked` di versioni esatte; il resolver processa prima i locked, poi i root non locked e infine dipendenze transitive. Lo stesso sorgente contiene anche un commento esplicito secondo cui alcuni comportamenti per package unlocked possono differire dal VCC ufficiale. fileciteturn31file0L2-L2 fileciteturn29file0L2-L2

Questa distinzione è essenziale: **VRC Project Setup può usare vrc-get come resolver reale scelto dall'app, ma non deve scrivere “VCC garantisce lo stesso risultato” se non è stato verificato quel caso.**

### SDK Base, Avatars e Worlds

VRChat definisce Base come contenuto comune, Avatars come package avatar e Worlds come package world; inoltre documenta che le versioni dei tre package ufficiali sono attualmente legate e richiedono lo stesso numero di versione. citeturn26view0turn27view0

Di conseguenza eviterei regole come:

> “ho trovato un componente avatar, quindi aggiorno Avatar SDK e Base alla versione corrente”.

Meglio:

> “Il contenuto richiede funzionalità Avatar. Il preset contiene Avatars X. Mantengo X e verifico il grafo con il resolver.”

Se un nuovo package richiede `com.vrchat.avatars 3.6.x` ma Dom ha esplicitamente scelto `3.5.x`, l'app non deve scegliere chi “vince”: deve mostrare il conflitto.

## Esempi concreti, conflitti e UX

### Modular Avatar e NDMF

Il `package.json` publisher consultato per Modular Avatar sulla branch principale riportava `nadena.dev.modular-avatar`, versione `1.19.0-alpha.2`, Unity 2022.3, una dipendenza UPM da `com.unity.nuget.newtonsoft-json@2.0.0` e una dipendenza VPM da `nadena.dev.ndmf` nel range `>=1.15.0-beta.1 <2.0.0-a`. fileciteturn19file0L2-L2

NDMF, nel manifest publisher consultato, risultava `nadena.dev.ndmf` `1.15.0-beta.2`, Unity 2022.3. fileciteturn20file0L2-L2

Caso ideale:

```text
Avatar.prefab
 └─ m_Script GUID → firma unica Modular Avatar

Scanner:
  Modular Avatar = Detected
  versione = Auto

Resolver:
  sceglie una versione di Modular Avatar compatibile
  legge il suo vpmDependencies
  risolve NDMF nel range compatibile

UI:
  Modular Avatar       Rilevato dall'avatar
  NDMF                 Dipendenza di Modular Avatar
```

Non serve che lo scanner inventi un secondo suggerimento indipendente per NDMF se il resolver lo porta transitivamente.

E soprattutto `com.unity.nuget.newtonsoft-json` **non deve finire nella lista VPM**, perché appartiene al dominio UPM.

### VRCFury

Il manifest del source tree di VRCFury che ho letto usa l'ID `com.vrcfury.vrcfury`, Unity 2022.3 e contiene `legacyFolders`/`legacyFiles`; la versione nel source tree è `0.0.0`. fileciteturn21file0L2-L2

Questo è un ottimo esempio del perché **non basta leggere `package.json` da una branch Git e trattare quel valore come versione pubblicata**. L'indice deve sapere da dove arriva il record: `publisher-source-tree`, `publisher-vpm-release`, `local-installed`, ecc.

Un GUID VRCFury stabile può quindi dire:

> “Questo avatar usa VRCFury.”

Non necessariamente:

> “Questo avatar richiede VRCFury >= X.”

### AudioLink

Il manifest publisher consultato di AudioLink riportava `com.llealloo.audiolink` `3.1.2`, Unity 2022.3, `vpmDependencies` vuoto e una migrazione legacy per `Assets\AudioLink`. fileciteturn23file0L2-L2

Quindi un riferimento a una firma AudioLink può identificare AudioLink con buona confidenza, ma una stringa `AudioLink` in uno shader o in un integration script non dimostra da sola una dipendenza obbligatoria.

### Poiyomi Pro

Qui la UX deve essere deliberatamente diversa.

**Fatto fornito da Dom, non testato da me:** nel test CLI locale indicato nella richiesta, `com.poiyomi.pro@11.0.38` installa un installer Unity e gli shader vengono acquisiti successivamente tramite il flusso autenticato Patreon.

Il modello dovrebbe quindi poter mostrare, per esempio:

```text
Poiyomi Pro
Package VPM: presente
Versione package: 11.0.38
Installer: presente
Shader Pro: non verificati
Autenticazione: stato non letto

Azione:
"Apri Unity e completa il setup ufficiale di Poiyomi Pro."
```

Non:

```text
✓ Poiyomi Pro installato e pronto
```

e certamente nessun tentativo di scaricare, estrarre, replicare o aggirare il payload autenticato.

Questo principio è generalizzabile: **“package manager success” è un fatto di gestione package, non necessariamente un fatto di disponibilità funzionale del contenuto downstream.**

### Messaggi UX

Pre-import:

> **VRCFury rilevato**  
> L'avatar contiene componenti che fanno riferimento a VRCFury.  
> `Aggiungi` · `Ignora`

> **Modular Avatar rilevato**  
> Riferimento trovato nel prefab principale. Le sue dipendenze verranno risolte automaticamente.  
> `Aggiungi`

> **Possibile uso di Poiyomi**  
> Ho trovato riferimenti compatibili, ma non abbastanza evidenza per installarlo automaticamente.  
> `Controlla`

> **Analisi parziale**  
> Alcuni file non sono stati analizzati completamente. Potrebbero esserci altre dipendenze.  
> `Analizza meglio`

Post-import:

> **Package richiesto ma assente**  
> `com.example.tool` è dichiarato nel progetto ma non è presente su disco.  
> `Ripristina`

> **Versioni in conflitto**  
> Hai scelto VRChat SDK Avatars 3.x.x. Modular Avatar richiede una versione che il resolver non riesce a soddisfare insieme a questa scelta.  
> `Mantieni la mia versione` · `Rivedi package`

> **Poiyomi Pro richiede un passaggio in Unity**  
> L'installer è presente, ma gli shader Pro non risultano verificabili da VRC Project Setup. Completa il setup ufficiale in Unity.

La correzione di un falso positivo deve essere reversibile e locale: `Ignora per questo import`, eventualmente `Non suggerire più da questa firma` in un pannello avanzato. Non trasformerei il feedback privato dell'utente in telemetria automatica.

### Regole operative

| Situazione | Comportamento |
|---|---|
| GUID interno/builtin | Automatico: ignorare come dipendenza esterna |
| Package `Included` nell'archive | Automatico: non aggiungere duplicato |
| Dipendenza VPM esplicitamente dichiarata, range compatibile | Automatico nel piano, risoluzione delegata al resolver |
| `Detected` con firma unica ma senza versione | Suggerimento pre-selezionato o one-click, versione `Auto` |
| `Possible` / text hint / collisione | Mai auto-add |
| Selezione esplicita già presente | Mai modificarla per effetto del detector |
| Downgrade di un package scelto | Conferma |
| Downgrade SDK | Conferma forte; se necessario a risolvere un conflitto, spiegare causa |
| Rimozione package per sostituzione/legacy | Mostrare nel piano; backup prima |
| Nessuna versione soddisfa i vincoli | Bloccare quel piano, non usare `--force` |
| Archive invalido | Bloccare import come “archive non valido”, separatamente dalle dipendenze |
| Copertura parziale | Consentire solo con warning esplicito o deep scan, non dichiarare “tutto ok” |
| Package privato non indicizzato | `Non verificabile`, possibilità di scelta manuale |
| Installer autenticato senza payload | `RequiresUserAction`, non `Ready` |

Il CLI ufficiale VPM offre anche una rimozione `--force` che può rimuovere un package persino se ciò rompe il progetto: questa è precisamente la semantica che eviterei per workflow automatici. citeturn27view1

## Integrazione incrementale nel repository

Qui distinguo esplicitamente **codice letto** da **proposte**.

### Punti d'integrazione verificati

**`src/VrcSetup.Infrastructure/UnityPackageInspector.cs` — letto.** È il punto corretto per observation/parser, non per decidere versioni o installazioni. Oggi raccoglie pathname, GUID refs, typed script/shader refs, hint e manifest. fileciteturn7file0L2-L2

Prima modifica:

```text
ObservedManifest.Dependencies
        ↓
ObservedManifest.UpmDependencies
ObservedManifest.VpmDependencies
ObservedManifest.Legacy*
ObservedManifest.Context
```

Inoltre restringerei la semantica “manifest package incluso” a percorsi contestualmente credibili, preferibilmente `Packages/<package-id>/package.json` oppure una root package esplicitamente verificata. Un JSON valido chiamato `package.json` sotto un asset generico non dovrebbe automaticamente diventare un VPM package incluso.

**`src/VrcSetup.Core/PackageInspection.cs` — letto.** È già il posto corretto per attribution/confidence. Qui modificherei `SuggestedVersion(Detected)` da `"latest"` a `null/"auto"` e manterrei la buona regola attuale: collisione GUID → niente scelta arbitraria. fileciteturn9file0L2-L2

La confidence attuale può restare quasi invariata:

```text
Declared
Detected
Possible
Included
```

ma aggiungerei il **domain**:

```text
Vpm | Upm | AssetOnly
```

e possibilmente una reason machine-readable:

```text
ManifestVpmDependency
UniqueScriptGuid
UniqueShaderGuid
AmbiguousGuid
TextHint
EmbeddedPackage
OptionalSample
IncompleteScan
```

Questo migliora molto UX e test senza introdurre complessità concettuale.

**`src/VrcSetup.Infrastructure/PackageInspectionService.cs` — letto.** Ha già cache, coalescing dei job, seed+local overlay, deep retry e explicit project indexing. Lo terrei come orchestratore della scansione. fileciteturn8file0L2-L2

Qui aggiungerei soltanto:

- schema versione 2 dell'indice;
- provenance strutturata invece di `Source` string-only;
- package/version catalog separato dalla tabella GUID;
- un flag `PublisherVerified / LocalObserved`, senza promuovere automaticamente un'osservazione locale.

Il fast/deep attuale ha già limiti ragionevoli: circa 8 s vs 45 s di budget, stream espanso 4 vs 8 GiB, 64 vs 256 MiB di testo e limiti su entry/assets/references. fileciteturn9file0L2-L2 Il punto non è aumentare sempre i limiti: è **rendere visibile la copertura**.

**`scripts/Build-DependencySeed.py` — letto.** Può diventare il primo generatore schema-2. Attualmente la allowlist include, tra gli altri, SDK Base/Avatars/Resolver, VRCFury, Poiyomi Toon/Thry, GoGo Loco, AudioLink, Modular Avatar e NDMF, e raccoglie metadata senza copiare gli asset. fileciteturn27file0L2-L2

Non amplierei quella allowlist a un package nuovo finché non hai:

1. ID installabile verificato;
2. fonte publisher o osservazione locale esplicitamente etichettata;
3. almeno una release/versione osservata;
4. ownership di script/shader verificata;
5. test di collisione.

**`src/VrcSetup.Infrastructure/ProjectWorkspace.cs` — letto.** È il punto naturale per il post-import state reconciler perché legge già direct VPM, locked e package fisici. fileciteturn15file0L1-L2

Aggiungerei una API tipo:

```text
ReadDependencyStateAsync(project)
```

che non modifica nulla e costruisce lo stato multidimensionale descritto sopra.

**`PackagePresence.cs` — letto.** Oggi `Contains` considera presenza in direct, locked oppure installed come “presence” logica. È utile per molte operazioni, ma per l'UX diagnostica questi tre casi non sono equivalenti. fileciteturn13file0L2-L2

Non romperei `Contains`; aggiungerei funzioni più precise:

```csharp
IsDirectlyRequested(...)
IsLocked(...)
IsPhysicallyPresent(...)
IsResolvedConsistently(...)
```

**`UnityProjectDriver` / `UnityProjectDriver.Packages.cs` — letti.** Il branch esegue le modifiche attraverso `vrc-get`, usa `downgrade` per target espliciti più bassi, esegue infine `resolve`, rifiuta operazioni quando Unity è aperto e verifica i log Unity dopo l'import batch. fileciteturn14file0L2-L2 fileciteturn16file0L1-L2

Questo è il punto corretto per l'esecuzione, ma non per detection. Aggiungerei un **preflight policy check** subito prima del package plan:

```text
Detection suggestions
        +
Preset/user selections
        ↓
immutable policy
        ↓
resolver plan
        ↓
diff:
 adds / transitives / updates / downgrades / removals / conflicts
        ↓
review
        ↓
ApplyPackagePlanAsync
```

**`PackageDowngrades.cs` — letto.** Esiste già una classificazione `None / Downgrade / RiskyDowngrade / Unknown`; SDK e cambio major verso il basso sono trattati come più rischiosi. fileciteturn34file0L2-L2

La estenderei con una distinzione importantissima:

```text
userExplicitChange
resolverRequiredChange
detectorSuggestedChange
```

Un downgrade che Dom ha scelto volontariamente non è la stessa cosa di un downgrade che emerge perché il detector ha aggiunto un package.

### Sequenza di implementazione

La prima iterazione può essere sorprendentemente piccola:

```text
A. Manifest VPM/UPM separati
B. Detected → Auto invece di latest
C. Package-state post-import preciso
D. Conflict review prima di Apply
```

Solo dopo espanderei il database.

Questa è la scelta che riduce maggiormente il rischio: **migliorare la semantica prima di migliorare la recall**.

## Piano di test e criteri di successo

La strategia di test dovrebbe avere tre livelli nettamente separati.

### Fixture CLI sintetiche

Sono il corpus principale per la logica deterministica. Un `.unitypackage` sintetico può usare GUID noti, YAML minimale e manifest fittizi senza distribuire contenuto di terzi.

| Fixture | Ground truth | Risultato atteso |
|---|---|---|
| Prefab → script GUID unico `PkgA` | `PkgA` richiesto | `Detected PkgA` |
| Prefab → GUID presente anche nell'archive | Nessun package esterno | Nessun `Detected` esterno |
| Due avatar, `PkgA` usato da entrambi | Dipendenza comune | `Detected` |
| Due avatar, `PkgA` solo nel secondo | Ambiguo per package globale | `Possible` |
| Più di otto candidate roots | Scope insufficiente | Nessuna promozione ingiustificata |
| GUID con owner `PkgA` e `PkgB` | Collisione | Entrambi `Possible`, zero auto-add |
| `vpmDependencies: {"PkgA": ">=1 <2"}` | VPM dichiarata | Range conservato esattamente |
| `dependencies: {"com.unity.foo":"1.0.0"}` | UPM dichiarata | **Mai** suggerita come root VPM |
| Manifest con entrambe | Domini separati | VPM e UPM distinti |
| `Packages/PkgA/package.json` incluso | Embedded/included | `Included`, niente duplicato |
| `Assets/foo/package.json` generico | Non prova package VPM | Non `Included` automaticamente |
| Sample/Editor usa `PkgA` ma avatar no | Opzionale | `Possible` |
| GUID sconosciuto | Ground truth sconosciuta | `Unresolved`, non “package mancante” |
| GUID rigenerato | Detector non può riconoscerlo | False negative noto / unverifiable |
| Archive troncato | Copertura incompleta | `ArchiveComplete=false` |
| Entry oltre text budget | Copertura parziale | `TextComplete=false`, deep disponibile |
| TAR con traversal/link | Nessuna estrazione | Nessuna scrittura fuori processo/cache |
| Duplicate archive GUID | Archive ambiguo | Warning/collisione, zero auto-fix |
| JSON/YAML malformato | Non verificabile | Nessun crash |
| Binary-only dependency surrogate | Non leggibile | Copertura esplicitamente incompleta |

### Test del resolver e del progetto

Qui servono progetti temporanei e package fittizi VPM:

| Scenario | Atteso |
|---|---|
| Root A → B transitiva | B installata dal resolver, non duplicata manualmente dal detector |
| A richiede `B >=2`, preset pin B=1 | Conflitto, B=1 non modificato silenziosamente |
| SDK Avatars vecchio esplicitamente scelto + tool che richiede SDK nuovo | Review/blocco; nessun upgrade automatico |
| `dependencies` presente, package folder assente | `Required + Missing` |
| `locked=1.2`, folder `package.json=1.1` | `VersionMismatch` |
| Folder fisico, nessun root/lock VPM | `Physical/Embedded`, non “VPM direct” |
| `packages-lock.json` UPM presente senza VPM entry | Stato UPM, non VPM |
| Package legacy sostituito | Diff mostra rimozione/migrazione prima dell'applicazione |
| User pin downgrade | Richiede conferma secondo policy |
| Resolver senza soluzione | Operazione bloccata, niente force |

VRChat documenta che `resolve project` ripristina i package dal `vpm-manifest.json` e può eliminare legacy packages; la CLI supporta inoltre l'aggiunta di una versione specifica. citeturn27view1 Questi test vanno però eseguiti contro **la versione di vrc-get effettivamente distribuita dall'app**, perché non va presunta l'identità comportamentale con il VPM CLI ufficiale.

### Unity batch e verifica manuale

Unity batch deve testare ciò che il parser statico non può dimostrare:

```text
compilazione completa
nessun error CS...
post-import bridge completato
script mancanti
materiali/shader unresolved
asset import failures
```

Il branch già tratta compilation failure, `error CS...`, import failure e mancato completamento del post-import come errore dell'operazione. fileciteturn16file0L1-L2

La **verifica visuale/manuale** resta separata per cose come:

- rendering di un materiale;
- shader effettivamente disponibile;
- workflow autenticato di un tool;
- inspector/editor UI;
- differenze PC/Android/Quest che non producono un errore di compilazione.

Non farei mai passare un test CLI “package presente” come test positivo di Poiyomi Pro pronto all'uso.

Per quel caso creerei nel repository un **surrogate sintetico**, per esempio:

```text
com.example.authenticated-installer
```

che produce:

```text
PackagePresent = true
InstallerPresent = true
PayloadVerified = false
```

Il test del prodotto a pagamento reale può essere un test locale/manuale opzionale eseguito soltanto con una copia legalmente posseduta dall'esecutore, mai una fixture CI pubblica.

### Metriche

Per il detector conviene separare le classi.

**Auto-action precision** è la metrica più importante: quanti package che il software suggerirebbe con un solo click sono realmente necessari.

Come criteri di release proposti, non come risultati già misurati:

| Metrica | Obiettivo iniziale |
|---|---:|
| Modifica silenziosa di pin utente | **0** |
| Range VPM trasformati arbitrariamente in `latest` | **0** |
| GUID con ownership multipla auto-installati | **0** |
| UPM dependency proposta come VPM per errore | **0** |
| Network request durante puro `inspect` | **0** |
| Upload di pathname/asset/credential | **0** |
| Determinismo, stesso archive + stessa index revision | **100%** |
| Precisione su suggerimenti `Detected/Declared` del corpus supportato | **≥99% target** |
| Recall sul corpus di package/versioni esplicitamente supportato | **≥90% target iniziale** |
| Archive sconosciuto classificato falsamente come “completo e senza dipendenze” quando scan è partial | **0** |

Per le performance userei i budget attuali come hard ceiling, ma registrerei anche p50/p95 su hardware dichiarato. Il branch già impone 8 secondi per la modalità normale e 45 secondi per Extended, oltre a limiti di espansione, testo, numero di entry, asset e referenze. fileciteturn9file0L2-L2

Un target ragionevole da misurare potrebbe essere **p95 < 2 s per il corpus “normale”**, mantenendo l'8 s come fail-safe; non lo presenterei come requisito finché non hai un corpus rappresentativo reale.

I test avversi dovrebbero includere archive bomb, centinaia di migliaia di member, path enormi, Unicode, duplicate TAR entries, symlink/hardlink, traversal, JSON profondamente annidato, YAML enorme, package manifest fasulli e GUID intenzionalmente collidenti.

## Limiti aperti, decisioni e backlog

### Limiti che non eliminerei con euristiche

Non esiste una scansione statica del `.unitypackage` che possa garantire tutte le dipendenze. Un archive può contenere asset binari, script che risolvono risorse dinamicamente, GUID rigenerati, package privati sconosciuti, codice editor che genera contenuto dopo l'import o una dipendenza disponibile solo dopo login. Il risultato corretto in questi casi è **“non verificabile”**, non una supposizione.

Inoltre, `package.json` fisicamente presente non significa automaticamente “package operativo”: Unity documenta soltanto il manifest come identificatore/versione/configurazione del package. citeturn30view2 Un package può richiedere inizializzazione, autenticazione o generazione successiva.

Unity UPM e VPM devono restare due domini distinti. `Packages/packages-lock.json` registra la risoluzione UPM; il VPM usa `vpm-manifest.json`, e nel vrc-get consultato quest'ultimo possiede le proprie sezioni `dependencies` e `locked`. citeturn29view3 fileciteturn31file0L2-L2

Anche “embedded” va usato con attenzione. Unity considera embedded un package fisicamente sotto `Packages`, e un embedded può avere precedenza sul package dichiarato nel manifest. citeturn29view4 Nel prodotto conviene però distinguere `EmbeddedUpm`, `PhysicalVpmPackage` e `LegacyAssets`: sembrano simili sul filesystem ma hanno implicazioni diverse.

Per **GoGo Loco** e **Avatar Limb Scaling**, il repository attuale contiene conoscenza/naming locale relativa a questi package, ma in questa sessione non ho verificato fino in fondo un manifest/version history publisher che mi permetta di raccomandare nuovi vincoli di versione o nuove firme come attestazioni ufficiali. Il fatto che `Build-DependencySeed.py` abbia `gogoloco` nella sua allowlist è un **fatto del repository**, non una prova autonoma sull'attuale distribuzione del creator. fileciteturn27file0L2-L2 Questa lacuna va lasciata esplicita.

Per **Poiyomi Pro 11.0.38**, considero l'installer-only + autenticazione successiva una **osservazione locale fornita da Dom**, non un test ripetuto durante questa ricerca.

### Decisioni che prenderei ora

**Decisione architetturale:** mantenere il detector come sistema di evidenze, non trasformarlo in un package manager parallelo.

**Decisione sulle versioni:** GUID e nomi identificano package, non range. Solo manifest verificati e resolver possono introdurre vincoli di versione.

**Decisione sull'automazione:** automatico solo ciò che non modifica una decisione esplicita e ha evidenza strutturale forte. Il resto è suggestion/review.

**Decisione privacy:** nessun archive, nome asset, pathname locale o credential esce dalla macchina durante detection. Gli aggiornamenti del catalogo/index sono operazioni separate.

**Decisione ML:** non implementarlo. Prima misurare precision/recall del detector deterministico; oggi aggiungere ML aumenterebbe complessità, opacità e superficie privacy più di quanto aumenterebbe l'affidabilità.

**Decisione PC/Quest:** registrare la piattaforma come contesto dell'evidenza, ma non assumere che “shader desktop rilevato” implichi automaticamente un package Quest equivalente. Un asset può avere materiale alternativo incorporato o richiedere una conversione manuale. Le raccomandazioni devono essere per target, non una singola lista globale.

### Backlog ordinato

| Priorità | Lavoro | Valore | Rischio | Dipendenze |
|---|---|---:|---:|---|
| **P0** | Separare `dependencies` UPM da `vpmDependencies` VPM | Molto alto | Basso | Nessuna |
| **P0** | Sostituire `Detected → latest` con `Detected → Auto/no version claim` | Molto alto | Basso | Modello UI |
| **P0** | Test regression Modular Avatar: Newtonsoft UPM ≠ VPM, NDMF = VPM | Molto alto | Basso | Fix manifest |
| **P0** | Rendere pin/preset explicit immutable per il detector | Molto alto | Medio | Planner |
| **P0** | Bloccare resolver conflict senza force/fallback silenzioso | Molto alto | Medio | Resolver integration |
| **P1** | Aggiungere `ProjectPackageState` multi-assi | Alto | Medio | `ProjectWorkspace` |
| **P1** | Distinguere direct / locked / physical / embedded / installer-ready | Alto | Medio | State model |
| **P1** | Restringere “Included manifest” a contesto package credibile | Alto | Basso | Inspector |
| **P1** | Reasons machine-readable per ogni suggestion | Alto | Basso | Core model |
| **P1** | UI pre-import vs post-import con testi brevi e azioni reversibili | Alto | Medio | State model |
| **P1** | Fixture sintetiche UPM/VPM/collision/range/included/partial | Alto | Basso | Nuovo schema |
| **P1** | Test immutabilità: 0 modifiche silenziose a versioni esplicite | Alto | Basso | Planner |
| **P2** | Signature index schema 2 con provenance strutturata | Alto | Medio | P0 semantics |
| **P2** | Stato provenance `publisher / local observation` | Alto | Basso | Index v2 |
| **P2** | Cronologia GUID per versione, senza inferire minimum version | Medio-alto | Medio | Corpus |
| **P2** | Preflight diff del resolver: adds/transitives/downgrades/removals | Alto | Medio-alto | vrc-get integration |
| **P2** | Fixture surrogate installer/authenticated payload | Medio | Basso | State model |
| **P2** | Corpus autorizzato con Modular Avatar, NDMF, VRCFury, AudioLink e altri gratuiti/legalmente redistribuibili | Alto | Medio | Provenance policy |
| **P3** | Verifica publisher di GoGo Loco, Avatar Limb Scaling e altri candidati prima di ampliare seed | Medio | Medio | Corpus/research |
| **P3** | Metadata-index updater opzionale, atomico e offline-cacheable | Medio | Medio | Index v2 |
| **P3** | Analisi `.asmdef` e assembly refs come evidence secondaria | Medio | Medio | Metrics FP/FN |
| **P3** | Classificazione target PC/Android/Quest delle evidenze | Medio | Medio-alto | Corpus multi-target |
| **P4** | Valutare fuzzy/ML ranking soltanto sugli `Unknown` | Basso oggi | Alto | Dataset misurato |

La scelta più importante, in pratica, è **non inseguire subito più package**. Il detector del branch è già abbastanza maturo da avere una base solida; i maggiori guadagni di affidabilità arrivano dal rendere più rigorosa la semantica di ciò che già riconosce: separare UPM/VPM, non dedurre versioni dai GUID, modellare presenza e readiness separatamente, preservare i pin come vincoli duri e lasciare la risoluzione transitiva al resolver effettivamente utilizzato. Questo non rende il rilevamento garantito, ma riduce in modo sostanziale le due failure mode più pericolose per VRC Project Setup: **installare il package sbagliato** e **“sistemare” un progetto cambiando una versione che l'utente aveva scelto intenzionalmente**.