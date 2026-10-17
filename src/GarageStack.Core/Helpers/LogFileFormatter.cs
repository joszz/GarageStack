using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Display;

namespace GarageStack.Core.Helpers;

/// <summary>
/// The log files' line format, Serilog's own, with every line after an entry's first indented: an
/// exception's message and stack, and any line break inside what an entry quotes from outside (an
/// identity provider's error description, a browser's error report). <see cref="LogFileReader"/>
/// only starts an entry at a timestamp in the first column, so nothing quoted can pose as an entry
/// of its own in the log viewer.
/// </summary>
public sealed class LogFileFormatter : ITextFormatter
{
    /// <summary>Serilog's default file template, which <see cref="LogFileReader"/> parses.</summary>
    public const string OutputTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}";

    /// <summary>What every continuation line starts with.</summary>
    public const string Indent = "  ";

    private readonly MessageTemplateTextFormatter _template = new(OutputTemplate);

    public void Format(LogEvent logEvent, TextWriter output)
    {
        using var buffer = new StringWriter();
        _template.Format(logEvent, buffer);

        var lines = buffer.ToString().ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');
        output.Write(lines[0]);
        output.Write(Environment.NewLine);
        foreach (var line in lines.Skip(1))
        {
            if (line.Length > 0) output.Write(Indent + line);
            output.Write(Environment.NewLine);
        }
    }
}
