using System;
using UnityEngine;

namespace Pragma.CommandExecutor
{
    [Serializable]
    public class DelayCommand : ICommand
    {
        [field: SerializeField ] public float Duration { get; set; }
        
        public void Reset()
        {
            Duration = 0;
        }
    }
}