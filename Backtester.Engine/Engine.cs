namespace Backtester.Engine;

public class Engine()
{
    private double Capital;

    public void Setup(double InitialCapital)
    {
        // Carica settaggi account
        Capital = InitialCapital;

        // Carica dati storici
        LoadCsv();
    }

    /// <summary>
    /// Lancia la simulazione.
    /// </summary>
    public void Start()
    { 
        // Genera barre e simulazione (valore indicatore per barra + operazioni)
    }

    public void CalculateResult()
    {
        // Calcola P&L, drawdown, ecc.
    }  


    private void LoadCsv()
    {
        throw new NotImplementedException();
    }
}
