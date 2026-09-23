using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.LowLevel;

namespace Pragma.CommandExecutor
{
    /// <summary>
    /// Global tick: a single system injected at the beginning of the player loop <c>Update</c> phase
    /// (the same place UniTask runs <c>PlayerLoopTiming.Update</c>) that ticks every live executor.
    /// </summary>
    internal static class CommandExecutorPlayerLoop
    {
        // Marker type identifying the injected system inside the player loop.
        private struct CommandExecutorUpdate
        {
        }

        private static readonly List<CommandExecutor> Executors = new();
        private static bool _isInstalled;
        private static bool _isUpdating;

        public static void Register(CommandExecutor executor)
        {
            Install();
            Executors.Add(executor);
        }

        public static void Unregister(CommandExecutor executor)
        {
            var index = Executors.IndexOf(executor);

            if (index < 0)
            {
                return;
            }

            if (_isUpdating)
            {
                Executors[index] = null;
                return;
            }

            Executors.RemoveAt(index);
        }

        private static void OnUpdate()
        {
            var deltaTime = Time.deltaTime;
            _isUpdating = true;

            try
            {
                // Executors created during this update are ticked starting from the next frame.
                var count = Executors.Count;

                for (var i = 0; i < count; i++)
                {
                    try
                    {
                        Executors[i]?.Tick(deltaTime);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception);
                    }
                }
            }
            finally
            {
                _isUpdating = false;
                Executors.RemoveAll(executor => executor == null);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState()
        {
            // Survives into the next play session when domain reload is disabled.
            Executors.Clear();
            _isInstalled = false;
            _isUpdating = false;
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void InitializeEditor()
        {
            UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(UnityEditor.PlayModeStateChange state)
        {
            // Leaving play mode does not reload the domain: executors of the finished session must not keep ticking.
            if (state is UnityEditor.PlayModeStateChange.ExitingPlayMode)
            {
                Executors.Clear();
            }
        }
#endif

        private static void Install()
        {
            if (_isInstalled)
            {
                return;
            }

            var root = PlayerLoop.GetCurrentPlayerLoop();
            var phases = root.subSystemList;

            for (var i = 0; i < phases.Length; i++)
            {
                if (phases[i].type != typeof(UnityEngine.PlayerLoop.Update))
                {
                    continue;
                }

                var systems = new List<PlayerLoopSystem>(phases[i].subSystemList ?? Array.Empty<PlayerLoopSystem>());

                // A previous installation may still be there when domain reload is disabled.
                systems.RemoveAll(system => system.type == typeof(CommandExecutorUpdate));
                systems.Insert(0, new PlayerLoopSystem
                {
                    type = typeof(CommandExecutorUpdate),
                    updateDelegate = OnUpdate,
                });

                phases[i].subSystemList = systems.ToArray();
                break;
            }

            root.subSystemList = phases;
            PlayerLoop.SetPlayerLoop(root);
            _isInstalled = true;
        }
    }
}
