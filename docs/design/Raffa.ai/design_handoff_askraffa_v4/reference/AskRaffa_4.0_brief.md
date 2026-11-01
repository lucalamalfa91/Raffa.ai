# AskRaffa 4.0 — Prompt definitivo per Claude Design e handoff allo sviluppatore

**Versione consolidata · 23 ottobre 2026**

Questo documento unifica `AskRaffa_4.0_Prompt_Claude_Design.md` e `redesign-v4-prompt.md`, applicando le decisioni di prodotto già definite. Sostituisce i due brief come riferimento per il redesign. Le indicazioni tecniche sono raccolte nella sezione dedicata allo sviluppatore.

---

## 1. Incarico e risultato atteso

Agisci come Product Designer senior. Riprogetta AskRaffa 4.0 nel progetto esistente e realizza un **prototipo navigabile dell'intero prodotto**, con un rinnovo completo che attraversi tutte le aree pertinenti.

AskRaffa aiuta le aziende a gestire contratti, offerte e rinnovi e a negoziare condizioni migliori. L'esperienza attuale contiene molte informazioni, ma spesso rende difficile capire dove guardare e cosa fare. Il redesign deve trasformarle in un percorso comprensibile e operativo.

In pochi secondi l'utente deve capire:

1. Dove si trova e su quale contratto o pratica sta lavorando.
2. Cosa richiede attenzione e perché.
3. Quale decisione o azione viene richiesta adesso.
4. Chi deve compiere il prossimo passo.
5. Come approfondire, cambiare strada o riprendere più tardi.

**Conserva informazioni e funzioni; riduci ciò che compete contemporaneamente per l'attenzione.** Mantieni l'identità visiva esistente dove disponibile. Ripensa gerarchia, navigazione e interazioni senza aggiungere integrazioni, KPI o capacità non richiesti.

Il perimetro comprende i comportamenti già concordati, incluso il rinnovo fino alla firma e all'acquisizione del nuovo contratto. Se un comportamento previsto non è ancora implementato, rappresentalo nel prototipo e segnalalo nell'handoff come requisito da implementare. Non descriverlo come già disponibile.

## 2. Decisioni definitive

| Tema | Decisione da applicare |
|---|---|
| Punto di ingresso | Dopo il sign-in si atterra su **Home**, evoluzione di Today. AskRaffa resta sempre raggiungibile. |
| Priorità informativa | Mostrare prima ciò che cambia la decisione o l'azione. Tutto il resto rimane accessibile negli approfondimenti. |
| Spazio operativo | **AskRaffa è lo spazio operativo guidato.** Home, elenchi e dettagli orientano e aprono la conversazione pertinente. |
| Flussi | Un'unica pratica e un unico stato, anche quando l'utente entra da sezioni diverse. Nessuna checklist operativa parallela alla chat. |
| Guida | Le attività sono strutturate in passi; consultare un contratto o fare una domanda semplice non richiede un wizard obbligatorio. |
| Home | Fino a **3–5 priorità reali**, senza riempire artificialmente l'elenco. Accessi chiari a Rinnovi, Preventivi, Portfolio e AskRaffa. |
| Dati economici | Conservare prezzi contrattuali, offerte e obiettivi indicati dall'utente. **Nessun benchmark di mercato o risparmio inventato.** |
| Email | Bozza, approvazione, copia e invio manuale sono passaggi distinti. Nessun invio o lettura automatica della casella nel primo rilascio. |
| Fine negoziazione | Solo dopo la firma del nuovo contratto e la relativa conferma autorizzata con documento. L'accordo sui termini non basta. |
| Nuovo contratto | Entra nel processo standard di acquisizione, estrazione, review e validazione, collegato alla pratica e al contratto precedente. |
| Risparmi | Nel redesign diventano una **vista del Portfolio**, raggiungibile anche dai risultati dei rinnovi. Si conservano storico, funzioni e collegamenti esistenti. |
| Modalità esperto | Non introdurre una seconda esperienza completa. Usare dettagli, filtri e colonne aggiuntive opzionali nella stessa interfaccia. |
| Permessi | Scorciatoie e azioni rispettano i permessi esistenti. Partecipare alla chat non attribuisce automaticamente poteri di approvazione. |

Le scelte sopra non sono domande da riaprire durante il design. Le sole ipotesi da segnalare riguardano dettagli non definiti o capacità tecniche da verificare.

## 3. Punto di partenza e problemi da risolvere

Le aree da preservare sono Home/Today, AskRaffa, Documenti/Review, Portfolio/Contract 360, Rinnovi, Preventivi/Quote check, Risparmi e Workspace.

Il secondo brief descrive un prototipo v3 con identità “Modernist”, IT/EN e dati dimostrativi Helvetia Foods, e riferisce una SPA in `web/src/routes/`. Questi riferimenti sono contesto fornito, non una verifica del repository eseguita per questo documento. Prima dell'implementazione vanno confrontati con il codice disponibile.

