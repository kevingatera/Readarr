using System;
using FluentAssertions;
using NLog;
using NUnit.Framework;
using NzbDrone.Common.Instrumentation;

namespace NzbDrone.Common.Test.InstrumentationTests
{
    [TestFixture]
    public class CleansingConsoleLogLayoutFixture
    {
        private const string ConsoleFormat = "[${level}] ${logger}: ${message} ${onexception:inner=${newline}${newline}[v${assembly-version}] ${exception:format=ToString}${newline}${exception:format=Data}${newline}}";
        private const string Message = "*** Migrating Database=readarr-main;Host=postgres14;Username=fakeuser;Password=FakeSecret123;Port=5432;Enlist=False ***";

        [Test]
        public void should_cleanse_message_and_keep_level_and_logger()
        {
            var layout = new CleansingConsoleLogLayout(ConsoleFormat);

            var logEvent = new LogEventInfo(LogLevel.Info, "MigrationController", Message);

            var rendered = layout.Render(logEvent);

            rendered.Should().NotContain("FakeSecret123");
            rendered.Should().Contain("[Info] MigrationController:");
        }

        [Test]
        public void should_cleanse_message_and_keep_exception_output()
        {
            var layout = new CleansingConsoleLogLayout(ConsoleFormat);

            var logEvent = new LogEventInfo(LogLevel.Info, "MigrationController", Message)
            {
                Exception = new InvalidOperationException("FakeException message")
            };

            var rendered = layout.Render(logEvent);

            rendered.Should().NotContain("FakeSecret123");
            rendered.Should().Contain("[Info] MigrationController:");
            rendered.Should().Contain("FakeException message");
            rendered.Should().Contain("[v");
        }
    }
}
