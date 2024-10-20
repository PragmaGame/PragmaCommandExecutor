using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using JetBrains.Annotations;
using UnityEngine;

namespace Pragma.CommandExecutor
{
    public class MonoCommandExecutor : MonoBehaviour, ICommandExecutor
    {
        private static MonoCommandExecutor _instance;
        private CommandExecutor _commandExecutor;

        public static MonoCommandExecutor Instance
        {
            get
            {
                if (_instance != null)
                {
                    return _instance;
                }

                _instance = FindObjectOfType<MonoCommandExecutor>();

                if (_instance == null)
                {
                    _instance = new GameObject(typeof(CommandExecutor).ToString()).AddComponent<MonoCommandExecutor>();
                }

                return _instance;
            }
        }
        
        [UsedImplicitly]
        protected virtual void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            if (Instance != null && Instance.transform.parent == null)
            {
                DontDestroyOnLoad(gameObject);
                
                var factory = FindObjectOfType<MonoCommandProcessorFactory>();
                _commandExecutor = new CommandExecutor(factory);
            }
        }

        public UniTask Execute(IEnumerable<ICommand> commands, CancellationToken token, ExecuteFormat format = ExecuteFormat.Parallel)
        {
            return _commandExecutor.Execute(commands, token, format);
        }

        public UniTask Execute(ICommand command, CancellationToken token)
        {
            return _commandExecutor.Execute(command, token);
        }
    }
}