# Golden test

Ogni sottocartella è un caso: la stessa strategia eseguita su TradingView e sul backtester,
sugli stessi dati. `GoldenTests.GoldenCases` confronta i trade uno per uno.

```
Golden/
  sma-cross-btc-1h/
    strategy.pine      lo script, identico a quello su TradingView
    ohlcv.csv          le barre esportate dal grafico
    tradingview.csv    la "List of trades" esportata dallo Strategy Tester
    case.json          opzionale
```

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

TradingView calcola gli indicatori anche sulle barre precedenti all'inizio dell'export: se lo script usa medie
lunghe, i primi segnali possono differire. Conviene esportare molta storia e, se serve, ignorare la parte iniziale
limitando la strategia con una condizione sulla data.