| Area | Problema da affrontare | Direzione del redesign |
|---|---|---|
| Shell | Barra demo, guida, coach e drawer possono sovrapporsi. | Una navigazione stabile, aiuto nel passo corrente e un solo accesso generale ad Aiuto. |
| Home/Today | Onboarding, coda di lavoro e molte azioni competono. | Priorità del giorno, prossima azione e ripresa del lavoro; onboarding contestuale. |
| Rinnovi | KPI, viste, tab, timeline, email e dati del caso tutti insieme. | Elenco essenziale; lavoro nella chat; dettagli del caso su richiesta. |
| Chat rinnovo | Buona base a passi, ma metadati e storico possono oscurare il lavoro corrente. | Un passo attivo, contesto compatto e riepilogo persistente. |
| Contract 360 | Sezioni lunghe senza separare decisioni e consultazione. | “Cosa richiede una decisione” in cima; condizioni complete negli approfondimenti. |
| Portfolio | Troppe colonne e informazioni allo stesso livello. | Elenco essenziale, ricerca, filtri e colonne aggiuntive opzionali. |
| Documenti/Review | Tutti i campi e le soglie di confidenza esposti insieme. | Review delle eccezioni e dei dati decisivi, con possibilità di consultare tutto. |
| Preventivi | Verdetto, confronto, azioni e tabella completa nella stessa pagina. | Caricamento, verifica, confronto disponibile e decisione, in sequenza. |
| Risparmi | KPI, obiettivi, filtri e potenziali opportunità mescolati. | Risultati verificabili prima; metodo e dettaglio accessibili. |

Preserva e generalizza i pattern utili: priorità ordinate per urgenza, passi già svolti consultabili, termini standard fuori dal focus, fonti accessibili, tooltip brevi e stato salvato delle pratiche.

## 4. Regola globale: impatto sulla decisione

**Mostra prima ciò che cambia la decisione o l'azione dell'utente.**

In primo piano:

- Condizioni negoziabili o da chiarire.
- Rischi, discrepanze e dati mancanti che impediscono una decisione affidabile.
- Scadenze, obblighi e preavvisi che limitano l'azione, anche se non negoziabili.
- SLA rilevanti e conseguenze di eventuali disservizi.
- Prossima azione pertinente alla fase corrente.

Negli approfondimenti:

- Condizioni standard o già accettate che non richiedono intervento.
- Informazioni descrittive, storico completo e documentazione di supporto.
- Tabelle complete, dettagli di calcolo e dati già verificati.

La priorità è contestuale. Un dato secondario deve emergere quando diventa decisivo. Un termine di disdetta o un conflitto sulla data di scadenza non può essere nascosto per rispettare un limite di densità.

### Scheda di un punto rilevante

Ogni punto deve rendere comprensibili:

1. **Cosa dice il documento:** condizione attuale o proposta.
2. **Perché conta:** conseguenza concreta per il cliente.
3. **Su cosa si basa:** fonte contrattuale, policy o obiettivo dell'utente.
4. **Cosa propone Raffa:** modifica o chiarimento motivato.
5. **Cosa decidere:** “Lo negoziamo”, “Lo accettiamo”, “Va chiarito”, quando applicabile.

Queste tre decisioni riguardano il singolo punto negoziale. Non sono stati della pratica e non equivalgono ad approvare un'email o un contratto.

Esempio: un aumento automatico del 5% può essere evidenziato rispetto a un obiettivo aziendale del 3%. Il 3% va etichettato come obiettivo, non come benchmark. Oggi non disponiamo di benchmark di mercato affidabili: il design deve funzionare pienamente anche senza di essi.

## 5. Gerarchia, guida e navigazione

### Una struttura comune

- Una domanda principale per vista e una CTA primaria per il passo attivo.
- Una o due azioni secondarie visibili; le altre in un menu chiaramente riconoscibile.
- Indicativamente non più di 3–4 gruppi informativi principali nella vista iniziale desktop. È un vincolo di gerarchia, non un motivo per nascondere un blocco critico.
- Dettagli, fonti, storico e filtri avanzati accessibili su richiesta.
- Colori d'allerta riservati a urgenze, rischi o blocchi che richiedono attenzione.
- Terminologia, componenti e stati coerenti in tutte le aree.

### Guida operativa nella chat

Ogni attività mostra il passo corrente, il risultato atteso, il prossimo passo e l'eventuale persona da cui si attende un intervento. Lo storico dei passi resta espandibile.

I comandi devono descrivere l'azione: “Conferma piano”, “Approva questa versione”, “Conferma dati”. Evita un generico “Avanti” quando nasconde l'effetto dell'azione.

L'utente può:

- Chiedere un chiarimento in qualsiasi momento.
- Aprire una fonte o un dettaglio e tornare allo stesso punto.
- Modificare risposte precedenti, con evidenza delle conseguenze.
- Salvare e uscire, oppure riprendere da Home o dalla pratica.
- Tornare indietro senza cancellare silenziosamente decisioni o azioni già registrate.

Non forzare una sequenza artificiale per leggere un contratto, cercare un dato o fare una domanda semplice. Il percorso guidato raccoglie soltanto i parametri necessari.

### Navigazione principale

Rail: **Home · AskRaffa · Rinnovi · Preventivi · Portfolio · Documenti**.

