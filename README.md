# DXAllert

Programma per radioamatori che mostra e segnala le DXpedition attive, quelle on air in tempo
reale (spot dal DX cluster) e quelle pianificate.

## Cosa fa

- **On Air**: gli spot del DX cluster in stile cluster web, con in alto le DXpedition on air negli
  ultimi 15 minuti. Per ogni spot: bandiera, nominativo DX in rosso e spotter in verde, paese e
  numero DXCC di entrambi in colonne separate, commento, "Già collegata" e, per le DXpedition,
  giorni rimanenti, QSL, distanza, azimut e sito. Di base mostra solo gli spot delle DXpedition
  attive; "Mostra tutti gli spot" li mostra tutti. Filtri per banda, modo e frequenza; l'imbuto
  accanto alla banda filtra su quella banda.
- **Attive ora**: le DXpedition in corso, con l'etichetta ON AIR per quelle spottate negli
  ultimi 15 minuti, paese e DXCC, ultimo spot, QSL, distanza e azimut dal proprio locator.
- In entrambe le schede cliccando il nominativo si apre la sua scheda su QRZ.com; le icone
  accanto aprono QRZ.com e HamQTH.
- **Pianificate**: calendario mensile (sei settimane da lunedì) con le DXpedition che iniziano
  in ciascun giorno, colorate per continente, e il numero di quelle attive; cliccando un giorno
  si vede a destra l'elenco completo. In alternativa la tabella, con filtri per mese,
  continente ed entità.
- **Mappa**: tutte le operazioni sulla mappa del mondo, con confini e nomi degli stati in italiano
  (disegnati dal programma, funzionano anche senza internet); da zoom 6 in su compaiono le
  tessere OpenStreetMap per i dettagli. Nelle schede On Air e Attive ora una minimappa mostra
  il proprio QTH, la DXpedition selezionata e la linea del
  percorso corto (cerchio massimo) tra i due, con distanza e azimut.
- **Contest**: calendario dei contest italiani, europei e mondiali (area = chi li organizza), con
  la spunta "Vista calendario" per passare dal mese a una tabella dei prossimi 12 mesi. Filtri per
  area e per nome, modo o banda; per ogni contest orari UTC, durata, modi, bande e link al
  regolamento.
- **Contee USA**: mappa delle contee degli Stati Uniti lavorate secondo il log ADIF importato, per
  il diploma USA-CA di MARAC (marac.org): totale, prossima classe (USA 500, 1000, …, 3077),
  avanzamento per stato, contee del log non riconosciute e città indipendenti.
- **Diplomi**: avanzamento, dal log ADIF, dei diplomi dell'elenco di VA3RJ che si possono calcolare
  da un log (paesi DXCC e WAE, continenti, stazioni e prefissi di un paese, isole IOTA), con classi,
  bande, voci collegate e mancanti e mappa dei paesi.
- **QRZ.com**: scheda del nominativo selezionato nel riquadro a destra, ridimensionabile.
- **Log ADIF**: importando il proprio log, la colonna "Già collegata" indica nominativi ed
  entità DXCC già fatti, con banda e modo.
- **Avvisi**: una notifica Windows quando una DXpedition attiva compare on air (una sola
  volta, non a ogni spot).

## Compilare e generare l'installer (Windows 10/11)

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

Il risultato è `dist\DXAllert<versione>.exe`, per esempio `dist\DXAllert1.1.0.exe`: la versione si cambia solo in `<Version>` di `src\DXAllert.App\DXAllert.App.csproj`, e build.ps1 la passa anche a Inno Setup.

### In alternativa, i tre passi a mano

```powershell
dotnet test tests\DXAllert.Tests -c Release
dotnet publish src\DXAllert.App -c Release -r win-x64 --self-contained true -o publish
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" installer\DXAllert.iss
```

(Se Inno Setup è stato installato per tutti gli utenti, `ISCC.exe` si trova in
`C:\Program Files (x86)\Inno Setup 6`.)

### 4. Installare

Doppio clic su `DXAllert<versione>.exe`. L'installer:

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
| us-atlas (US Census Bureau) | `Assets\map\us-counties.js` | confini delle contee USA (licenza ISC) |

Escluse: **DXMaps** (i termini d'uso vietano l'uso dei dati in programmi) e **DXNews**
(il dominio non risultava raggiungibile; il modulo `DxNewsSource` è pronto ma spento).

