#if COMMAND_EXECUTOR_SINGLETON_ENABLED

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Pragma.CommandExecutor
{
    public partial class CommandExecutor
    {
        private static CommandExecutor _singleton;
        
        public static ICommandExecutor Singleton
        {
            get
            {
                if (_singleton == null)
                {
                    _singleton = Create();
                }

                return _singleton;
            }
        }
        
        private static CommandExecutor Create()
        {
            var executor = new CommandExecutor(null, new ICommandRegistrationContext[]
            {
                new DefaultCommandRegistrationContext(),
            });

            return executor;
        }

#if UNITY_EDITOR
        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }
        
        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state is PlayModeStateChange.ExitingPlayMode)
            {
                _singleton?.Dispose();
                _singleton = null;
            }
        }
#endif
    }
}

#endif