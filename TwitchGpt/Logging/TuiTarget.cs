using NLog;
using NLog.Targets;
using TwitchGpt.Tui;

namespace TwitchGpt.Logging;

/// <summary>NLog target that writes log lines to the Terminal.Gui log view.</summary>
[Target("Tui")]
public sealed class TuiTarget : TargetWithLayout
{
    protected override void Write(LogEventInfo logEvent)
    {
        var line = RenderLogEvent(Layout, logEvent);
        TuiApplication.AppendLog(line);
    }
}