Le liste si aggiornano ogni minuto (costante `RefreshInterval` in
`src\DXAllert.App\ViewModels\MainViewModel.cs`); se una fonte non risponde si usa l'ultima
copia in cache. Un intervallo così breve interroga spesso i siti delle fonti: se uno di loro
inizia a rifiutare le richieste, conviene aumentare quel valore.

## Riquadro QRZ.com

Nelle schede "On Air" e "Attive ora" il riquadro a destra mostra la scheda QRZ.com del
nominativo selezionato (nome, indirizzo, locator, foto, QSL manager, LoTW/eQSL, e-mail).
Utente e password si inseriscono in Impostazioni > QRZ.com.

- Si usa il servizio XML ufficiale di QRZ (`xmldata.qrz.com`), modulo `src\DXAllert.Data\QrzClient.cs`.
- I dati completi richiedono un abbonamento XML di QRZ; senza, arrivano solo quelli di base.
- La password è salvata in `settings.json` cifrata con la protezione dati di Windows (DPAPI):
  si decifra solo con lo stesso utente Windows su quel computer.
- Ogni nominativo viene chiesto a QRZ una sola volta per sessione di lavoro.

## Log ADIF (già collegati)

In Impostazioni > Log ADIF, "Importa file ADIF…" legge il file `.adi` esportato dal proprio
programma di log. Nelle schede "On Air", "Attive ora" e "Pianificate" la colonna
"Già collegata" mostra:

- `✓ 20m FT8 · 15m CW` se quel nominativo è già nel log, con bande e modi;
- `~ 20m SSB` se nel log c'è solo la sua entità DXCC;
- niente se è nuova.

Ogni importazione sostituisce la precedente; "Cancella il log importato" la rimuove.
L'entità DXCC di ogni QSO è ricavata dal nominativo con `cty.dat`, non dal nome scritto nel
file. Se nel QSO manca la banda si usa la frequenza; il sottomodo (per esempio FT4) ha la
precedenza sul modo. Modulo `src\DXAllert.Core\AdifParser.cs`.

## Calendario dei contest