- Risparmi è una vista riconoscibile del Portfolio, con collegamenti contestuali dai rinnovi.
- Aiuto e Workspace/membri restano nella parte secondaria della navigazione.
- Le conversazioni hanno titoli comprensibili, con fornitore o attività; evitare chat senza nome.
- La barra “Pilot demo”, il cambio persona e il reset appartengono soltanto alla modalità demo.
- Strip, coach e drawer di guida sovrapposti vengono sostituiti da aiuto contestuale e da un unico accesso generale ad Aiuto.

Conserva gli accessi diretti già esistenti: un vecchio collegamento deve aprire il contenuto corretto anche se cambia la collocazione nel rail.

## 6. Home: “Cosa devo fare oggi?”

Home è il punto di ingresso dopo il sign-in.

### Workspace con attività

La prima area mostra fino a cinque priorità reali, ordinate per urgenza, impatto e possibilità di agire. Se esiste una sola priorità, mostrarne una.

Ogni voce contiene:

- Contratto o fornitore.
- Motivo concreto dell'attenzione.
- Termine utile per agire, con etichetta esplicita.
- Prossima azione e un solo pulsante principale.
- Responsabile o persona attesa, quando rilevante.

Distingui ciò che richiede una mia azione da ciò che è in attesa di un collega o del fornitore. Non presentare tutti i rinnovi come urgenti.

Una seconda area compatta offre quattro accessi d'intento:

- **Rinnovi:** cosa devo rinnovare e quando?
- **Preventivi:** quali condizioni di questa offerta devo valutare?
- **Portfolio:** quali contratti abbiamo e cosa prevedono?
- **Chiedi a Raffa:** ho una domanda o un'attività.

Gli accessi aprono direttamente la sezione o la conversazione pertinente. Non avviano un wizard se l'intento è soltanto consultare. Mantengono peso visivo inferiore alla priorità attiva.

“Riprendi” riporta al lavoro interrotto; se è già presente nelle priorità, non duplicarlo.

### Stati alternativi

- **Nessuna urgenza:** dichiararlo e proporre un'attività utile, senza inventare alert.
- **Workspace vuoto:** dare priorità a “Carica il primo contratto”, con il percorso Carica → Elabora → Verifica → Usa in Raffa.
- **Primo documento in elaborazione:** mostrare lo stato e cosa accadrà dopo, senza riproporre l'onboarding da zero.
- **Utente privo di permessi:** spiegare chi può completare il passo; mantenere accessibile la consultazione consentita.

Non riempire la Home di KPI, grafici o di una seconda lista completa dei rinnovi.

## 7. Rinnovi: elenco essenziale, pratica nella chat

### Elenco

L'elenco deve far capire cosa richiede un intervento, cosa è in corso e cosa è in attesa.

Ogni riga/card rende riconoscibili:

- Fornitore e contratto.
- **Termine di preavviso** e **scadenza/fine del contratto**, distinti.
- Sintesi delle condizioni rilevanti, inclusi gli SLA quando incidono sulla decisione.
- Stato, responsabile e prossima azione.

Usa una sintesi compatta e un dettaglio espandibile; non trasformare ogni riga in una tabella estesa. Dai priorità a circa cinque elementi nella prima vista, lasciando il resto facilmente consultabile. Il limite della Home non è un limite al numero di rinnovi accessibili.

Raggruppamenti possibili: “Richiede azione”, “In corso”, “In attesa”, “Da avviare”. Spiega l'ordinamento per urgenza. Ricerca e filtri sono disponibili; al massimo tre indicatori esistenti possono servire da scorciatoie, solo se aiutano davvero a scegliere.

Le azioni di gruppo compaiono dopo la selezione delle righe. Non devono competere con la prossima azione del singolo rinnovo.

### Apertura della pratica

“Avvia rinnovo” o “Riprendi” apre la chat della pratica esatta. Il riepilogo del caso mostra il passo corrente; termini, SLA, obiettivi, timeline, email, documenti e team rimangono negli approfondimenti.

Non mostrare contemporaneamente tutte le tab, tutti i KPI e tutte le fasi. Non creare un percorso alternativo nel dettaglio contratto.

## 8. Portfolio e Contract 360

### Portfolio

Tre livelli nella stessa area:

1. **Sintesi:** contratti che richiedono attenzione e, se utili, fino a quattro aggregati già disponibili e verificabili.
2. **Elenco completo:** fornitore/contratto, scadenza pertinente, valore se disponibile, stato. Altre colonne e filtri sono opzionali.
3. **Dettaglio contratto:** decisioni rilevanti prima, informazioni complete su richiesta.

Il riepilogo non deve impedire di raggiungere subito un contratto tramite ricerca o elenco. Mostra chiaramente se si sta leggendo la scadenza o il termine di preavviso.

### Contract 360

In alto:

- Identità, versione e stato del contratto.
- Date decisive.
- “Cosa richiede una decisione”: condizioni da discutere, rischi, discrepanze, SLA rilevanti.
- Prossima azione, che apre o riprende la chat corretta.

Negli approfondimenti conserva tutte le informazioni delle sezioni esistenti: termini principali, prodotti e prezzi, clausole, obblighi, rischi, leve negoziali, documenti e storico.

