using NLog;
using NLog.Conditions;
using NLog.Targets;

namespace TwitchGpt.Logging;

public static class Logger
{
    private const string DefaultLayout = @"[${date:format=yyyy-MM-dd HH\:mm\:ss}][${level:uppercase=true}] ${message}";

    static Logger()
    {
        var config = new NLog.Config.LoggingConfiguration();

        var bot = new FileTarget("logfile") { FileName = @"TwitchGpt_${logger}.log", Layout = DefaultLayout };

        var consoleLog = new ColoredConsoleTarget("consolelog") { Layout = DefaultLayout };
        foreach (var rule in ColorRules)
            consoleLog.RowHighlightingRules.Add(rule);

        config.AddRuleForAllLevels(bot);
        config.AddRuleForAllLevels(consoleLog);

        LogManager.Configuration = config;
    }
    
    public static ILogger Instance(string context) => LogManager.GetLogger(context);

    private static readonly Dictionary<LogLevel, ConsoleOutputColor> ColoringRules = new()
    {
        [LogLevel.Trace] = ConsoleOutputColor.DarkGray,
        [LogLevel.Info] = ConsoleOutputColor.Green,
        [LogLevel.Debug] = ConsoleOutputColor.Gray,
        [LogLevel.Warn] = ConsoleOutputColor.DarkYellow,
        [LogLevel.Error] = ConsoleOutputColor.Red,
        [LogLevel.Fatal] = ConsoleOutputColor.Red,
    };
    
    private static IEnumerable<ConsoleRowHighlightingRule> ColorRules
    {
        get => ColoringRules.Select(_ =>
        {
            var highlightRule = new ConsoleRowHighlightingRule
            {
                Condition = ConditionParser.ParseExpression($"level == LogLevel.{_.Key.ToString()}"),
                ForegroundColor = _.Value
            };

            return highlightRule;
        });
    }
}