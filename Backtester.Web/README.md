# Backtester.Web (bozza)

UI per il backtester: Astro + SolidJS + UnoCSS, grafici con lightweight-charts.

- Editor della strategia (esempi `strategy001`, `test002`), input JSON, CSV opzionale (default BINANCE:BTCUSDT 1h), min tick
- Metriche principali e dettagliate
- Grafico con candele, plot dello script e trade; equity e drawdown in pannelli sotto
- Lista dei trade (chiusi e aperti), errori con fase, riga e barra

## Avvio

Serve il backend sulla porta 5014:

```
dotnet run --project Backtester --launch-profile http
```

Poi la UI (http://localhost:4321, `/api` va in proxy sul backend):

```
cd Backtester.Web
npm install
npm run dev
```
