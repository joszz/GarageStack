namespace GarageStack.Core.Configuration;

public record TyrePressureThresholds(double LowBar, double GoodBar, double HighBar)
{
    public static readonly TyrePressureThresholds Default = new(LowBar: 2.2, GoodBar: 2.6, HighBar: 3.2);
}
