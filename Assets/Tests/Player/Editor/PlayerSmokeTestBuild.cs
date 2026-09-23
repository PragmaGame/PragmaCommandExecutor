using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Pragma.CommandExecutor.Tests
{
    /// <summary>
    /// Builds <see cref="SCENE_PATH"/> for the active build target with High managed code stripping.
    /// The project's stripping level is restored right after the build.
    /// </summary>
    public static class PlayerSmokeTestBuild
    {
        public const string SCENE_PATH = "Assets/Tests/Player/PlayerSmokeTest.unity";
        private const string OUTPUT_FOLDER = "Builds/PlayerSmokeTest";

        [MenuItem("Tools/Pragma Command Executor/Build Player Smoke Test (High Stripping)")]
        private static void BuildFromMenu()
        {
            var report = Build(GetDefaultOutputPath(EditorUserBuildSettings.activeBuildTarget));
            Debug.Log($"Player smoke test build: {report.summary.result}, {report.summary.outputPath}");

            if (report.summary.result == BuildResult.Succeeded)
            {
                EditorUtility.RevealInFinder(report.summary.outputPath);
            }
        }

        public static BuildReport Build(string outputPath)
        {
            var target = EditorUserBuildSettings.activeBuildTarget;
            var targetGroup = BuildPipeline.GetBuildTargetGroup(target);
            var namedTarget = NamedBuildTarget.FromBuildTargetGroup(targetGroup);
            var previousLevel = PlayerSettings.GetManagedStrippingLevel(namedTarget);

            try
            {
                PlayerSettings.SetManagedStrippingLevel(namedTarget, ManagedStrippingLevel.High);

                return BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { SCENE_PATH },
                    locationPathName = outputPath,
                    target = target,
                    targetGroup = targetGroup,
                    options = BuildOptions.None,
                });
            }
            finally
            {
                // The build saves the project settings with High stripping: write the restored level back to disk.
                PlayerSettings.SetManagedStrippingLevel(namedTarget, previousLevel);
                AssetDatabase.SaveAssets();
            }
        }

        private static string GetDefaultOutputPath(BuildTarget target)
        {
            return target switch
            {
                BuildTarget.Android => $"{OUTPUT_FOLDER}/Android/PlayerSmokeTest.apk",
                BuildTarget.StandaloneWindows or BuildTarget.StandaloneWindows64 => $"{OUTPUT_FOLDER}/Windows/PlayerSmokeTest.exe",
                BuildTarget.StandaloneOSX => $"{OUTPUT_FOLDER}/macOS/PlayerSmokeTest.app",
                _ => $"{OUTPUT_FOLDER}/{target}",
            };
        }
    }
}
