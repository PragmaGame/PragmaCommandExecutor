using System;
using UnityEngine;

namespace Pragma.CommandExecutor
{
    [Serializable]
    public class CommandGraph
    {
        [field: SerializeField, SerializeReference] public ICommand Root { get; set; }
    }
}