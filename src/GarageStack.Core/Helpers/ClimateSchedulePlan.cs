using System.Globalization;
using GarageStack.Core.Models;

namespace GarageStack.Core.Helpers;

/// <summary>One command a schedule sends: a <see cref="VehicleCommands"/> name and its value.</summary>
public readonly record struct ScheduledCommand(string Command, string Value);

/// <summary>The commands a climate schedule sends, in the order they are sent.</summary>
public static class ClimateSchedulePlan
{
    /// <summary>The command that switches climate on; a run counts as started once the car takes it.</summary>
    public const string ClimateCommand = "climate";

    /// <summary>
    /// The temperature comes first: with climate off the gateway only stores it, without waking the
    /// car, and switching climate on then uses it. Fan only and front defrost take none. The extras
    /// follow, and only when asked for.
    /// </summary>
    public static IReadOnlyList<ScheduledCommand> For(ClimateSchedule schedule)
    {
        var commands = new List<ScheduledCommand>();
        if (schedule.Mode == ClimateScheduleMode.On)
            commands.Add(new("climate-temperature", schedule.TemperatureC.ToString(CultureInfo.InvariantCulture)));
        commands.Add(new(ClimateCommand, schedule.Mode.ToGatewayValue()));
        if (schedule.RearDefroster)
            commands.Add(new("rear-defroster", "on"));
        if (schedule.SeatLeftLevel > 0)
            commands.Add(new("seat-left", schedule.SeatLeftLevel.ToString(CultureInfo.InvariantCulture)));
        if (schedule.SeatRightLevel > 0)
            commands.Add(new("seat-right", schedule.SeatRightLevel.ToString(CultureInfo.InvariantCulture)));
        return commands;
    }
}