- Le date vengono dalla regola abituale di ogni contest (es. "ultimo fine settimana completo di
  ottobre"), scritta in `src\DXAllert.Core\Contests.cs`; per i contest italiani le regole sono
  quelle dei regolamenti ARI 2026. Gli organizzatori possono cambiare le date: il regolamento
  dell'anno resta il riferimento.
- La settimana in corso è completata con il feed iCal del WA7BNM Contest Calendar
  (contestcalendar.com), scaricato al più ogni 6 ore: le sue condizioni d'uso permettono di
  caricare il feed in un calendario per uso personale. Il feed dà orari esatti e anche i contest
  minori, che compaiono come "Altri".

## Contee USA (MARAC USA-CA)

La scheda legge dal log importato i campi `STATE` e `CNTY` dei QSO con Stati Uniti, Alaska e
Hawaii (`CNTY` nel formato ADIF `MA,Middlesex`; vanno bene anche `MA, Middlesex` o solo
`Middlesex` con lo stato in `STATE`). Se il log è stato importato prima della versione 1.0.9 va
importato di nuovo, perché allora stato e contea non venivano salvati.

Le contee seguono le regole USA-CA (revisione aprile 2025): le contee dei 48 stati, le parrocchie
della Louisiana, i 4 distretti giudiziari dell'Alaska e le contee storiche del Connecticut. Le
città indipendenti (Virginia, Baltimore City, St. Louis City) non sono contee: compaiono a parte,
perché le regole permettono di dichiarare una contea adiacente. Washington DC e i territori non
fanno parte del diploma.

- Confini: us-atlas 3.0.1 (dati US Census Bureau, licenza ISC), libreria topojson-client (ISC);
  licenze in `Assets\licenses`.
- In Alaska ogni borough è assegnato al suo distretto giudiziario: i confini dei distretti seguono
  quasi sempre quelli dei borough, ma non in ogni dettaglio.
- Con queste regole le contee sono 3076, una in meno del 3077 che dà il nome al diploma: le
  regole pubblicate non dicono quale caso particolare MARAC conta in più.
- I nomi del log sono confrontati ignorando maiuscole, accenti, spazi, punteggiatura e parole come
  "County" o "Parish" ("Saint" vale "St."); alcuni vecchi nomi sono riconosciuti (Dade →
  Miami-Dade, Shannon → Oglala Lakota). Le voci che non corrispondono a nessuna contea sono
  elencate nella scheda.

## Diplomi

La scheda calcola dal log importato l'avanzamento di 38 diplomi dell'elenco
[qsl.net/va3rj/awards_dx.html](https://www.qsl.net/va3rj/awards_dx.html), con le regole lette sulle
pagine degli enti (novembre 2026): DXCC, 5 e 9 bande ed Elite della WIA, 101 Países, DX 100/200/300,
CT DX 100, EADX100, Toplist ARI, WNH/WSH, Formula I, WAE, WA EU, The Europe Award, Balkan, C.D.M.,
Euro Countries, WANC, Zone 40, Alpine Country, WAC, 5BWAC, CCC, ADXA, W.A.A.C., WACS, TPA, IOTA,
W-100-I, WAOE 100, W 100 OM, LA 100, YV 100/200/300, ZP 100-500, EU-PX-A, Prefixes-SP, CT-WPX.

- Entità, continenti e paesi WAE (Sicilia, Shetland, Bear Island, 4U1V, Turchia europea) vengono
  dal nominativo con il cty.dat; date (`QSO_DATE`), conferme (`QSL_RCVD`, `LOTW_QSL_RCVD`,
  `EQSL_QSL_RCVD`) e `IOTA` dal file ADIF. I log importati prima della versione 1.1.2 vanno
  importati di nuovo per avere date, conferme e IOTA.
- I filtri limitano il conteggio a un modo (CW, fonia, digitali), a una banda o ai QSO confermati.
- Gli altri diplomi dell'elenco chiedono riferimenti che un log non contiene (castelli, parchi,
  comuni, province, fari…) oppure valevano solo in un periodo già passato.
- Le regole nella scheda sono riassunte: prima di fare domanda va letto il regolamento dell'ente.

**Diplomi da Log4OM.** Log4OM scrive in ogni QSO i riferimenti dei diplomi che segue (campo
`APP_L4ONG_QSO_AWARD_REFERENCES`, per esempio WAIP, WCA, WAS, WAZ, POTA, JCC). DXAllert mostra un
diploma per ogni codice presente nel file (gruppo «Da Log4OM») con i riferimenti collegati e, per
quelli più noti, il totale. Per vedere un diploma in più basta attivarlo in Log4OM, esportare di nuovo
il log e reimportarlo.

I diplomi di hamlog.online per eventi speciali (oltre mille, quasi tutti a tempo) non si calcolano dal
proprio log: hamlog li verifica sui log caricati dalle stazioni speciali, cercando il nominativo nella
pagina del diploma.

## Aggiornamenti automatici

All'avvio (dopo 20 secondi) e poi una volta al giorno DXAllert legge l'ultima release pubblicata
su GitHub (`api.github.com/repos/mattianobis/DxAllert/releases/latest`). Se è più nuova compare
una barra verde in alto con «Aggiorna ora», «Novità», «Più tardi» e «Salta questa versione».

«Aggiorna ora» scarica l'installer `DXAllert<versione>.exe` (solo da
`github.com/mattianobis/DxAllert/releases/download/…`) e controlla dimensione e impronta SHA-256
indicate da GitHub. Poi chiude il programma e lancia l'installer in modalità silenziosa
(`/SILENT /AUTOUPDATE=1`, per tutti gli utenti se DXAllert è in Programmi). A fine installazione
l'installer riapre DXAllert. Il controllo si disattiva in Impostazioni > Aggiornamenti.

Perché il programma veda le release, il repository deve essere **pubblico** (oppure le release vanno
pubblicate in un repository pubblico a parte). Per pubblicare una versione:

1. alzare `<Version>` in `src/DXAllert.App/DXAllert.App.csproj` ed eseguire `.\build.ps1`;
2. su GitHub, *Releases > Draft a new release*, tag `v<versione>` (es. `v1.1.3`);
3. allegare `dist\DXAllert<versione>.exe` e pubblicare (non come pre-release).

## Cartella dati separata

Con la variabile d'ambiente `DXALLERT_DATA_DIR` il programma usa un'altra cartella per
impostazioni, database, cache e log, e può girare accanto all'istanza normale (serve per provare
una nuova versione senza toccare i propri dati):

```powershell
$env:DXALLERT_DATA_DIR = "C:\DXAllert-prova"; & ".\publish\dxallert.exe"
```

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
