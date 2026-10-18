using System;

namespace Pragma.CommandExecutor
{
    /// <summary>
    /// Animates a value of <see cref="Command{TContext, TData}.Context"/> from <see cref="LerpData{TValue}.From"/> to
    /// <see cref="LerpData{TValue}.To"/>. Derive a non-generic command and a <see cref="LerpCommandProcessor{TCommand, TContext, TValue}"/>
    /// that applies the value.
    /// </summary>
    [Serializable]
    public abstract class LerpCommand<TContext, TValue> : Command<TContext, LerpData<TValue>>
    {
        protected override void OnResetData()
        {
            var data = Data;

            if (data == null)
            {
                base.OnResetData();
                return;
            }

            // In place: a pooled command keeps its data object instead of allocating a new one.
            data.From = default;
            data.To = default;
            data.Duration = LerpData<TValue>.DEFAULT_DURATION;
            data.Curve = null;
        }
    }
}
