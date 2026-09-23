using System;
using UnityEngine;

namespace Pragma.CommandExecutor.Examples
{
    /// <summary>
    /// Builder shortcuts for the example commands, written the same way as the package's own
    /// <see cref="CommandBuilderExtensions"/>: <c>Join(out command)</c> rents a pooled command and adds it to the
    /// current group, so no configuration delegate (and no closure) is allocated.
    /// </summary>
    public static class ExampleCommandBuilderExtensions
    {
        public static CommandBuilder JoinMove(
            this CommandBuilder builder,
            Transform context,
            Vector3 from,
            Vector3 to,
            float duration,
            AnimationCurve curve = null)
        {
            builder.Join(out MoveCommand command);
            command.Context = context;
            command.From = from;
            command.To = to;
            command.Duration = duration;
            command.Curve = curve;
            return builder;
        }

        public static CommandBuilder JoinRotate(
            this CommandBuilder builder,
            Transform context,
            Vector3 from,
            Vector3 to,
            float duration,
            AnimationCurve curve = null)
        {
            builder.Join(out RotateCommand command);
            command.Context = context;
            command.From = from;
            command.To = to;
            command.Duration = duration;
            command.Curve = curve;
            return builder;
        }

        public static CommandBuilder JoinSetActive(this CommandBuilder builder, GameObject target, bool isActive)
        {
            builder.Join(out SetActiveCommand command);
            command.Target = target;
            command.IsActive = isActive;
            return builder;
        }

        public static CommandBuilder JoinWaitUntil(this CommandBuilder builder, Func<bool> condition)
        {
            builder.Join(out WaitUntilCommand command);
            command.Condition = condition;
            return builder;
        }

        public static CommandBuilder JoinAddScore(this CommandBuilder builder, int amount)
        {
            builder.Join(out AddScoreCommand command);
            command.Amount = amount;
            return builder;
        }
    }
}
