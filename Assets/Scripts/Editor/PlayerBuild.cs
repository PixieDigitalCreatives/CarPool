using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace RacingPrototype.EditorTools
{
    /// <summary>Builds a standalone Windows player (main menu + 4 routes) into Builds/Windows.</summary>
    public static class PlayerBuild
    {
        const string OutputPath = "Builds/Windows/thegameioRacing.exe";

        [MenuItem("Racing Prototype/Build Windows Player", priority = 30)]
        public static void BuildWindowsMenu()
        {
            if (Build()) EditorUtility.RevealInFinder(Path.GetFullPath(OutputPath));
        }

        public static void BuildWindowsBatch()
        {
            try { EditorApplication.Exit(Build() ? 0 : 1); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        static bool Build()
        {
            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = OutputPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };
            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            Debug.Log($"[PlayerBuild] {summary.result}: {summary.totalErrors} errors, {summary.totalWarnings} warnings, " +
                      $"{summary.totalSize / (1024 * 1024)} MB, {summary.totalTime.TotalSeconds:F0} s -> {Path.GetFullPath(OutputPath)}");
            return summary.result == BuildResult.Succeeded;
        }
    }
}
