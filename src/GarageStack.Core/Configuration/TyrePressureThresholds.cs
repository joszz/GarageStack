namespace GarageStack.Core.Configuration;

/// <param name="LowBar">Below this a tyre is low: a warning on the dashboard and a notification.</param>
/// <param name="GoodBar">Up to this a tyre is fine; between it and <paramref name="HighBar"/> it is on the high side.</param>
/// <param name="HighBar">Above this a tyre is overinflated: a warning and a notification.</param>
/// <param name="DropBar">
/// How far one tyre may fall behind the other three over <see cref="Helpers.TyreTrend.WindowDays"/>
/// days before that is a slow puncture. Zero switches the alert off.
/// </param>
public record TyrePressureThresholds(double LowBar, double GoodBar, double HighBar, double DropBar = 0.15)
{
    public static readonly TyrePressureThresholds Default = new(LowBar: 2.2, GoodBar: 2.6, HighBar: 3.2, DropBar: 0.15);
}
