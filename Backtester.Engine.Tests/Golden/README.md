# Golden test

Ogni sottocartella è un caso: la stessa strategia eseguita su TradingView e sul backtester,
sugli stessi dati. `GoldenTests.GoldenCases` confronta i trade uno per uno.

```
Golden/
  sma-cross-btc-1h/
    strategy.pine      lo script, identico a quello su TradingView
    ohlcv.csv          le barre esportate dal grafico (oppure "data" in case.json)
    tradingview.csv    la "List of trades" esportata dallo Strategy Tester
    case.json          opzionale
```

Un caso senza `tradingview.csv` viene comunque eseguito (verifica che lo script giri) e il test
scrive "waiting for tradingview.csv".

## Caso pronto: `strategy001-btcusdt-1h`

Manca solo l'export di TradingView. Le barre sono già nel repository (`Backtester/Resources/btcusdt_1h.csv`,
BTCUSDT 1h da Binance dal 2024-01-01 al 2024-05-04): sono le stesse che TradingView mostra per `BINANCE:BTCUSDT`.

1. Apri `BINANCE:BTCUSDT` (proprio Binance, non un altro exchange), timeframe **1h**, fuso del grafico **UTC**.
2. Aggiungi come strategia il contenuto di `strategy001-btcusdt-1h/strategy.pine` (con gli input di default).
   La strategia opera solo tra il 15 gennaio e il 1° maggio 2024: il resto della storia caricata non conta.
3. Assicurati che il grafico abbia caricato la storia almeno da inizio 2024 (scorri indietro se serve).
4. Strategy Tester → *List of trades* → export CSV → salva come `strategy001-btcusdt-1h/tradingview.csv`.
5. Esegui i test: `GoldenTests.GoldenCases` stampa le differenze trade per trade.

Il backtester produce 57 trade in quella finestra.

## Come preparare un caso

1. Su TradingView apri il grafico (simbolo e timeframe) e aggiungi la strategia.
2. Imposta il fuso orario del grafico su **UTC** (in basso a destra), oppure indica l'offset in `case.json`.
3. **Dati**: menu del grafico → *Export chart data…* → CSV con orario in formato UNIX → `ohlcv.csv`.
   Esporta dopo aver caricato tutta la storia che ti interessa: le barre del CSV sono quelle su cui gira il backtester.
4. **Trade**: Strategy Tester → *List of trades* → icona di export → CSV → `tradingview.csv`.
5. Se hai cambiato gli input rispetto ai default, riportali in `case.json`.

```json
{
  "minTick": 0.01,
  "utcOffset": "+00:00",
  "profitTolerance": 0.01,
  "inputs": { "Fast SMA": 20 }
}
```

`minTick` deve essere il tick del simbolo (`syminfo.mintick`): serve per `profit`/`loss` in tick,
per lo slippage e come tolleranza sui prezzi (metà tick). `profitTolerance: null` salta il controllo sul profitto.

## Cosa aspettarsi

TradingView calcola gli indicatori anche sulle barre precedenti all'inizio dei nostri dati, e sul grafico
fa trade su tutta la storia caricata. Per questo una strategia da confrontare va **limitata a una finestra di date**
con `timestamp()`, che inizi abbastanza dopo l'inizio dei dati da stabilizzare le medie (vedi `strategy001-btcusdt-1h`):
così su entrambi i lati il primo trade parte con lo stesso capitale.
