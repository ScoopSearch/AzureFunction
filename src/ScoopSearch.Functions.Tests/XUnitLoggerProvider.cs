using System;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace ScoopSearch.Functions.Tests
{
    public class XUnitLoggerProvider : ILoggerProvider
    {
        private readonly ITestOutputHelper _testOutputHelper;

        public XUnitLoggerProvider(ITestOutputHelper testOutputHelper)
        {
            _testOutputHelper = testOutputHelper;
        }

        public ILogger CreateLogger(string categoryName) => new XUnitLogger(categoryName, _testOutputHelper);

        public void Dispose()
        {
        }

        private class XUnitLogger : ILogger
        {
            private readonly string _categoryName;
            private readonly ITestOutputHelper _testOutputHelper;

            public XUnitLogger(string categoryName, ITestOutputHelper testOutputHelper)
            {
                var lastPart = categoryName.Split(".").Last();
                _categoryName = lastPart.Substring(0, Math.Min(15, lastPart.Length)).PadRight(15);
                _testOutputHelper = testOutputHelper;
            }

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception,
                Func<TState, Exception, string> formatter)
            {
                _testOutputHelper.WriteLine(
                    $"{GetNow()} | {GetLogLevel(logLevel)} | {_categoryName} | {formatter(state, exception)}");
            }

            public bool IsEnabled(LogLevel logLevel) => true;

            public IDisposable BeginScope<TState>(TState state)
            {
                _testOutputHelper.WriteLine(
                    $"{GetNow()} | BEGIN SCOPE | {_categoryName} | {state}");
                return new Disposable(() =>
                    _testOutputHelper.WriteLine(
                        $"{GetNow()} | END SCOPE   | {_categoryName} | {state}"));
            }

            private string GetNow() => DateTime.Now.ToString("u");

            private string GetLogLevel(LogLevel logLevel) => logLevel.ToString().ToUpper().PadRight(11);

            private class Disposable : IDisposable
            {
                private readonly Action _action;

                public Disposable(Action action)
                {
                    _action = action;
                }

                public void Dispose()
                {
                    _action();
                }
            }
        }
    }
}
