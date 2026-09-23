using System;
using UnityEngine;

namespace Pragma.CommandExecutor
{
    /// <summary>
    /// Builder shortcuts for the commands registered by <see cref="DefaultCommandRegistrationContext"/>.
    /// </summary>
    public static class CommandBuilderExtensions
    {
        public static CommandBuilder JoinDelay(this CommandBuilder builder, float duration)
        {
            builder.Join(out DelayCommand command);
            command.Duration = duration;
            return builder;
        }

        public static CommandBuilder JoinCallback(this CommandBuilder builder, Action callback)
        {
            builder.Join(out CallbackCommand command);
            command.Callback = callback;
            return builder;
        }

        public static CommandBuilder JoinLog(this CommandBuilder builder, string message, LogType logType)
        {
            builder.Join(out LogCommand command);
            command.Message = message;
            command.LogType = logType;
            return builder;
        }

        public static CommandBuilder JoinScale(
            this CommandBuilder builder,
            Transform context,
            Vector3 from,
            Vector3 to,
            float duration,
            AnimationCurve curve = null)
        {
            builder.Join(out ScaleCommand command);
            command.Context = context;
            command.From = from;
            command.To = to;
            command.Duration = duration;
            command.Curve = curve;
            return builder;
        }
    }
}
