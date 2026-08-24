# Guida al versionamento VPM

## Come Funziona

Ogni package VPM può avere una versione specifica o usare `"latest"` per l'ultima disponibile.

## Comandi VPM Utili

### Vedere Versioni Disponibili

```powershell
# Mostra informazioni complete su un package
vpm show package com.vrchat.avatars

# Lista tutti i packages disponibili
vpm list packages

# Cerca un package
vpm search poiyomi
```

### Installare Versioni Specifiche

```powershell
# Installa ultima versione
vpm add package com.vrchat.avatars

# Installa versione specifica
vpm add package com.vrchat.avatars@3.5.0
```

## Esempi di Configurazione

### Usare Solo Latest (Raccomandato per Principianti)

```json
{
    "VpmPackages": {
        "com.vrchat.base": "latest",
        "com.vrchat.avatars": "latest",
        "com.poiyomi.toon": "latest"
    }
}
```

**Pro:**
- ✅ Sempre aggiornato
- ✅ Semplice da gestire

**Contro:**
- ⚠️ Può rompere progetti esistenti con breaking changes
- ⚠️ Meno controllo

### Usare Versioni Specifiche (Raccomandato per Produzione)

```json
{
    "VpmPackages": {
        "com.vrchat.base": "3.5.0",
        "com.vrchat.avatars": "3.5.0",
        "com.poiyomi.toon": "9.0.57",
        "com.vrcfury.vrcfury": "1.983.0"
    }
}
```

**Pro:**
- ✅ Riproducibilità garantita
- ✅ Nessuna sorpresa
- ✅ Perfetto per team o progetti condivisi

**Contro:**
- ⚠️ Devi aggiornare manualmente
- ⚠️ Potresti perdere fix importanti

### Approccio Misto (Raccomandato)

```json
{
    "VpmPackages": {
        "com.vrchat.base": "3.5.0",          // SDK fisso
        "com.vrchat.avatars": "3.5.0",       // SDK fisso
        "com.poiyomi.toon": "latest",        // Shader sempre aggiornato
        "com.vrcfury.vrcfury": "latest",     // Tool sempre aggiornato
        "gogoloco": "latest"                 // Animazioni sempre aggiornate
    }
}
```

**Pro:**
- ✅ Stabilità per componenti critici (SDK VRChat)
- ✅ Novità per tool e assets non critici
- ✅ Bilanciato

## Validazione Versioni

Lo script valida automaticamente le versioni usando `vpm show package`:

1. ✅ Se la versione è `"latest"` → sempre valida
2. 🔍 Se è una versione specifica → controlla che esista nel registry VPM
3. ❌ Se non esiste → mostra errore e suggerisce di usare `vpm show`

## Troubleshooting

### "Package non trovato nel registry VPM"

**Causa:** Il package non esiste o hai scritto male il nome.

**Soluzione:**
```powershell
# Cerca il package corretto
vpm search <parte-del-nome>

# Esempio
vpm search poiyomi
```

### "Versione non trovata"

**Causa:** La versione specificata non esiste.

**Soluzione:**
```powershell
# Vedi tutte le versioni disponibili
vpm show package <nome-package>

# Esempio
vpm show package com.vrchat.avatars
```

### "vpm non disponibile o errore"

**Causa:** VPM CLI non è installato o non è nel PATH.

**Soluzione:**
1. Installa VPM Creator Companion
2. Apri PowerShell e prova `vpm --version`
3. Se non funziona, aggiungi VPM al PATH

## Best Practices

### 🎯 Per Progetti Personali
- Usa `"latest"` per tutto
- Aggiorna regolarmente

### 👥 Per Progetti Condivisi
- Usa versioni specifiche
- Documenta le versioni nel README del progetto
- Aggiorna in modo coordinato con il team

### 🏭 Per Progetti in Produzione
- Usa versioni specifiche
- Testa ogni aggiornamento in un branch separato
- Fai backup prima di aggiornare

### 🔧 Per Tool/Asset Non Critici
- Va bene usare `"latest"`
- Esempi: Poiyomi, VRCFury, GoGoLoco

### 🚨 Per SDK e Componenti Critici
- Usa sempre versioni specifiche
- Esempi: com.vrchat.base, com.vrchat.avatars
- Aggiorna solo quando necessario

## Esempio Workflow

```powershell
# 1. Cerca il package
vpm search vrcfury

# 2. Vedi le versioni disponibili
vpm show package com.vrcfury.vrcfury

# 3. Aggiungi nel wizard con versione specifica
# Nel wizard: A → com.vrcfury.vrcfury → 1.983.0

# 4. Salva e usa!
```
