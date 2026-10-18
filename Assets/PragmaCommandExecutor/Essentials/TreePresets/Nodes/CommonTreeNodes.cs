using System;
using System.Collections.Generic;
using UnityEngine;

namespace Pragma.CommandExecutor
{
    /// <summary>Builds a <see cref="DelayCommand"/>.</summary>
    [Serializable]
    public class DelayTreeNode : ICommandTreePresetNode
    {
        [field: SerializeField] public float Duration { get; set; }

        public ICommand Build(IReadOnlyDictionary<string, object> args)
        {
            return new DelayCommand { Duration = Duration };
        }
    }

    /// <summary>Builds a <see cref="LogCommand"/>.</summary>
    [Serializable]
    public class LogTreeNode : ICommandTreePresetNode
    {
        [field: SerializeField] public string Message { get; set; }
        [field: SerializeField] public LogType LogType { get; set; } = LogType.Log;

        public ICommand Build(IReadOnlyDictionary<string, object> args)
        {
            return new LogCommand { Message = Message, LogType = LogType };
        }
    }

    /// <summary>Builds a <see cref="CallbackCommand"/> that invokes the <see cref="Action"/> found under <see cref="Callback"/>.</summary>
    [Serializable]
    public class CallbackTreeNode : ICommandTreePresetNode
    {
        [field: SerializeField] public CommandTreePresetKey<Action> Callback { get; set; }

        public ICommand Build(IReadOnlyDictionary<string, object> args)
        {
            return new CallbackCommand { Callback = Callback.Resolve(args) };
        }
    }
}
