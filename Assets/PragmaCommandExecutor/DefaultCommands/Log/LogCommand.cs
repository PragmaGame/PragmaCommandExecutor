using System;
using UnityEngine;

namespace Pragma.CommandExecutor
{
    [Serializable]
    public class LogCommand : ICommand
    {
        [field: SerializeField] public string Message { get; set; }
        [field: SerializeField] public LogType LogType { get; set; }

        public void Reset()
        {
            Message = string.Empty;
            LogType = LogType.Log;
        }
    }
}