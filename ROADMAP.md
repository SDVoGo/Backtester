# Roadmap — Backtester PineScript

Obiettivo: leggere script **PineScript** (strategie `strategy()`), eseguirli barra per barra su dati
OHLCV letti da CSV e produrre i risultati del backtest (trade, equity, metriche) in CSV.

```
.pine ──► Lexer ──► Parser (AST) ──► Interprete bar-by-bar ──► Broker simulato ──► CSV
                                          ▲
                              OHLCV da CSV (input)
```

Questo file è la fonte di verità sulla direzione del progetto: aggiornarlo quando una fase avanza
o quando si prende una decisione che cambia il piano (vedi [Registro decisioni](#registro-decisioni)).

## Struttura della soluzione

| Progetto            | Tipo          | Contenuto                                                         |
|---------------------|---------------|-------------------------------------------------------------------|
| `Pine.Core`         | class library | Linguaggio: lexer, parser, AST, interprete, built-in              |
| `Pine.Core.Tests`   | xUnit         | Test di `Pine.Core`                                               |
| `Backtester`        | ASP.NET (API) | Host: endpoint per lexer/parser, documentazione su `/docs`        |
| `Backtester.Engine` | class library | Broker simulato, `strategy.*`, lettura OHLCV da CSV, `Backtest.Run` |
| `Backtester.Engine.Tests` | xUnit   | Test del motore, dei report e dei golden test                     |
| `Backtester.Cli`    | console       | Comando `backtest`: esegue una strategia e scrive i CSV           |

La documentazione delle fasi e della teoria (`Backtester/Resources/docs.html`, servita su `GET /docs`)
va aggiornata insieme a questo file quando una fase avanza.

## Subset di Pine supportato (MVP)

Pine v5/v6 completo è troppo grande per partire: si supporta un sottoinsieme e lo si allarga.

**Dentro:** letterali, operatori, ternario, operatore storico `x[n]`, `na`, dichiarazioni con tipo
opzionale, `var`/`varip`, `:=` e assegnazioni composte, `if`/`else`, `for`, `for ... in`, `while`,
`switch`, funzioni utente (`=>`) con ritorno di tuple, destrutturazione `[a, b] = f()`,
`input.*` (valore di default o override), `math.*`, `ta.*` più usati, `strategy()` e `strategy.*`.

**No-op (accettati e ignorati):** tutto ciò che disegna — `plot`, `plotshape`, `plotcandle`, `fill`,
`barcolor`, `bgcolor`, `hline`, `line.*`, `label.*`, `box.*`, `table.*`, `linefill.*`, `alert*`.

**Fuori (per ora):** `request.security` multi-timeframe, `import` di librerie, UDT (`type`),
`method`, `enum`, `map`/`matrix`, `varip` con semantica realtime.

## Fasi

### Fase 0 — Fondamenta ✅
- [x] Separazione in progetti (`Pine.Core`, `Pine.Core.Tests`, host `Backtester`)
- [x] Lexer riscritto come scanner manuale (niente regex, complessità lineare)
- [x] Token `NEWLINE` / `INDENT` / `DEDENT` (blocchi a indentazione, multipli di 4 spazi)
- [x] Righe di continuazione: dentro `(`/`[` e righe indentate con non-multiplo di 4
- [x] Keyword riservate (`and or not if else for in to by while switch var varip true false ...`)
- [x] Operatori Pine mancanti (`=>`, `*=`, `/=`, `%=`), rimossi quelli non-Pine (`&& || ++ -- ; { }`)
- [x] Numeri `.5`, `1.`, `1e-3`; stringhe con apici singoli ed escape; colori `#RRGGBB[AA]`
- [x] Commenti a fine riga; direttiva `//@version=N` come token `VERSION`
- [x] Riga/colonna su ogni token ed errori con posizione
- [x] Test xUnit del lexer

### Fase 1 — Parser ✅
- [x] AST (record C#, `Pine.Core.Syntax`) per espressioni e istruzioni, con posizione su ogni nodo
- [x] Parser a discesa ricorsiva per le istruzioni, Pratt per le espressioni (`Pine.Core.Parsing`)
- [x] Precedenze Pine: `?:` < `or` < `and` < `== !=` < `< <= > >=` < `+ -` < `* / %` < unari < `[]`/chiamata/`.`
- [x] Argomenti nominali (`title = "x"`), dichiarazioni multiple separate da virgola su una riga
- [x] Dichiarazioni tipizzate (`var float x`, `float[]`, `array<float>`), tuple `[a, b] = f()`
- [x] `if`/`else if`/`else`, `switch` con e senza soggetto, `for ... to ... by`, `for ... in`, `while`, `break`/`continue`
- [x] `if`/`switch`/`for`/`while` usabili come valore (`x = if ...`)
- [x] Funzioni utente su una riga o a blocco, parametri tipizzati e con default
- [x] Errori di sintassi con riga/colonna; `AstPrinter` (S-expression) per test e debug
- [x] Test su `test002.pine` e sulla strategia di esempio `strategy001.pine`; endpoint `api/parser/parse`

### Fase 2 — Interprete (modello di esecuzione Pine) ✅
- [x] Esecuzione dello script una volta per barra (`Pine.Core.Runtime.Interpreter`)
- [x] Ogni variabile è una **serie** (`Series`, ring buffer fino a `max_bars_back` = 5000) con storia via `x[n]`
- [x] `x[n]` su variabili, built-in (`close[1]`), parametri di funzione ed espressioni qualsiasi (`(a + b)[1]`)
- [x] `var`/`varip` persistenti tra barre; variabili normali reinizializzate ad ogni barra
- [x] Stato **per punto di chiamata** (`InstanceState`) per funzioni utente e built-in con memoria
- [x] Propagazione di `na`; tipi `int`, `float`, `bool`, `string`, `color`, tuple (`Value`)
- [x] Funzioni utente: argomenti nominali e di default, tuple di ritorno, divieto di ricorsione e di modifica dei globali
- [x] `if`/`switch`/`for`/`for in`/`while` con valore, `break`/`continue`
- [x] Variabili built-in: `open high low close volume hl2 hlc3 ohlc4 time bar_index last_bar_index barstate.*`
- [x] Registro built-in estendibile (`BuiltinRegistry`); `na()`, `nz()`, `plot()` (registra i valori), `ta.sma` di riferimento
- [x] Errori di runtime con riga, colonna e barra (`PineRuntimeException`)

### Fase 3 — Built-in ✅
- [x] `input.*` (`int float bool string color source timeframe session symbol price time`): default o override per titolo, validato su `minval`/`maxval`/`options`; valori usati in `ExecutionResult.Inputs`
- [x] `math.*` (variadiche `max min avg`, `round` con precisione, `sum`, trigonometria, costanti), `nz`, `na`, `fixnan`
- [x] `str.*` (`tostring` con formati `#.##`/`format.*`, `format` stile MessageFormat, `tonumber`, `length`, `contains`, ...)
- [x] `color.*` (palette, `new`, `rgb`, `r g b t`, `from_gradient`): i colori non disegnano ma vanno valutati
- [x] `ta.sma ema rma wma vwma tr atr rsi stdev highest lowest crossover crossunder cross change mom roc rising falling cum barssince valuewhen macd bb supertrend`
- [x] Funzioni grafiche e di alert come no-op; namespace di disegno (`line.*`, `label.*`, `box.*`, `table.*`, `shape.*`, ...) no-op in blocco
- [x] `test002.pine` eseguito per intero (test di integrazione)
- [x] Tempo: `timestamp()` (numerico, con fuso, da stringa), `year month dayofmonth dayofweek hour minute second weekofyear` come variabili e funzioni, `time_close`, `dayofweek.*`
- Rimandati: `ta.pivothigh/pivotlow` (semantica da verificare su TradingView), `syminfo.*` (tranne `mintick` e `timezone`)

### Fase 4 — Motore di backtest (`Backtester.Engine`) ✅
- [x] Lettura OHLCV da CSV (`OhlcvCsv`): colonne per nome, separatore auto, tempo unix s/ms o data ISO
- [x] `strategy()` (capitale iniziale, qty di default fixed/cash/percent_of_equity, commissioni, slippage, pyramiding, `process_orders_on_close`)
- [x] `strategy.entry` (inversione, pyramiding), `strategy.order`, `strategy.exit` (limit/stop, profit/loss in tick, trailing, qty/qty_percent), `strategy.close` (`immediately`), `strategy.close_all`, `strategy.cancel`, `strategy.cancel_all`
- [x] Esecuzione di default all'open della barra successiva; stop/limit/stop-limit lungo il percorso intrabar open → estremo vicino → estremo lontano → close; gap eseguiti all'open
- [x] `strategy.position_size position_avg_price equity netprofit openprofit opentrades closedtrades wintrades losstrades grossprofit grossloss max_drawdown ...`, anche con storia (`strategy.equity[1]`); `strategy.opentrades.*(n)`, `strategy.closedtrades.*(n)`; `syminfo.mintick`
- [x] Trade chiusi con commissioni, run-up e drawdown; equity per barra
- [x] `strategy001.pine` eseguito per intero (test di coerenza)
- Non gestiti: margin call, `oca_name` tra ordini diversi, bar magnifier, arrotondamento della quantità al lotto minimo

### Fase 5 — Output e validazione ✅
- [x] `trades.csv` (entry/exit, prezzo, qty, P&L, cumulato, commissioni, run-up/drawdown, trade aperti in fondo), `equity.csv`, `plots.csv`
- [x] `StrategyMetrics` e `metrics.csv`: net profit, profit factor, % profittevoli, medie, max drawdown/run-up, buy & hold, Sharpe/Sortino mensili
- [x] Golden test: parser della "List of trades" di TradingView (formato vecchio e nuovo), `GoldenComparison`, casi in `Backtester.Engine.Tests/Golden/`
- [x] CLI `backtest --script x.pine --data ohlcv.csv --out risultati/ [--input "T=v"] [--compare tv.csv]` (`Backtester.Cli`)
- [x] Endpoint `POST api/backtest` (JSON), `api/backtest/csv` (zip), `api/backtest/strategy001`
- [x] Dataset reale `Backtester/Resources/btcusdt_1h.csv` (BINANCE BTCUSDT 1h, 2024-01-01 → 2024-05-04); test su dati reali con verifica indipendente dei segnali e snapshot di regressione
- [x] Caso `Golden/strategy001-btcusdt-1h` pronto (strategia limitata a una finestra di date)
- [ ] **Primo golden test reale**: esportare la List of trades da TradingView per quel caso (vedi `Golden/README.md`)

### Dopo l'MVP
- Ottimizzazione parametri (`input.*` in griglia), eventuale compilazione AST → expression tree
- `request.security`, UDT, `method`, librerie

## Registro decisioni

| Data       | Decisione                                                                                  |
|------------|--------------------------------------------------------------------------------------------|
| 2026-10-29 | Interprete ad albero prima di qualsiasi compilazione: più semplice da debuggare.           |
| 2026-10-29 | Lexer scritto a mano al posto delle regex: serve gestire l'indentazione ed è O(n).         |
| 2026-10-29 | Indentazione: tab = 4 spazi; righe con indentazione non multipla di 4 = continuazione.     |
| 2026-10-29 | `na` resta un identificatore (in Pine è sia costante sia funzione `na(x)`).                |
| 2026-10-29 | Tipi (`int float bool string color`) e qualificatori (`series simple const`) restano identificatori: li distingue il parser, perché `color`/`input` sono anche namespace. |
| 2026-10-29 | I commenti non producono token; solo `//@version=N` (su riga propria) diventa `VERSION`.  |
| 2026-10-29 | `if`/`switch`/`for`/`while` sono nodi espressione (`IfExpr`, ...): il valore è l'ultima espressione del blocco eseguito. Ammessi come valore solo a inizio lato destro di dichiarazioni e assegnazioni. |
| 2026-10-29 | `else if` è rappresentato come blocco `else` con un solo `IfExpr`: niente nodo dedicato.  |
| 2026-10-29 | `[a, b]` è sempre `TupleExpr`: sia tupla di ritorno sia array letterale negli argomenti (`options = [...]`). |
| 2026-10-29 | `import`, `export`, `type`, `method`, `enum` producono un errore esplicito "not supported yet". |
| 2026-10-29 | Storia delle variabili locali: `x[1]` è il valore dell'ultima esecuzione del blocco/chiamata su una barra precedente (come in Pine). Più scritture nella stessa barra sovrascrivono lo slot. |
| 2026-10-29 | Divisione: sempre `float` (semantica v6); divisione e modulo per zero danno `na`. `+ - * %` tra int restano int. |
| 2026-10-29 | Confronti con `na` (anche `==`/`!=`) danno `false`; `na` in una condizione vale `false`. `and`/`or` sono lazy (v6). |
| 2026-10-29 | `plot()` non è un no-op: registra i valori per barra (`ExecutionResult.Plots`). Serve per i test e potrà finire nel CSV. |
| 2026-10-29 | Stato per call-site indicizzato per identità del nodo AST (`ReferenceEqualityComparer`), non per uguaglianza strutturale dei record. |
| 2026-10-29 | `ta.*` portati dalle implementazioni di riferimento del manuale Pine (`pine_ema`, `pine_supertrend`, ...), incluse le stranezze su `na`. Internamente `na` = `NaN` (`Indicators.cs`). |
| 2026-10-29 | EMA/RMA: la SMA di innesco vede ogni barra, quindi dopo un `na` la media riparte da una finestra pulita. Da confermare con i golden test. |
| 2026-10-29 | Override degli input indicizzati per **titolo** dell'input; gli input senza titolo non sono sovrascrivibili. `input.source` si sovrascrive col nome del campo (`"high"`, `"hl2"`). |
| 2026-10-29 | Namespace di disegno: funzioni no-op che restituiscono `na` (così `na(myLine)` funziona) e costanti stringa col proprio nome (`line.style_dotted`). Le lookup non modificano il registro: un `BuiltinRegistry` è condivisibile tra run. |
| 2026-10-29 | `format.mintick` assume tick 0.01 finché non esiste `syminfo` (fase 4). |
| 2026-10-29 | Il motore si aggancia all'interprete con `IBarHooks` (`BeforeBar`/`AfterBar`) e registra i propri built-in: `Pine.Core` non conosce il broker. |
| 2026-10-29 | Broker modellato sull'emulatore di TradingView (ordini dalla barra successiva, percorso intrabar verso l'estremo più vicino) per rendere i risultati confrontabili nei golden test. |
| 2026-10-29 | Le uscite chiudono i trade in FIFO, spezzando l'ultimo se parziali. Quando la posizione si azzera, gli `strategy.exit` pendenti vengono cancellati. |
| 2026-10-29 | Size `percent_of_equity`/`cash` calcolata al piazzamento dell'ordine (close della barra, o livello per limit/stop). Da confermare con i golden test. |
| 2026-10-29 | `strategy.*` con storia letti da uno snapshot salvato dopo i fill di inizio barra; a offset 0 il valore è calcolato al momento. |
| 2026-10-29 | I trade aperti a fine dati restano aperti (`BacktestResult.OpenTrades`), come su TradingView. |
| 2026-10-29 | Prezzi di limit/stop/trailing arrotondati al tick (`MinTick`), come TradingView. Da confermare con i golden test. |
| 2026-10-29 | Drawdown e run-up misurati sull'equity alla chiusura di ogni barra (TradingView usa anche gli estremi intrabar). Sharpe/Sortino su rendimenti mensili in UTC con risk-free 2%. |
| 2026-10-29 | Golden test: confronto in ordine dei soli trade chiusi; tolleranza sui prezzi di mezzo tick; orari confrontati nel fuso del grafico esportato. |
| 2026-10-29 | CSV di output: virgola, punto decimale, orari ISO 8601 UTC, campi vuoti per i valori mancanti. |
| 2026-10-29 | Dati di esempio reali da Binance invece che sintetici: sono le stesse barre di `BINANCE:BTCUSDT` su TradingView, quindi servono anche per i golden test. |
| 2026-10-29 | Fuso dell'exchange fisso a UTC finché non esiste `syminfo` completo; le funzioni di tempo accettano un fuso esplicito. |
| 2026-10-29 | Strategie da confrontare con TradingView limitate a una finestra di date: TradingView fa trade su tutta la storia caricata e il capitale divergerebbe. |
| 2026-10-29 | L'host lavora in cultura invariante: con la cultura italiana `minTick=0.01` veniva letto come 1. |
| 2026-10-29 | Snapshot di regressione dei trade (`Snapshots/`), da rigenerare con `BACKTEST_UPDATE_SNAPSHOTS=1` solo per cambiamenti voluti. |