Una clausola deve mostrare testo o sintesi fedele, riferimento alla fonte e motivo della proposta. Aprire la fonte non deve far perdere il contesto.

Se una data o una condizione è in conflitto, mostra **“Da verificare”** e guida alla verifica. Non scegliere silenziosamente il valore più conveniente. Non mettere in evidenza risparmi teorici o un “market check” privo di dati attendibili.

## 9. AskRaffa: spazio di lavoro condiviso

### Ingresso generale

La schermata iniziale offre intenti, per esempio:

- Riassumi un contratto.
- Prepara un rinnovo.
- Confronta fornitori o offerte.
- Trova una clausola.
- Analizza i risultati dei rinnovi.
- Altro.

Il testo libero è sempre disponibile. Raccogli i parametri mancanti soltanto quando servono: quale contratto, quale offerta, quale periodo. Non ripetere richieste se il contesto è già noto.

Toggle avanzati ed eventuali opzioni esistenti vanno in un menu secondario. Non introdurre capacità di ricerca o integrazione soltanto perché compare un'opzione grafica.

### Conversazione di pratica

Ogni pratica ha una conversazione persistente e condivisa, con un responsabile e colleghi partecipanti.

Mantieni un riepilogo compatto di:

- Contratto e fornitore.
- Fase corrente.
- Prossima azione e persona attesa.
- Scadenza rilevante.
- Punti aperti e condizioni concordate, espandibili.

Le risposte seguono una struttura riconoscibile: **risposta breve → evidenze accessibili → azione consigliata**. La provenienza e un'eventuale incertezza restano visibili; i dettagli delle citazioni possono essere espansi.

Una domanda a Raffa riceve una risposta. **Non deve diventare silenziosamente un commento interno.** Se esistono commenti tra colleghi, distinguili con una modalità esplicita e riconoscibile.

Dopo una domanda libera, Raffa ripropone il passo lasciato in sospeso. Non serve rileggere tutto lo storico per capire cosa fare.

## 10. Rinnovo completo, dall'avvio al nuovo contratto

La struttura comprende **Avvio · Piano · Negoziazione · Accordo · Contratto e firma · Acquisizione**. Mostra soprattutto il passo corrente; il percorso completo è consultabile.

Questa struttura orienta l'utente, senza imporre un processo rigidamente lineare: possono arrivare nuove offerte, versioni o richieste di chiarimento. Il ciclo delle email si ripete nella negoziazione; “Inviare” non è una fase unica da superare una volta sola.

### 10.1 Avvio e intervista

Il rinnovo può partire da Home, Rinnovi, Portfolio, dettaglio contratto o AskRaffa. Tutti gli ingressi mantengono lo stesso contratto e riusano la pratica pertinente, se già esiste.

Chiedi poche informazioni progressive, precompilate dai dati disponibili:

- Prodotti e quantità da mantenere, aumentare o ridurre.
- Durata desiderata del rinnovo.
- Budget o obiettivo economico indicato dall'utente, anche “Non definito”.
- Clausole e condizioni da cambiare.

Permetti di correggere i dati di partenza o chiedere spiegazioni. Non bloccare il percorso perché manca un target di prezzo.

### 10.2 Piano

Raffa combina policy aziendali disponibili e proposte motivate. Distingui esplicitamente:

- Dato contrattuale.
- Policy aziendale.
- Obiettivo dell'utente.
- Suggerimento di Raffa.

Se manca una policy, dichiaralo; non attribuire alle proposte AI il valore di regole aziendali.

Fai confermare cosa negoziare, cosa accettare e cosa chiarire. Mantieni consultabili le decisioni e le motivazioni. La conferma del piano non approva automaticamente le future email.

### 10.3 Preparazione, approvazione e invio manuale delle email

Raffa non invia email e non legge automaticamente la casella nel primo rilascio.

1. Prepara una bozza nella chat, con oggetto, destinatari se disponibili, testo e allegati da includere.
2. L'utente modifica il testo o chiede una revisione a Raffa.
3. Il responsabile o un delegato esplicito approva la **versione esatta**.
4. “Copia email” permette il trasferimento nella casella dell'utente.
5. L'utente invia manualmente dalla propria casella.
6. Il responsabile o un delegato registra **“Ho inviato questa versione”**.
7. Solo dopo questa conferma la pratica passa in attesa del fornitore.

Rendi inequivocabili le condizioni **Bozza · Da approvare · Approvata · Copiata · Invio confermato**. La copia è un evento, non una prova di invio; può anche non essere necessaria se il testo viene trasferito manualmente.

Qualsiasi modifica invalida l'approvazione della versione precedente. Se il testo è stato modificato fuori da Raffa, consenti di registrare quello effettivamente inviato, con versione e conferma esplicite; non mostrarlo come la vecchia bozza già approvata. Eventuali differenze che richiedono nuova approvazione devono risultare visibili.

Se mancano dati necessari, spiega come aggiungerli. Gli indirizzi di esempio appartengono solo alla demo e non possono sembrare contatti reali. Non usare CTA “Invia email” che suggeriscano un invio automatico.

### 10.4 Attesa e sollecito

Raffa propone una data di sollecito; l'utente può confermarla o cambiarla. Dopo la conferma, il promemoria viene salvato e genera un avviso in Raffa.

