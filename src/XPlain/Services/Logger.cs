using System;
using Microsoft.Extensions.Logging;

namespace XPlain.Services
{
    public class Logger<T> : ILogger<T>
    {
        private readonly ILoggerFactory _factory;

        public Logger(ILoggerFactory factory)
        {
            _factory = factory;
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            return _factory.CreateLogger<T>().BeginScope(state);
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return _factory.CreateLogger<T>().IsEnabled(logLevel);
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            _factory.CreateLogger<T>().Log(logLevel, eventId, state, exception, formatter);
        }
    }
}