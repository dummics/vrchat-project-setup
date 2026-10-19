# Test su Windows pulito

Usare una VM Windows 11 x64 con un account normale. Creare uno snapshot prima del test. Non installare .NET, Visual Studio o la CLI VPM per preparare l'app: il download include i componenti necessari. Unity resta necessario per creare e importare i progetti.

1. Scaricare l'EXE dalla [pagina release](https://github.com/dummics/vrchat-project-setup/releases). Installarlo senza «Esegui come amministratore». Verificare il nome **VRC Project Setup**, il collegamento in Start e le opzioni finali.
2. Avviare l'app senza Unity installato: deve aprirsi e permettere di scegliere l'editor, senza eccezioni tecniche. Avviarla una seconda volta: deve tornare alla finestra esistente.
3. Controllare i pacchetti predefiniti: SDK VRChat (Resolver, Base, Avatars), Poiyomi e VRCFury. Creare un preset, uscire con modifiche e provare Salva, Scarta e Annulla.
4. Installare l'editor Unity richiesto dall'app tramite Unity Hub e completare l'attivazione di Unity. Usare un piccolo `.unitypackage` di prova, senza dati privati, e una cartella come `Documenti\Avatar test [PC] & città`.
5. Creare un progetto Windows, aprirlo in Unity e attendere la fine dell'importazione. Verificare che gli asset e i pacchetti scelti siano presenti. Ripetere la reimportazione sullo stesso progetto. Per Quest, installare anche il modulo Android prima del relativo test.
6. Aggiungere e togliere un pacchetto, applicare e riaprire la schermata. Verificare le versioni effettive. Le modifiche a un preset personale non devono sostituire gli altri preset.
7. Per l'update, ripartire dallo snapshot e installare prima la beta.23. Salvare un preset riconoscibile, abilitare gli aggiornamenti beta e cercare la beta.24. Installarla e verificare versione, riapertura e preset conservato. Se si è appena controllato, la cache può durare 10 minuti; i controlli automatici sono distanziati di circa 4 ore.
8. Avviare un import e verificare che l'aggiornamento venga rimandato. Interrompere la rete prima di un nuovo download: l'app deve mostrare un errore gestibile; ripristinarla e riprovare.
9. Esportare i log dal menu Contatti: deve comparire uno ZIP con un nome leggibile, selezionato nella sua cartella, pronto da condividere.

In caso di errore, conservare lo ZIP dei log e annotare versione Windows/app, passaggio eseguito ed eventuali messaggi. Non servono account GitHub per installare o esportare i log.

## Copertura verificata il 19 ottobre 2026

- 234 test automatici, inclusi percorsi Unicode, ripresa dopo errore della sorgente, metadati disponibili offline e conservazione delle scelte.
- EXE pubblico beta.24: 19 controlli reali su Windows 11 x64 con token non elevato; cache iniziale vuota, 13 componenti / 182.384.465 byte scaricati, installazione, aggiornamento della stessa versione, riparazione, CLI, collegamenti isolati e disinstallazione. Impostazioni e progetto di prova conservati.
- Client updater reale: feed pubblico beta.23 → beta.24, cache, nessun aggiornamento della stessa versione/downgrade, filtro beta, metadati di preparazione e checksum dell'installer. Una sola richiesta al feed; nessuna richiesta a `api.github.com`.
- La CI beta.24 ha già verificato metadati VPM su profilo Windows pulito, UI nativa e aggiornamento dalla release precedente con riavvio.

Queste prove non certificano ogni PC: restano da eseguire in VM il primo import reale con Unity, Windows 10, ARM64/emulazione, DPI/accessibilità, proxy/reti aziendali e antivirus/SmartScreen. Il pacchetto pubblicato è x64. Il test locale non equivale all'assenza di runtime già presenti sul sistema; la VM serve anche a confermare questo confine.