La chat mostra cosa si attende e quando ricontrollare. Se arriva una risposta, aggiorna il promemoria pertinente per evitare avvisi già superati.

Un sollecito è una nuova bozza: segue lo stesso ciclo di revisione, approvazione e invio manuale.

### 10.5 Risposta del fornitore e nuova iterazione

L'utente incolla il testo della risposta e allega offerte o contratti. Il sistema chiede conferma del contesto solo se ambiguo.

Raffa evidenzia:

- Cosa è cambiato rispetto alla versione precedente.
- Cosa è accettato, rifiutato o ancora aperto.
- Effetto su obiettivi, clausole, quantità e durata.
- Eventuali dati mancanti o allegati incoerenti.
- Decisione richiesta e risposta suggerita.

Quando emerge uno scambio concreto, per esempio prezzo inferiore in cambio di durata maggiore, chiedi allora limiti e concessioni accettabili. Non obbligare a prevedere tutti gli scenari nell'intervista iniziale.

Il ciclo continua mantenendo versioni, fonti e decisioni consultabili.

### 10.6 Accordo e arrivo del nuovo contratto

“Condizioni concordate” indica un accordo negoziale, **non una negoziazione completata**.

Quando arriva il nuovo contratto:

- Collegalo alla pratica e al contratto precedente.
- Avvia il processo standard di caricamento ed estrazione.
- Confronta la versione ricevuta con i termini concordati.
- Evidenzia differenze, omissioni e punti non verificabili.
- Guida alla decisione: chiedere correzioni, chiarire oppure procedere verso la firma.

Una bozza non diventa un contratto attivo. Non richiedere di caricare di nuovo un file già presente: collega o riusa il documento esistente. Una nuova versione firmata deve invece restare distinguibile dalla bozza.

### 10.7 Firma e completamento della negoziazione

La firma avviene tramite il processo disponibile all'azienda; non simulare un'integrazione di firma elettronica.

Per completare la negoziazione occorrono:

- Documento firmato associato alla pratica.
- Conferma esplicita dell'evento da parte di un utente autorizzato.
- Tracciabilità della versione firmata.

Se manca il contratto firmato, lo stato rimane “In attesa di firma” o quello pertinente. Un'offerta accettata o un'email di accordo non sono sufficienti.

### 10.8 Acquisizione e ingresso nel ciclo standard

La versione firmata segue il processo standard: **caricamento/collegamento → elaborazione → estrazione → review → validazione**. Riusa quanto già elaborato della stessa versione; conserva la storia delle versioni.

La review operativa del rinnovo continua nella chat. Documenti e Portfolio mostrano lo stesso stato e portano allo stesso lavoro.

Distingui sempre:

- **Negoziazione completata:** contratto firmato e conferma registrata.
- **Acquisizione completata:** dati e documento validati.
- **Contratto efficace:** decorrenza raggiunta secondo i termini verificati.

Se la negoziazione è conclusa ma la validazione è aperta, mostra “Negoziazione completata · Dati da validare” e la CTA per completare il lavoro. Non creare un punto morto dopo la firma.

Collega precedente e successore; non archiviare o sostituire il contratto corrente prima di aver verificato la decorrenza. Al termine mostra l'esito, l'accesso al nuovo contratto e le prossime scadenze.

## 11. Collaborazione e percorsi alternativi

### Ruoli nella pratica

| Ruolo | Comportamento dell'interfaccia |
|---|---|
| Responsabile | Visibile come owner della pratica; può approvare email, confermare invii e gestire le deleghe consentite. |
| Delegato esplicito | Può approvare e confermare invii nei limiti della delega; la delega deve essere riconoscibile. |
| Partecipante | Può contribuire, fare domande e proporre modifiche nei limiti dei permessi esistenti; non riceve poteri di approvazione impliciti. |

Mostra autore e momento delle decisioni, versione approvata e chi deve agire. L'essere Admin del workspace non va equiparato automaticamente a una delega negoziale: applicare i permessi effettivi.

Se due colleghi lavorano sulla stessa bozza, evita che una versione superata appaia approvata. Spiega l'aggiornamento e consenti di recuperare il proprio contributo.

### Disdetta

Dalla pratica deve essere possibile preparare la disdetta del contratto corrente:

- Verificare termine, modalità e condizioni sulla fonte disponibile.
- Evidenziare informazioni mancanti o da chiarire.
- Preparare una bozza da approvare, copiare e inviare manualmente.
- Registrare la conferma di invio e mantenere visibili eventuali passi successivi.

Non indicare il contratto come terminato dopo la sola creazione o copia della disdetta. La data di cessazione dipende dai termini e dalle evidenze disponibili.

### Fornitori alternativi

Avvia una valutazione guidata collegata alla pratica, con requisiti, offerte e condizioni disponibili.

Confronta dati reali e segnala ciò che manca. Valutare o scegliere un'alternativa non invia la disdetta, non chiude il contratto attuale e non attiva automaticamente un nuovo contratto. Mantieni visibili preavvisi e obblighi esistenti.

## 12. Documenti e review

