// Assets/Editor/BuildScript.cs
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class BuildScript
{
    static string Version => Environment.GetEnvironmentVariable("BUILD_VERSION") ?? PlayerSettings.bundleVersion;
    static string[] Scenes()
    {
        var l = new System.Collections.Generic.List<string>();
        foreach (var s in EditorBuildSettings.scenes) if (s.enabled) l.Add(s.path);
        return l.ToArray();
    }

    [MenuItem("Build/WebGL")]
    public static void BuildWebGL() => Run(BuildTarget.WebGL, false, Path.Combine("Builds", "WebGL", Version));
    [MenuItem("Build/Linux Server")]
    public static void BuildLinuxServer() => Run(BuildTarget.StandaloneLinux64, true,
        Path.Combine("Builds", "LinuxServer", Version, "MazeServer.x86_64"));

    // Run ONE target per Unity invocation: exiting inside Run would otherwise skip the second build.
    static void Run(BuildTarget target, bool server, string output)
    {
        Directory.CreateDirectory(server ? Path.GetDirectoryName(output) : output);
        if (target == BuildTarget.WebGL)
        {
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = false;
            PlayerSettings.WebGL.nameFilesAsHashes = true;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.runInBackground = true;
        }
        var opts = new BuildPlayerOptions
        {
            scenes = Scenes(), locationPathName = output, target = target,
            targetGroup = server ? BuildTargetGroup.Standalone : BuildTargetGroup.WebGL,
            options = BuildOptions.None
        };
        if (server)
        {
            EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Server;
            opts.subtarget = (int)StandaloneBuildSubtarget.Server;
        }
        var report = BuildPipeline.BuildPlayer(opts);
        bool ok = report.summary.result == BuildResult.Succeeded;
        Debug.Log(ok ? $"Build OK {target} -> {output} ({report.summary.totalSize} bytes)" : "Build failed: " + report.summary.result);
        if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        else if (!ok) throw new Exception("Build failed");
    }
}
