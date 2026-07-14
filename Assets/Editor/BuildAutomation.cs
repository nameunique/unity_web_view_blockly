#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;

public static class BuildAutomation
{
    private const string DefaultWindowsBuildPath = @"D:\projects_builds\unity blockly test\win\blockly_unity.exe";

    public static void BuildWindowsDevelopment()
    {
        BuildWindows(BuildOptions.Development | BuildOptions.AllowDebugging);
    }

    public static void BuildWindowsRelease()
    {
        BuildWindows(BuildOptions.None);
    }

    private static void BuildWindows(BuildOptions options)
    {
        string outputPath = Environment.GetEnvironmentVariable("BLOCKLY_UNITY_BUILD_PATH");
        if (string.IsNullOrWhiteSpace(outputPath))
            outputPath = DefaultWindowsBuildPath;

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

        string[] scenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToArray();

        if (scenes.Length == 0)
            throw new InvalidOperationException("No enabled scenes found in EditorBuildSettings.");

        BuildPlayerOptions buildPlayerOptions = new()
        {
            scenes = scenes,
            locationPathName = outputPath,
            target = BuildTarget.StandaloneWindows64,
            options = options
        };

        BuildReport report = BuildPipeline.BuildPlayer(buildPlayerOptions);
        if (report.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException($"Build failed: {report.summary.result}");
    }
}
#endif
