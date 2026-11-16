using System.Text;
using NLog;
using NLog.Layouts;

namespace NzbDrone.Common.Instrumentation
{
    public class CleansingConsoleLogLayout : SimpleLayout
    {
        public CleansingConsoleLogLayout(string layout)
            : base(layout)
        {
        }

        protected override string GetFormattedMessage(LogEventInfo logEvent)
        {
            return CleanseLogMessage.Cleanse(base.GetFormattedMessage(logEvent));
        }

        protected override void RenderFormattedMessage(LogEventInfo logEvent, StringBuilder target)
        {
            var rendered = new StringBuilder();
            base.RenderFormattedMessage(logEvent, rendered);
            target.Append(CleanseLogMessage.Cleanse(rendered.ToString()));
        }
    }
}
