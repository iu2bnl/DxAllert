# DXAllert

Programma per radioamatori che mostra e segnala le DXpedition attive, quelle on air in tempo
reale (spot dal DX cluster) e quelle pianificate.

## Cosa fa

- **On air**: solo le DXpedition spottate negli ultimi 15 minuti, la più recente in alto, con
  frequenza, banda, modo, spotter e commento dell'ultimo spot. On air, Attive ora e Tempo reale
  hanno la stessa grafica da cluster web e una colonna "QRZ" con il link alla scheda del nominativo.
- **Attive ora**: le DXpedition in corso, con l'etichetta ON AIR per quelle spottate negli
  ultimi 15 minuti, ultimo spot, QSL, distanza e azimut dal proprio locator.
- **Tempo reale**: gli spot del DX cluster in stile cluster web: bandiera, nominativo DX in rosso
  e spotter in verde, continente con numero DXCC e paese di entrambi; l'imbuto accanto alla banda
  filtra su quella banda, le icone aprono la scheda su QRZ.com o HamQTH. Filtri per banda, modo
  e frequenza.
- **Pianificate**: calendario mensile (sei settimane da lunedì) con le DXpedition che iniziano
  in ciascun giorno, colorate per continente, e il numero di quelle attive; cliccando un giorno
  si vede a destra l'elenco completo. In alternativa la tabella, con filtri per mese,
  continente ed entità.
- **Mappa**: tutte le operazioni sulla mappa del mondo, con confini e nomi degli stati in italiano
  (disegnati dal programma, funzionano anche senza internet); da zoom 6 in su compaiono le
  tessere OpenStreetMap per i dettagli. Nelle schede On air, Attive ora e
  Tempo reale una minimappa mostra il proprio QTH, la DXpedition selezionata e la linea del
  percorso corto (cerchio massimo) tra i due, con distanza e azimut.
- **QRZ.com**: scheda del nominativo selezionato nel riquadro a destra, ridimensionabile.
- **Log ADIF**: importando il proprio log, la colonna "Già collegata" indica nominativi ed
  entità DXCC già fatti, con banda e modo.
- **Avvisi**: una notifica Windows quando una DXpedition attiva compare on air (una sola
  volta, non a ogni spot).

## Compilare e generare DXAllert-Setup.exe (Windows 10/11)

### 1. Installare gli strumenti (una volta sola)

Apri PowerShell ed esegui:

```powershell
winget install Microsoft.DotNet.SDK.8
winget install JRSoftware.InnoSetup
```

Chiudi e riapri PowerShell, poi verifica con `dotnet --version` (deve rispondere 8.0.x).

### 2. Mettere il progetto in una cartella dal percorso corto

Per esempio `C:\DXAllert`. Con percorsi molto lunghi Inno Setup si ferma con
"Impossibile trovare il percorso specificato".

### 3. Generare l'installer

