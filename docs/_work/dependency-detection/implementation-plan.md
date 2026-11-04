# Detector: direzione di implementazione

Stato 2026-10-28. Fonte: `2026-10-28-deep-research.md`, riferita al commit 19bf51f; verificare ogni affermazione esterna prima di usarla come requisito. Il report contiene marcatori di citazione della sessione di ricerca che potrebbero non essere risolvibili fuori da ChatGPT.

## Obiettivo

Riconoscere automaticamente le dipendenze effettive di un UnityPackage, senza sovrascrivere preset o versioni esplicite, distinguendo ciò che è certo da ciò che non può essere verificato. Evitare upload di contenuti privati e richieste di scelte tecniche non necessarie.

## Primo intervento

- Separare le dipendenze UPM (`dependencies`) dalle VPM (`vpmDependencies`) nel parsing e nei dati osservati. Solo le seconde alimentano i suggerimenti VPM. Incrementare schema e chiave della cache per evitare interpretazioni vecchie.
- Un package è identificato come incorporato solo se il manifest si trova in `Packages/<id>/package.json` e il nome corrisponde. L'indice locale apprende firme dagli archive solo con questa stessa prova.
- Regressioni sintetiche: package con Newtonsoft UPM e NDMF VPM, falso manifest in `Assets`, indici e cache.

Questa modifica riduce falsi positivi ma non aumenta da sola la copertura. Le firme apprese precedentemente potrebbero contenere associazioni errate: non eliminarle automaticamente senza un piano di migrazione che preservi l'indice personale.

## Prossime fasi

1. **Corpus e baseline**: fixture sintetiche e archivi reali autorizzati, ground truth verificata; misurare precisione, richiami mancati, copertura, tempo e memoria; mantenere separati platform, package a pagamento e asset privati.
2. **Risoluzione versioni**: progettare una policy `auto` distinta da `latest`, con i pin espliciti come vincoli invariabili. Verificare una vera preview del resolver e le sue regole di conflitto; non attribuire a una nuova etichetta una compatibilità non dimostrata. Niente fallback che forza una versione incompatibile.
3. **Evidenze e confidenza**: tracciabilità per GUID, package, versione della fonte e collisioni; usare gli indizi testuali solo come prova debole e trattare scansioni parziali come incomplete. Definire una migrazione dell'indice personale esistente.
4. **Verifica dopo import**: confrontare richieste VPM, lock e contenuto fisico senza confondere installatore e payload, con particolare attenzione a Poiyomi Pro, asset legacy, SDK e rollback.
5. **Esperienza utente**: solo suggerimenti affidabili applicati automaticamente; messaggi brevi per i casi ambigui o impossibili e un singolo passo di recupero. Nessuna scelta tecnica obbligatoria per il flusso ordinario.

## Confini di verifica

I test sintetici validano casi specifici, non dimostrano una precisione generale. CLI, apertura Unity e risultato visivo sono prove distinte. Non alterare progetti aperti in Unity o usare copie di shader a pagamento senza autorizzazione.
