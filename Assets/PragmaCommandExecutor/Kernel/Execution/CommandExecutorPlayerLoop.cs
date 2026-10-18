using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.LowLevel;
using Phases = UnityEngine.PlayerLoop;

namespace Pragma.CommandExecutor
{
    /// <summary>
    /// Global tick: one system injected at the beginning of each player loop phase that auto-ticked executors use
    /// (for <c>Update</c>, the same place UniTask runs <c>PlayerLoopTiming.Update</c>), ticking every live executor of
    /// that phase. A phase is installed with its first executor.
    /// </summary>
    internal static class CommandExecutorPlayerLoop
    {
        // Marker type identifying the injected system inside a player loop phase.
        internal struct CommandExecutorUpdate
        {
        }

        private static readonly PhaseRunner[] Runners =
        {
            new(CommandUpdatePhase.EarlyUpdate, typeof(Phases.EarlyUpdate)),
            new(CommandUpdatePhase.FixedUpdate, typeof(Phases.FixedUpdate)),
            new(CommandUpdatePhase.PreUpdate, typeof(Phases.PreUpdate)),
            new(CommandUpdatePhase.Update, typeof(Phases.Update)),
            new(CommandUpdatePhase.PreLateUpdate, typeof(Phases.PreLateUpdate)),
            new(CommandUpdatePhase.PostLateUpdate, typeof(Phases.PostLateUpdate)),
        };

        public static void Register(CommandExecutor executor, CommandUpdatePhase phase)
        {
            GetRunner(phase).Register(executor);
        }

        public static void Unregister(CommandExecutor executor, CommandUpdatePhase phase)
        {
            GetRunner(phase).Unregister(executor);
        }

        /// <summary>
        /// Type of the top-level player loop system that <paramref name="phase"/> is ticked in.
        /// </summary>
        internal static Type GetPhaseType(CommandUpdatePhase phase)
        {
            return GetRunner(phase).PhaseType;
        }

        private static PhaseRunner GetRunner(CommandUpdatePhase phase)
        {
            for (var i = 0; i < Runners.Length; i++)
            {
                if (Runners[i].Phase == phase)
                {
                    return Runners[i];
                }
            }

            throw new ArgumentOutOfRangeException(nameof(phase), phase, "Unknown player loop phase");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState()
        {
            // Survives into the next play session when domain reload is disabled.
            for (var i = 0; i < Runners.Length; i++)
            {
                Runners[i].Reset();
            }
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
                for (var i = 0; i < Runners.Length; i++)
                {
                    Runners[i].Clear();
                }
            }
        }
#endif

        private sealed class PhaseRunner
        {
            private readonly List<CommandExecutor> _executors = new();
            private bool _isInstalled;
            private bool _isUpdating;

            public CommandUpdatePhase Phase { get; }
            public Type PhaseType { get; }

            public PhaseRunner(CommandUpdatePhase phase, Type phaseType)
            {
                Phase = phase;
                PhaseType = phaseType;
            }

            public void Register(CommandExecutor executor)
            {
                Install();
                _executors.Add(executor);
            }

            public void Unregister(CommandExecutor executor)
            {
                var index = _executors.IndexOf(executor);

                if (index < 0)
                {
                    return;
                }

                if (_isUpdating)
                {
                    _executors[index] = null;
                    return;
                }

                _executors.RemoveAt(index);
            }

            public void Clear()
            {
                _executors.Clear();
            }

            public void Reset()
            {
                _executors.Clear();
                _isInstalled = false;
                _isUpdating = false;
            }

            private void OnUpdate()
            {
                // Inside FixedUpdate both return the fixed time step.
                var deltaTime = Time.deltaTime;
                var unscaledDeltaTime = Time.unscaledDeltaTime;
                _isUpdating = true;

                try
                {
                    // Executors created during this update are ticked starting from the next one.
                    var count = _executors.Count;

                    for (var i = 0; i < count; i++)
                    {
                        try
                        {
                            _executors[i]?.TickFromPlayerLoop(deltaTime, unscaledDeltaTime);
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
                    _executors.RemoveAll(executor => executor == null);
                }
            }

            private void Install()
            {
                if (_isInstalled)
                {
                    return;
                }

                var root = PlayerLoop.GetCurrentPlayerLoop();
                var phases = root.subSystemList;

                for (var i = 0; i < phases.Length; i++)
                {
                    if (phases[i].type != PhaseType)
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
}