Documenti è il luogo per trovare file e vedere lo stato dell'acquisizione. La vista iniziale privilegia “Richiedono attenzione”; “Tutti” rimane raggiungibile.

Le attività di caricamento e review si svolgono nel flusso guidato AskRaffa, anche quando avviate da Documenti:

1. Carica o collega il file.
2. Identifica fornitore, tipo e relazione con un contratto o una pratica.
3. Mostra elaborazione, eventuale risultato parziale e prossimo passo.
4. Guida alla verifica dei campi che richiedono attenzione.
5. Presenta il riepilogo e la conferma di validazione secondo le regole esistenti.

La review dà priorità a **bassa confidenza, conflitti tra fonti, dati mancanti e campi critici per la decisione**. Una confidenza elevata non elimina un'incoerenza e non equivale a validazione.

Presenta un campo o un piccolo gruppo coerente alla volta, con fonte e correzione possibile. “Vedi tutti i dati” permette di controllare anche gli altri campi. Non nascondere definitivamente informazioni e non inventare nuove soglie di validazione.

Non chiedere all'utente di imparare una legenda tecnica delle percentuali per completare la review. Spiega concretamente cosa controllare e perché.

## 13. Preventivi / Quote check

Percorso guidato:

1. **Carica:** preventivo e identificazione del fornitore.
2. **Collega:** contratto/pratica esistente oppure nuova esigenza.
3. **Verifica:** righe estratte, quantità, durata, clausole e dati mancanti.
4. **Confronta:** contratto corrente, altre offerte, policy e obiettivi effettivamente disponibili.
5. **Decidi:** sintesi in una frase e fino a tre punti prioritari, con evidenze accessibili.
6. **Agisci:** chiedi chiarimenti, prepara una controproposta o continua nella pratica pertinente.

La proposta UX copre il comportamento desiderato; l'handoff deve distinguere quanto è già disponibile da quanto richiede implementazione.

Non usare giudizi come “prezzo competitivo”, “sopra la mediana” o “risparmio di mercato” senza basi disponibili e verificabili. In assenza di confronto sufficiente, dire cosa si può valutare e quale informazione manca.

Tabella completa delle righe, mapping e calcoli rimangono negli approfondimenti. Lo storico dei check e lo stato salvato sono accessibili. Una controproposta segue il medesimo ciclo di email manuali del rinnovo.

Se un check avvia una nuova pratica, dichiaralo e mantieni il collegamento con l'offerta; se appartiene a una pratica esistente, riusa quella conversazione.

## 14. Risparmi nel Portfolio

Conserva le funzioni e lo storico dell'area Savings, organizzandoli come vista **Risparmi** del Portfolio e come esito consultabile dal rinnovo.

Mostra prima risultati sostenuti da documenti e decisioni verificabili. Distingui:

- Risultati contrattuali concordati e supportati dalla versione firmata.
- Risparmi realizzati, soltanto se esiste un'evidenza adatta a dimostrarli.
- Obiettivi e opportunità ancora da valutare.

Per ogni valore indica baseline, periodo, valuta, quantità/perimetro e metodo. Una riduzione della spesa dovuta alla riduzione delle quantità non va presentata automaticamente come risultato della negoziazione. Un contratto firmato non dimostra da solo il risparmio già realizzato.

Filtri per fornitore, categoria, responsabile e periodo, risultati per fornitore e storico rimangono disponibili senza occupare tutta la vista iniziale. Eventuali obiettivi vengono raccolti nella chat.

Se i dati non bastano, mostra lo stato incompleto e il passo utile; non riempire la pagina con stime inventate.

## 15. Coerenza dei dati e fonti

Queste regole valgono in tutte le aree:

- Stesso contratto, stessa pratica, stessi dati, stessi permessi e stesso prossimo passo.
- Date di fine, rinnovo, preavviso e decorrenza hanno etichette distinte.
- Una modifica confermata si riflette in tutte le viste.
- Differenze fra contatori hanno un motivo esplicito, per esempio “contratti totali” e “pratiche di rinnovo aperte”.
- Un dato estratto, un dato validato e una proposta AI non sono presentati nello stesso modo.
- Ogni dato critico e ogni confronto hanno una provenienza consultabile.
- Un conflitto mostra le fonti in disaccordo e una procedura di verifica.
- Caricare lo stesso file non crea automaticamente un nuovo contratto o una seconda pratica.

Usa una data corrente coerente. Nella demo adotta un unico orologio di scenario dichiarato, condiviso da Home, Rinnovi, Portfolio, promemoria e timeline. Etichetta chiaramente tutti i dati dimostrativi.

Quando manca una capacità o un'integrazione, mostra il limite e una via praticabile. Non esibire un'azione riuscita se il sistema non l'ha eseguita.

## 16. Stati da progettare esplicitamente

