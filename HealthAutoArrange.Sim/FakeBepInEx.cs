// Fake BepInEx surface used by the shared-source UnityUiAdapter/ReminderDispatcher compile.
using System;

namespace BepInEx.Logging
{
    public enum LogLevel
    {
        None = 0,
        Fatal = 1,
        Error = 2,
        Warning = 3,
        Info = 4,
        Debug = 5,
        Message = 6,
        Tip = 7,
        All = 0x7FFFFFFF,
    }

    public sealed class ManualLogSource
    {
        private readonly Action<LogLevel, string> _sink;

        public ManualLogSource(Action<LogLevel, string> sink = null)
        {
            _sink = sink ?? ((_level, _message) => { });
        }

        public void Log(LogLevel level, string message) => _sink(level, message);
        public void LogInfo(string message) => _sink(LogLevel.Info, message);
        public void LogDebug(string message) => _sink(LogLevel.Debug, message);
        public void LogWarning(string message) => _sink(LogLevel.Warning, message);
        public void LogError(string message) => _sink(LogLevel.Error, message);
    }
}
