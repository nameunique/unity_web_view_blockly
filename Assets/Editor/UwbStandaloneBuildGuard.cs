#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

internal sealed class UwbStandaloneBuildGuard : IPreprocessBuildWithReport, IPostprocessBuildWithReport
{
    private const string MacArchitectureGroup = "OSXUniversal";
    private const string MacArchitectureKey = "Architecture";
    private const string DefaultMacArchitecture = "x64";
    private const string EngineExecutableName = "UnityWebBrowser.Engine.Cef";

    public int callbackOrder => 1000;

    [InitializeOnLoadMethod]
    private static void EnsureDefaultMacArchitecture()
    {
        string architecture = EditorUserBuildSettings.GetPlatformSettings(
            "Standalone",
            MacArchitectureGroup,
            MacArchitectureKey);

        if (string.IsNullOrWhiteSpace(architecture))
        {
            EditorUserBuildSettings.SetPlatformSettings(
                "Standalone",
                MacArchitectureGroup,
                MacArchitectureKey,
                DefaultMacArchitecture);
        }
    }

    public void OnPreprocessBuild(BuildReport report)
    {
        if (string.IsNullOrEmpty(GetRequiredEnginePackageName(report.summary.platform)))
            return;

        GetRequiredEnginePackage(report.summary.platform);
    }

    public void OnPostprocessBuild(BuildReport report)
    {
        if (report.summary.result is BuildResult.Failed or BuildResult.Cancelled)
            return;

        string packageName = GetRequiredEnginePackageName(report.summary.platform);
        if (string.IsNullOrEmpty(packageName))
            return;

        PackageInfo packageInfo = GetRequiredEnginePackage(report.summary.platform);
        string engineSourcePath = GetEnginePackageSourcePath(report.summary.platform, packageInfo.resolvedPath);
        string engineTargetPath = GetEngineBuildTargetPath(report.summary.platform, report.summary.outputPath);

        if (Directory.Exists(engineTargetPath))
            FileUtil.DeleteFileOrDirectory(engineTargetPath);

        Directory.CreateDirectory(Path.GetDirectoryName(engineTargetPath));
        FileUtil.CopyFileOrDirectory(engineSourcePath, engineTargetPath);
        Debug.Log($"Copied UWB CEF engine '{packageName}' to '{engineTargetPath}'.");
    }

    private static PackageInfo GetRequiredEnginePackage(BuildTarget buildTarget)
    {
        string packageName = GetRequiredEnginePackageName(buildTarget);
        PackageInfo packageInfo = PackageInfo.FindForAssetPath($"Packages/{packageName}/package.json");
        if (packageInfo == null)
        {
            throw new BuildFailedException(
                $"Missing UWB CEF engine package '{packageName}'. Let Unity resolve Packages/manifest.json before building.");
        }

        string enginePath = Path.Combine(packageInfo.resolvedPath, "Engine~");
        if (!Directory.Exists(enginePath))
        {
            throw new BuildFailedException(
                $"UWB CEF engine files were not found at '{enginePath}'. Reimport or reinstall package '{packageName}'.");
        }

        return packageInfo;
    }

    private static string GetEnginePackageSourcePath(BuildTarget buildTarget, string packageResolvedPath)
    {
        string engineSourcePath = Path.Combine(packageResolvedPath, "Engine~");
        return buildTarget == BuildTarget.StandaloneOSX
            ? Path.Combine(engineSourcePath, $"{EngineExecutableName}.app")
            : engineSourcePath;
    }

    private static string GetEngineBuildTargetPath(BuildTarget buildTarget, string buildOutputFullPath)
    {
        string buildAppName = Path.GetFileNameWithoutExtension(buildOutputFullPath);
        string buildOutputPath = Path.GetDirectoryName(buildOutputFullPath);

        if (buildTarget == BuildTarget.StandaloneOSX)
        {
            return Path.Combine(
                buildOutputPath,
                $"{buildAppName}.app",
                "Contents",
                "Frameworks",
                $"{EngineExecutableName}.app");
        }

        return Path.Combine(buildOutputPath, $"{buildAppName}_Data", "UWB");
    }

    private static string GetRequiredEnginePackageName(BuildTarget buildTarget)
    {
        return buildTarget switch
        {
            BuildTarget.StandaloneWindows64 => "dev.voltstro.unitywebbrowser.engine.cef.win.x64",
            BuildTarget.StandaloneLinux64 => "dev.voltstro.unitywebbrowser.engine.cef.linux.x64",
            BuildTarget.StandaloneOSX => GetMacEnginePackageName(),
            BuildTarget.StandaloneWindows => throw new BuildFailedException(
                "UnityWebBrowser CEF supports Windows x64 only. Use StandaloneWindows64."),
            _ => string.Empty
        };
    }

    private static string GetMacEnginePackageName()
    {
        string architecture = EditorUserBuildSettings.GetPlatformSettings(
            "Standalone",
            MacArchitectureGroup,
            MacArchitectureKey);

        if (string.IsNullOrWhiteSpace(architecture))
            architecture = DefaultMacArchitecture;

        return architecture switch
        {
            "x64" => "dev.voltstro.unitywebbrowser.engine.cef.macos.x64",
            "ARM64" => "dev.voltstro.unitywebbrowser.engine.cef.macos.arm64",
            _ => throw new BuildFailedException(
                $"UnityWebBrowser CEF supports macOS x64 or ARM64 builds. Current macOS architecture is '{architecture}'.")
        };
    }
}
#endif
