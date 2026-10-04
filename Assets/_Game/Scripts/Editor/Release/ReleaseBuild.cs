using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

// M19g: the Windows release. "CityBuilder > Apply Release Settings" sets the product identity, version, icon and window
// defaults (run once, commit the ProjectSettings diff); "CityBuilder > Build Windows Release" produces
//   Build/Release/<Product>_<version>/<Product>.exe, <Product>_<version>_win64.zip and, when Inno Setup is installed,
//   <Product>_<version>_setup.exe (from Tools/release/Installer.iss.template),
// and writes Build/Release/release_log.txt. Build/ is ignored by git. A player build rewrites some ProjectSettings and
// Assets/Settings files: check `git status` afterwards (see the perf-benchmark skill, step 4).
public static class ReleaseBuild
{
    public const string Company = "J-man Studios";
    public const string Product = "Chronopolis";
    public const string Version = "1.0.0";
    private const string ScenePath = "Assets/_Game/Scenes/Main.unity";
    private const string IconFolder = "Assets/_Game/Art/Icon";
    private static readonly string[] s_InnoPaths =
    {
        @"C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        @"C:\Program Files\Inno Setup 6\ISCC.exe",
    };

    private static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);

    [MenuItem("CityBuilder/Apply Release Settings")]
    public static void ApplySettings()
    {
        PlayerSettings.companyName = Company;
        PlayerSettings.productName = Product;
        PlayerSettings.bundleVersion = Version;

        SetIcons();

        PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
        PlayerSettings.defaultIsNativeResolution = true;
        PlayerSettings.resizableWindow = true;
        PlayerSettings.runInBackground = false;
        PlayerSettings.allowFullscreenSwitch = true;
        AssetDatabase.SaveAssets();
        Debug.Log($"ReleaseBuild: {Product} {Version} by {Company}, icon set, full screen window, resizable.");
    }

    // The default icon is one texture; Windows standalone asks for a list of sizes, each given the matching picture.
    private static void SetIcons()
    {
        Texture2D Load(string size) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{IconFolder}/icon_{size}.png");
        Texture2D biggest = Load("256");
        if (biggest == null)
        {
            Debug.LogWarning($"ReleaseBuild: {IconFolder} has no icons; add the icon PNGs (Art/Icon/icon_<size>.png, from Tools/release/Chronopolis.ico).");
            return;
        }
        PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { biggest }, IconKind.Application);
        int[] sizes = PlayerSettings.GetIconSizes(NamedBuildTarget.Standalone, IconKind.Application);
        var icons = new Texture2D[sizes.Length];
        for (int i = 0; i < sizes.Length; i++)
        {
            icons[i] = Load(sizes[i].ToString()) ?? biggest;
        }
        if (icons.Length > 0) PlayerSettings.SetIcons(NamedBuildTarget.Standalone, icons, IconKind.Application);
    }

    [MenuItem("CityBuilder/Build Windows Release")]
    public static void Build()
    {
        var log = new StringBuilder();
        string root = Path.Combine(ProjectRoot, "Build", "Release");
        string name = $"{Product}_{PlayerSettings.bundleVersion}";
        string folder = Path.Combine(root, name);
        string logPath = Path.Combine(root, "release_log.txt");
        try
        {
            Directory.CreateDirectory(root);
            if (Directory.Exists(folder)) Directory.Delete(folder, true);

            log.AppendLine($"Build {DateTime.Now:yyyy-MM-dd HH:mm}: {PlayerSettings.productName} {PlayerSettings.bundleVersion}, {PlayerSettings.companyName}");
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                target = BuildTarget.StandaloneWindows64,
                locationPathName = Path.Combine(folder, Product + ".exe"),
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            log.AppendLine($"Result {summary.result}, {summary.totalErrors} errors, {summary.totalWarnings} warnings, " +
                $"{summary.totalTime.TotalSeconds:F0} s, {summary.totalSize / (1024 * 1024)} MB");
            if (summary.result != BuildResult.Succeeded) return;

            // The debug symbols and backups Unity writes next to a player are not shipped.
            foreach (string dir in Directory.GetDirectories(folder))
            {
                string leaf = Path.GetFileName(dir);
                if (leaf.EndsWith("_BurstDebugInformation_DoNotShip", StringComparison.Ordinal) ||
                    leaf.EndsWith("_BackUpThisFolder_ButDontShipItWithYourGame", StringComparison.Ordinal))
                {
                    Directory.Delete(dir, true);
                    log.AppendLine("removed " + leaf);
                }
            }
            log.AppendLine($"folder {folder}, {DirectorySize(folder) / (1024 * 1024)} MB");

            string zip = Path.Combine(root, name + "_win64.zip");
            if (File.Exists(zip)) File.Delete(zip);
            ZipFile.CreateFromDirectory(folder, zip, System.IO.Compression.CompressionLevel.Optimal, true);
            log.AppendLine($"zip {zip}, {new FileInfo(zip).Length / (1024 * 1024)} MB");

            BuildInstaller(root, folder, name, log);
        }
        catch (Exception e)
        {
            log.AppendLine("FAILED: " + e);
        }
        finally
        {
            File.WriteAllText(logPath, log.ToString());
            Debug.Log("ReleaseBuild:\n" + log);
        }
    }

    private static void BuildInstaller(string root, string folder, string name, StringBuilder log)
    {
        string compiler = Array.Find(s_InnoPaths, File.Exists);
        if (compiler == null)
        {
            log.AppendLine("Inno Setup not found: no installer (the zip is the release).");
            return;
        }
        string template = File.ReadAllText(Path.Combine(ProjectRoot, "Tools", "release", "Installer.iss.template"));
        string script = template
            .Replace("{{PRODUCT}}", Product)
            .Replace("{{COMPANY}}", Company)
            .Replace("{{VERSION}}", PlayerSettings.bundleVersion)
            .Replace("{{SOURCE}}", folder)
            .Replace("{{OUTPUT}}", root)
            .Replace("{{ICON}}", Path.Combine(ProjectRoot, "Tools", "release", Product + ".ico"))
            .Replace("{{EXE}}", Product + ".exe")
            .Replace("{{SETUPNAME}}", name + "_setup");
        string iss = Path.Combine(root, name + ".iss");
        File.WriteAllText(iss, script);

        var info = new ProcessStartInfo(compiler, "/Q \"" + iss + "\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        using (Process process = Process.Start(info))
        {
            string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit();
            log.AppendLine($"Inno Setup exit {process.ExitCode} {output.Trim()}");
        }
        string setup = Path.Combine(root, name + "_setup.exe");
        if (File.Exists(setup)) log.AppendLine($"installer {setup}, {new FileInfo(setup).Length / (1024 * 1024)} MB");
    }

    private static long DirectorySize(string folder)
    {
        long size = 0;
        foreach (string file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories)) size += new FileInfo(file).Length;
        return size;
    }
}