```powershell
cd C:\DXAllert
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

Lo script fa tre cose in ordine:

1. esegue i test automatici (se uno fallisce si ferma);
2. pubblica il programma self-contained per win-x64 nella cartella `publish`
   (non serve installare .NET sul PC di destinazione);
3. compila `installer\DXAllert.iss` con Inno Setup.

Il risultato è `dist\DXAllert-Setup.exe`.

### In alternativa, i tre passi a mano

```powershell
dotnet test tests\DXAllert.Tests -c Release
dotnet publish src\DXAllert.App -c Release -r win-x64 --self-contained true -o publish
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" installer\DXAllert.iss
```

(Se Inno Setup è stato installato per tutti gli utenti, `ISCC.exe` si trova in
`C:\Program Files (x86)\Inno Setup 6`.)

### 4. Installare

Doppio clic su `DXAllert-Setup.exe`. L'installer:

- installa `dxallert.exe` senza chiedere i diritti di amministratore;
- crea il collegamento nel menu Start e, se lasci la spunta, sul desktop;
- offre l'opzione "Avvia DXAllert con Windows";
- avvisa se manca Microsoft Edge WebView2 Runtime (serve solo per la scheda Mappa;
  su Windows 11 c'è già).

L'eseguibile non è firmato digitalmente: al primo avvio Windows SmartScreen può mostrare
"PC protetto da Windows". Scegli "Ulteriori informazioni" e poi "Esegui comunque".

### Provare il programma senza installarlo

```powershell
dotnet run --project src\DXAllert.App
```

## Primo avvio

Apri la scheda **Impostazioni**, inserisci nominativo e locator e premi **Salva**.
Il collegamento al DX cluster parte subito dopo. Chiudendo la finestra il programma resta
nell'area di notifica; per chiuderlo davvero: clic destro sull'icona, poi **Esci**.

## Dove sono i dati

Tutto in `%AppData%\DXAllert`:

| File | Contenuto |
|---|---|
| `settings.json` | impostazioni |
| `dxallert.db` | database SQLite: cache delle fonti, storico spot (30 giorni), log ADIF importato |
| `cty.dat` | file delle entità DXCC, aggiornato ogni 7 giorni |
| `logs\dxallert-AAAAMMGG.log` | log giornaliero (pulsante "Apri log") |

La disinstallazione non cancella questa cartella.

## Fonti dei dati

| Fonte | Cosa si legge | Note |
|---|---|---|
| DXZone | dati strutturati (JSON-LD) di `/events/dxpeditions/`, `/today/`, `/this-weekend/` | le viste con parametri (`?ym=`) sono vietate dal robots.txt e non vengono usate |
| 425 DX News | calendario `index.php?op=wcal` | il credito "425 DX News" compare nella colonna Fonti |
| NG3K ADXO | feed RSS `adxo.xml` | |
| DX-World | feed RSS | solo nominativo, entità e link: integra le altre fonti |
| DX cluster | telnet, uno o più nodi | riconnessione automatica |
| DXSummit | API JSON | riserva, solo quando nessun nodo telnet è collegato |
| country-files.com | `cty.dat`, `cty.csv` | nominativo -> entità DXCC; numero DXCC (ADIF) |
| K0SWE dxcc-json | `Assets\dxcc.csv` (ricavato una volta) | paese di ciascuna entità, per la bandiera; licenza Apache 2.0 in `Assets\licenses` |
| flagcdn.com | `Assets\flags` | bandiere (pubblico dominio) |
| Natural Earth | `Assets\map\countries.js` (ricavato una volta, 1:50m) | confini e nomi italiani degli stati sulla mappa (pubblico dominio) |

Escluse: **DXMaps** (i termini d'uso vietano l'uso dei dati in programmi) e **DXNews**
(il dominio non risultava raggiungibile; il modulo `DxNewsSource` è pronto ma spento).

Le liste si aggiornano ogni minuto (costante `RefreshInterval` in
`src\DXAllert.App\ViewModels\MainViewModel.cs`); se una fonte non risponde si usa l'ultima
copia in cache. Un intervallo così breve interroga spesso i siti delle fonti: se uno di loro
inizia a rifiutare le richieste, conviene aumentare quel valore.

## Riquadro QRZ.com

Nelle schede "On air", "Attive ora" e "Tempo reale" il riquadro a destra mostra la scheda QRZ.com del
nominativo selezionato (nome, indirizzo, locator, foto, QSL manager, LoTW/eQSL, e-mail).
Utente e password si inseriscono in Impostazioni > QRZ.com.

- Si usa il servizio XML ufficiale di QRZ (`xmldata.qrz.com`), modulo `src\DXAllert.Data\QrzClient.cs`.
- I dati completi richiedono un abbonamento XML di QRZ; senza, arrivano solo quelli di base.
- La password è salvata in `settings.json` cifrata con la protezione dati di Windows (DPAPI):
  si decifra solo con lo stesso utente Windows su quel computer.
- Ogni nominativo viene chiesto a QRZ una sola volta per sessione di lavoro.

## Log ADIF (già collegati)

In Impostazioni > Log ADIF, "Importa file ADIF…" legge il file `.adi` esportato dal proprio
programma di log. Nelle schede "Attive ora", "Tempo reale" e "Pianificate" la colonna
"Già collegata" mostra:

- `✓ 20m FT8 · 15m CW` se quel nominativo è già nel log, con bande e modi;
- `~ 20m SSB` se nel log c'è solo la sua entità DXCC;
- niente se è nuova.

Ogni importazione sostituisce la precedente; "Cancella il log importato" la rimuove.
L'entità DXCC di ogni QSO è ricavata dal nominativo con `cty.dat`, non dal nome scritto nel
file. Se nel QSO manca la banda si usa la frequenza; il sottomodo (per esempio FT4) ha la
precedenza sul modo. Modulo `src\DXAllert.Core\AdifParser.cs`.

## Struttura del progetto

```
src\DXAllert.Core    modelli, interfacce, DXCC, unione delle fonti, regole degli avvisi
src\DXAllert.Data    un file per fonte (cartella Sources), cluster telnet, SQLite
src\DXAllert.App     interfaccia WPF (MVVM), mappa Leaflet in WebView2, tray, notifiche
tests\DXAllert.Tests test dei parser (su campioni reali in Samples) e del riconoscimento DXCC
installer            script Inno Setup
```

Se una fonte cambia formato si corregge solo il suo file in `src\DXAllert.Data\Sources`
e si aggiorna il campione corrispondente in `tests\DXAllert.Tests\Samples`.

## Limiti noti

- L'entità DXCC viene dal nome indicato dalla fonte quando coincide con uno del `cty.dat`,
  altrimenti dal prefisso. Per nominativi nuovi con prefissi ambigui può risultare sbagliata
  finché `cty.dat` non viene aggiornato.
- Bande e modi sono ricavati dal testo libero degli annunci: possono mancare.
- Le voci che arrivano solo da DX-World non hanno date e compaiono in "Pianificate" con "—".
- Il modo degli spot è stimato dal commento o dalla frequenza.
