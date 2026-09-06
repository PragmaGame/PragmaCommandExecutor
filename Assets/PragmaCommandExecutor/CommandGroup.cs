using System;
using System.Collections.Generic;
using UnityEngine;

namespace Pragma.CommandExecutor
{
    [Serializable]
    public class CommandGroup : ICommand
    {
        [field: SerializeField] public CommandExecuteFormat ExecuteFormat { get; set; }
        [field: SerializeField] public int Loop { get; set; } = 0;
        [field: SerializeField, SerializeReference] public List<ICommand> Commands { get; set; } = new();
        
        public CommandGroup()
        {
        }

        public CommandGroup(CommandExecuteFormat executeFormat, List<ICommand> commands = null, int loop = 0)
        {
            ExecuteFormat = executeFormat;
            Loop = loop;

            if (commands != null)
            {
                Commands.AddRange(commands);
            }
        }
        
        public void Reset()
        {
            ExecuteFormat = CommandExecuteFormat.Parallel;
            Loop = 0;
            Commands.Clear();
        }
    }
}