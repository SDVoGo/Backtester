namespace Backtester.Graph;

/// <summary>
/// Rappresentazione di una barra nel grafico.
/// </summary>
/// <param name="Time"></param>
/// <param name="Open"></param>
/// <param name="High"></param>
/// <param name="Low"></param>
/// <param name="Close"></param>
/// <param name="Volume"></param>
public record Bar(DateTime Time, double Open, double High, double Low, double Close, double Volume);
