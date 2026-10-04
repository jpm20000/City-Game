using System.IO;
using UnityEditor;
using UnityEngine;

// "CityBuilder > Build WebGL" writes a browser build to Build/WebGL (index.html + Build/ + StreamingAssets/). Gzip with the
// decompression fallback works on any static host (itch.io, GitHub Pages) without special response headers. Test it through
// a local web server (`python -m http.server` inside the folder), not by opening index.html as a file. Switching the active
// build target to Web changes some ProjectSettings and Library state: switch back to Windows afterwards and check git status.
public static class WebGLBuild
{
    private const string ScenePath = "Assets/_Game/Scenes/Main.unity";

    [MenuItem("CityBuilder/Build WebGL")]
    public static void Build()
    {
        string folder = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Build", "WebGL");
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
        PlayerSettings.WebGL.decompressionFallback = true;
        var options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            target = BuildTarget.WebGL,
            locationPathName = folder,
            options = BuildOptions.None,
        };
        var report = BuildPipeline.BuildPlayer(options);
        Debug.Log($"WebGLBuild: {report.summary.result}, {report.summary.totalErrors} errors, {report.summary.totalSize / (1024 * 1024)} MB, {folder}");
    }
}