| Stato | Comportamento richiesto |
|---|---|
| Nessun contratto | Onboarding contestuale con prossimo passo utile. |
| Nessuna attività urgente | Messaggio chiaro e accessi utili, senza allarmi artificiali. |
| Elaborazione in corso/parziale | Stato comprensibile, risultati disponibili e possibilità di riprendere. Nessuna percentuale fittizia. |
| Documento illeggibile | Spiegazione del problema e caricamento di una versione alternativa. |
| Dato mancante o fonti in conflitto | Evidenza del punto incerto, fonti e azione di verifica. |
| File duplicato | Riusa/collega quello esistente o distingue una vera nuova versione. |
| Errore o timeout | Messaggio recuperabile e riprova senza perdere input o duplicare azioni. |
| Attesa di collega/fornitore | Chi deve agire, cosa si attende e quando ricontrollare. |
| Permesso insufficiente | Motivo comprensibile e indicazione del ruolo che può procedere. |
| Bozza modificata dopo approvazione | Approvazione precedente non più valida; revisione e nuova approvazione. |
| Aggiornamento concorrente | Versione corrente evidente e recupero delle modifiche non salvate. |
| Contratto diverso dall'accordo | Differenze e prossima decisione prima di procedere. |
| Firma registrata, acquisizione aperta | Negoziazione completata e review ancora da completare nella stessa chat. |
| Operazione non supportata | Limite esplicito e alternativa manuale o percorso disponibile. |

Ogni stato deve spiegare cosa succede, cosa resta salvato e cosa può fare l'utente. Evita pulsanti senza effetto, schermate nere e vicoli ciechi.

## 17. Componenti, accessibilità e direzione visiva

Riusa o definisci componenti comuni:

- **GuidedFlow / WizardShell:** struttura del passo nella chat, avanzamento, azioni e ripresa. Non un secondo sistema operativo fuori dalla chat.
- **PriorityCard:** motivo, scadenza, contesto e prossima azione.
- **CaseSummary:** stato compatto della pratica e persona attesa.
- **DecisionCard:** condizione, impatto, fonte, proposta e decisione.
- **SourceLink / DetailsPanel:** consultazione della fonte o del dettaglio senza perdere il passo.
- **EmailDraft:** versione, modifiche, approvazione, copia e conferma di invio.
- **ReviewField:** dato, origine, motivo della verifica e correzione.
- **EmptyState / ErrorState / Resume:** componenti di recupero e continuità.

La prossima azione deve essere sempre reperibile. Se usi una barra persistente, deve ospitare l'azione del passo e non duplicare altri pulsanti primari.

Interfaccia professionale e leggibile, con tipografia gerarchica, spazio sufficiente e testi brevi. Colori e icone non devono essere l'unico mezzo per comunicare stato o urgenza.

Desktop come esperienza principale, con comportamento responsive su viewport stretto. Dettagli e fonti restano usabili su mobile; composer e CTA non coprono il contenuto.

Prevedi navigazione da tastiera, focus visibile, contrasto AA, nomi accessibili, annunci comprensibili dei cambi di stato e ritorno del focus quando un pannello viene chiuso. Evita catene di drawer o modali annidate.

Mantieni coerente la lingua del progetto. Se esistono IT/EN, conserva entrambe tramite il catalogo i18n; non mescolare lingue nella stessa schermata. Se il prototipo non ha una lingua stabilita, usa inglese per la UI e prepara la corrispondenza italiana nell'handoff.

## 18. Consegna richiesta a Claude Design

### A. Audit essenziale

Per ogni area identifica la domanda principale, le informazioni inizialmente visibili, le azioni e i punti di sovraccarico.

Classifica gli elementi come **Essenziale ora · Utile su richiesta · Ridondante nella presentazione**. Una presentazione ridondante può essere eliminata; il dato o la funzione sottostante devono restare accessibili.

### B. Architettura e prototipo

Consegna:

1. Mappa delle sezioni, dei collegamenti e del contesto mantenuto.
2. Home con priorità, workspace vuoto e stato senza urgenze.
3. Rinnovi, Portfolio, Contract 360, AskRaffa iniziale, Documenti/Review, Preventivi e Risparmi.
4. Rinnovo navigabile dall'intervista alla firma e alla validazione del nuovo contratto.
5. Percorsi di disdetta e valutazione alternative.
6. Componenti riutilizzabili e relative varianti di stato.
7. Breve handoff con decisioni, dipendenze tecniche e requisiti ancora da implementare.

Usa un unico scenario dimostrativo principale, con dati coerenti tra sezioni e versioni. Puoi aggiungere varianti dello stesso scenario per mostrare errori, permessi e percorsi alternativi.

Il prototipo deve permettere almeno queste prove:

- Home → rinnovo → intervista → domanda libera → ritorno al piano.
- Piano → bozza → approvazione → copia, senza passaggio automatico a “inviata”.
- Conferma invio → promemoria → risposta incollata → nuova proposta.
- Accordo → documento con discrepanza → correzione → firma → review → nuovo contratto.
- Portfolio → contratto → stessa pratica e stesso punto del processo.
- Offerta caricata → collegamento al rinnovo → confronto con dati disponibili.
- Disdetta e alternativa, mantenendo separato lo stato del contratto corrente.
- Errore recuperabile e riapertura di un lavoro già salvato.

Non fermarti a wireframe isolati. Non modificare il prodotto reale né presentare le interazioni simulate della demo come integrazioni operative.

## 19. Handoff tecnico allo sviluppatore

Questa sezione disciplina l'eventuale implementazione successiva. Non è una richiesta a Claude Design di modificare il backend o distribuire il prodotto.

