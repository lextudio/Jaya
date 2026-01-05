using Avalonia;
using Avalonia.Controls;
using Avalonia.Logging;

using Serilog;

using System;
using System.Collections.Generic;
using System.Diagnostics;

using AvaloniaLogEventLevel = Avalonia.Logging.LogEventLevel;
using SerilogEventLevel = Serilog.Events.LogEventLevel;

namespace Jaya.Ui.Logging
{
    public static class MyLogExtensions
    {
        public static AppBuilder LogToMySink(this AppBuilder builder,
            AvaloniaLogEventLevel level = AvaloniaLogEventLevel.Warning,
            params string[] areas)
        {
            Logger.Sink = new MyLogSink(level, areas);
            return builder;
        }
    }

    sealed class MyLogSink : ILogSink
    {
        readonly AvaloniaLogEventLevel _minimumLevel;
        readonly HashSet<string> _areas;

        public MyLogSink(AvaloniaLogEventLevel level, params string[] areas)
        {
            _minimumLevel = level;
            _areas = new HashSet<string>(areas ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        }

        bool ShouldLog(AvaloniaLogEventLevel level, string area)
        {
            if (level < _minimumLevel)
                return false;

            if (_areas.Count == 0 || area == null)
                return true;

            return _areas.Contains(area);
        }

        static SerilogEventLevel MapLevel(AvaloniaLogEventLevel level)
            => level switch
            {
                AvaloniaLogEventLevel.Verbose => SerilogEventLevel.Verbose,
                AvaloniaLogEventLevel.Debug => SerilogEventLevel.Debug,
                AvaloniaLogEventLevel.Information => SerilogEventLevel.Information,
                AvaloniaLogEventLevel.Warning => SerilogEventLevel.Warning,
                AvaloniaLogEventLevel.Error => SerilogEventLevel.Error,
                AvaloniaLogEventLevel.Fatal => SerilogEventLevel.Fatal,
                _ => SerilogEventLevel.Information
            };

        public bool IsEnabled(AvaloniaLogEventLevel level, string area) => ShouldLog(level, area);

        public void Log(AvaloniaLogEventLevel level, string area, object? source, string messageTemplate)
            => Log(level, area, source, messageTemplate, Array.Empty<object?>());

        public void Log(AvaloniaLogEventLevel level, string area, object? source, string messageTemplate, object?[]? propertyValues)
        {
            if (!ShouldLog(level, area))
                return;

            var logger = Serilog.Log.ForContext("Area", area ?? string.Empty)
                                    .ForContext("SourceContext", "Avalonia");

            if (source != null)
                logger = logger.ForContext("Source", source);

            var serilogLevel = MapLevel(level);

            if (propertyValues == null || propertyValues.Length == 0)
            {
                logger.Write(serilogLevel, messageTemplate);
            }
            else
            {
                logger.Write(serilogLevel, messageTemplate, propertyValues);
            }
        }
    }

    sealed class AvaloniaTraceSerilogSink : TraceListener
    {
        readonly ILogger _logger;

        public AvaloniaTraceSerilogSink()
            : this(Serilog.Log.Logger)
        {
        }

        public AvaloniaTraceSerilogSink(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public override void Write(string? message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            _logger.Information(message);
        }

        public override void WriteLine(string? message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            _logger.Information(message);
        }
    }
}
