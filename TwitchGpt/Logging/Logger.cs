using NLog;
using NLog.Targets;

namespace TwitchGpt.Logging;

public static class Logger
{
    private const string DefaultLayout = @"[${date:format=yyyy-MM-dd HH\:mm\:ss}][${level:uppercase=true}] ${message}";

    static Logger()
    {
        var config = new NLog.Config.LoggingConfiguration();

        var fileTarget = new FileTarget("logfile") { FileName = @"TwitchGpt_${logger}.log", Layout = DefaultLayout };
        var tuiTarget = new TuiTarget { Name = "tui", Layout = DefaultLayout };

        config.AddRuleForAllLevels(fileTarget);
        config.AddRuleForAllLevels(tuiTarget);

        LogManager.Configuration = config;
    }

    public static ILogger Instance(string context) => LogManager.GetLogger(context);
}