### Verifica iniziale nel repository

Il brief tecnico di partenza cita:

- `web/`, React, TypeScript e Vite.
- Route `ask`, `documents`, `contracts`, `renewals`, `savings`, `quotes`, `today`, `workspace`.
- i18n con ID inglesi e catalogo italiano, CSS per area.
- `renewalActions.ts`, case engine, view model e componente `InfoTip`.
- ADR-030/031 per operazioni non disponibili.
- README con indicazione del landing post sign-in.

Verifica nomi, disponibilità e comportamento nel codice: non considerarli confermati solo perché compaiono in questo documento. Riusa logica, componenti e convenzioni effettivamente presenti.

### Vincoli

- La presentazione riusa le regole di business e gli stati esistenti, senza duplicarli nel frontend.
- Non modificare auth, isolamento dei workspace, permessi o pipeline documentale per semplificare il design.
- Mantieni eventuali prerequisiti esistenti: il brief segnala l'abilitazione di Portfolio/Rinnovi/Quote check dopo un contratto validato. Verifica questa regola nel codice. Se applicabile, mostra il motivo del blocco e il percorso per soddisfarlo, evitando una navigazione senza uscita.
- Una nuova esigenza in Quote check non implica automaticamente la rimozione dei gate di prodotto.
- Se un requisito già concordato richiede backend o API aggiuntivi, documenta il gap, il comportamento necessario e la dipendenza. Non compensare con un falso successo nel frontend.
- Navigazione, route e README devono riflettere Home come landing; preserva deep link e accesso alla stessa pratica.
- Una ripresa riuscita richiede persistenza appropriata della pratica condivisa: la sola sopravvivenza al reload non dimostra collaborazione o persistenza tra sessioni.
- Una capability non supportata deve avere un fallback esplicito, riusando il comportamento previsto dal prodotto quando disponibile.

### Sequenza di implementazione suggerita

1. Componenti comuni, stati, continuità del contesto e struttura guidata nella chat.
2. Home, rail, onboarding e ripresa del lavoro.
3. Rinnovi e ciclo delle email nella chat.
4. Collegamento contratto ricevuto/firma/acquisizione e review.
5. Preventivi, Portfolio/Contract 360 e Risparmi.
6. AskRaffa generale, Documenti e coerenza dei percorsi rimanenti.
7. Accessibilità, responsive, verifica completa e rifinitura.

Procedi con modifiche circoscritte. Se compatibile con il progetto, usa un feature flag `ui_v4` per confronto e rollback. Non creare due insiemi indipendenti di dati o pratiche per v3 e v4.

### Verifica

Riusa i test esistenti e aggiungi quelli necessari per comportamenti e regressioni significative: contesto della pratica, permessi, versioni delle email, conferma invio, firma, acquisizione e recupero degli errori. Usa gli strumenti del repository; Vitest/RTL sono indicati dal brief e vanno verificati.

Controlla la densità con review visiva e screenshot prima/dopo su viewport rappresentativi; evita test fragili basati solo sul numero di elementi sopra la piega. Completa lint, typecheck e test pertinenti previsti dal progetto.

## 20. Criteri di accettazione finali

Il redesign è accettabile quando:

1. In pochi secondi capisco cosa conta, perché e cosa fare.
2. La Home mostra al massimo cinque priorità reali e offre accesso diretto alle aree principali.
3. Informazioni e funzioni restano raggiungibili, con una CTA primaria per il passo corrente.
4. Condizioni negoziabili, SLA, scadenze e vincoli decisivi emergono nel momento opportuno.
5. Ogni ingresso apre il contratto, la pratica e l'avanzamento corretti.
6. Il lavoro operativo resta guidato in AskRaffa, anche se avviato altrove.
7. Posso fare una domanda, aprire una fonte e riprendere senza perdere il punto.
8. Un nuovo utente completa caricamento, review e avvio del rinnovo senza istruzioni esterne.
9. Ruoli e deleghe regolano approvazione e conferma di invio.
10. Copiare un'email non la rende inviata; modificare una bozza invalida l'approvazione precedente.
11. Il promemoria viene confermato e resta collegato all'attesa corretta.
12. Accordo sui termini, firma, validazione e decorrenza sono distinti.
13. Nessuna negoziazione risulta completata prima della firma documentata e confermata.
14. Il nuovo contratto entra nel processo standard senza duplicare il file o perdere la relazione con il precedente.
15. Disdetta e alternative non chiudono implicitamente il contratto corrente.
16. Non compaiono benchmark, risparmi o risultati operativi inventati.
17. Date, versioni, contatori e prossime azioni sono coerenti o hanno differenze esplicitamente motivate.
18. Errori, attese, permessi insufficienti e dati mancanti offrono un passo utile.
19. I percorsi si possono interrompere e riprendere; nessuna funzione della versione precedente diventa irraggiungibile.
20. Navigazione, copy IT/EN, accessibilità e responsive sono coerenti; nell'implementazione passano i controlli pertinenti.

**Procedi con il redesign sulla base di questo documento. Per i dettagli non definiti proponi una soluzione motivata e dichiarala nell'handoff, senza riaprire le decisioni già fissate.**

