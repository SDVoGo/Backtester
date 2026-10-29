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

### Fase 0 — Fondamenta ✅ / 🚧
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

### Fase 1 — Parser
- [ ] AST (record C#) per espressioni e istruzioni
- [ ] Parser a discesa ricorsiva per le istruzioni, Pratt per le espressioni
- [ ] Precedenze Pine: `?:` < `or` < `and` < `== !=` < `< <= > >=` < `+ -` < `* / %` < unari < `[]`/chiamata/`.`
- [ ] Argomenti nominali (`title = "x"`), dichiarazioni multiple separate da virgola su una riga
- [ ] Errori di sintassi con riga/colonna; test su `test002.pine` e su script di strategia

### Fase 2 — Interprete (modello di esecuzione Pine)
- [ ] Esecuzione dello script una volta per barra
- [ ] Ogni variabile è una **serie** con storia accessibile via `x[n]`
- [ ] `var` persistente tra barre; variabili normali reinizializzate ad ogni barra
- [ ] Stato **per punto di chiamata** per funzioni con memoria (`ta.*` e funzioni utente che le usano)
- [ ] Propagazione di `na`; tipi base `int`, `float`, `bool`, `string`, `color`
- [ ] Variabili built-in: `open high low close volume time bar_index barstate.*`

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
