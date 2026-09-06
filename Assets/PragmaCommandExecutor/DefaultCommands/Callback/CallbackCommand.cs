using System;

namespace Pragma.CommandExecutor
{
    public class CallbackCommand : ICommand
    {
        public Action Callback { get; set; }

        public void Reset()
        {
            Callback = null;
        }
    }
}