using System;
using System.Collections.Generic;
using UnityEngine;

namespace Pragma.CommandExecutor
{
    [Serializable]
    public class CommandGroup : ICommand
    {
        public const int REPEAT_FOREVER = -1;

        [field: SerializeField] public GroupMode Mode { get; set; }

        /// <summary>
        /// How many times the group runs again after the first pass. Any negative value (<see cref="REPEAT_FOREVER"/>)
        /// repeats until the run is cancelled.
        /// </summary>
        [field: SerializeField] public int Repeat { get; set; } = 0;
        [field: SerializeField, SerializeReference] public List<ICommand> Commands { get; set; } = new();
        
        public CommandGroup()
        {
        }

        public CommandGroup(GroupMode mode, List<ICommand> commands = null, int repeat = 0)
        {
            Mode = mode;
            Repeat = repeat;

            if (commands != null)
            {
                Commands.AddRange(commands);
            }
        }
        
        public void Reset()
        {
            Mode = GroupMode.Parallel;
            Repeat = 0;
            Commands.Clear();
        }
    }
}