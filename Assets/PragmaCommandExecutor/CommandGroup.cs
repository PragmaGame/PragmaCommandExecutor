using System;
using System.Collections.Generic;
using UnityEngine;

namespace Pragma.CommandExecutor
{
    [Serializable]
    public class CommandGroup : ICommand
    {
        [field: SerializeField] public CommandExecuteFormat ExecuteFormat { get; private set; }
        [field: SerializeField, SerializeReference] public List<ICommand> Commands { get; private set; }
    }
}