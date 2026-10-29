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
| `Backtester`        | ASP.NET (API) | Host: endpoint per lanciare lexer/parser/backtest                 |
| `Backtester.Engine` | class library | *(fase 4)* Broker simulato, ordini, posizioni, metriche, CSV      |

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

### Fase 3 — Built-in
- [ ] `input.*`, `math.*`, `str.tostring`, `nz`, `na`, `fixnan`
- [ ] `ta.sma ema rma wma atr tr rsi highest lowest crossover crossunder change supertrend stdev`
- [ ] Funzioni grafiche come no-op

### Fase 4 — Motore di backtest (`Backtester.Engine`)
- [ ] Lettura OHLCV da CSV
- [ ] `strategy()` (capitale iniziale, qty di default, commissioni, slippage, pyramiding, `process_orders_on_close`)
- [ ] `strategy.entry`, `strategy.exit` (stop/limit/trail), `strategy.close`, `strategy.close_all`, `strategy.order`
- [ ] Esecuzione di default all'open della barra successiva; stop/limit intrabar su OHLC
- [ ] `strategy.position_size`, `strategy.equity`, `strategy.opentrades`, ...

### Fase 5 — Output e validazione
- [ ] CSV lista trade (entry/exit, prezzo, qty, P&L, MAE/MFE opzionali)
- [ ] CSV equity curve e riepilogo metriche (net profit, drawdown, win rate, profit factor)
- [ ] Golden test: confronto con la "List of trades" esportata da TradingView sugli stessi dati
- [ ] Host: endpoint / CLI `backtest --script x.pine --data ohlcv.csv --out risultati/`

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
