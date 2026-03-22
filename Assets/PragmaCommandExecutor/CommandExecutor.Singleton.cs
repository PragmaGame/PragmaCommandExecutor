#if COMMAND_EXECUTOR_SINGLETON_ENABLED

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Pragma.CommandExecutor
{
    public partial class CommandExecutor
    {
        private static ICommandExecutor _singleton;
        
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
        
        private static ICommandExecutor Create()
        {
            var executor = new CommandExecutor(null);
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
                _singleton = null;
            }
        }
#endif
    }
}

#endif