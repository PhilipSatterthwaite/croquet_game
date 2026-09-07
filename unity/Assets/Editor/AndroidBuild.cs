using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Builds the Android player, from the menu or from a command line.
///
/// The settings it asserts are the ones a device build is wrong without, and
/// they are asserted HERE rather than only sitting in ProjectSettings because a
/// build that silently comes out as ARMv7-and-Mono is one that answers a
/// different question from the one being asked -- the whole point of putting
/// this on a phone is to see the game at IL2CPP speed on the architecture it
/// would actually ship on.
///
/// Called by ..\..\android.ps1, which is the way to run it.
/// </summary>
public static class AndroidBuild
{
    /// <summary>Where the apk lands, relative to the repo root.</summary>
    const string Output = "build/android/croquet.apk";

    /// <summary>Must match the package android.ps1 installs and launches.</summary>
    const string Package = "com.satterthwaite.croquet";

    [MenuItem("Croquet/Build for Android")]
    public static void FromMenu() => Run(development: true);

    /// <summary>
    /// The -executeMethod entry point. Reads `-development` off the command
    /// line and exits with a code, so a script can tell a failure from a
    /// success -- a batchmode Unity that merely stops is indistinguishable from
    /// one that worked.
    /// </summary>
    public static void Build()
    {
        bool dev = Environment.GetCommandLineArgs().Contains("-development");
        bool ok = Run(dev);
        EditorApplication.Exit(ok ? 0 : 1);
    }

    static bool Run(bool development)
    {
        var root = Directory.GetParent(Application.dataPath).Parent.FullName;
        var apk = Path.Combine(root, Output.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(apk));

        var android = NamedBuildTarget.Android;

        // A phone is 64-bit and Play has required it for years, and Mono here
        // would measure the wrong runtime entirely.
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);

        // Android needs an identifier of its own; there is no falling back to
        // the standalone one, and a build without it does not start.
        PlayerSettings.SetApplicationIdentifier(android, Package);

        // The game is landscape. A 2:1 court fitted by the width of a portrait
        // phone is a band across the middle with empty surround above and
        // below, so the two landscape rotations are the only ones offered.
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
        PlayerSettings.allowedAutorotateToPortrait = false;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
        PlayerSettings.allowedAutorotateToLandscapeRight = true;
        PlayerSettings.allowedAutorotateToLandscapeLeft = true;

        // An apk sideloads; an aab only goes through Play.
        EditorUserBuildSettings.buildAppBundle = false;

        // Unity signs with its own debug keystore when there is no custom one,
        // which is all a sideloaded build needs.
        PlayerSettings.Android.useCustomKeystore = false;

        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android &&
            !EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
        {
            Debug.LogError("Could not switch the active build target to Android. " +
                           "Is Android Build Support installed for this editor?");
            return false;
        }

        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        if (scenes.Length == 0)
        {
            Debug.LogError("No scenes are enabled in the build settings.");
            return false;
        }

        var options = BuildOptions.None;
        if (development)
            // AllowDebugging is what lets a profiler and the log attach over
            // adb, which is most of why a build goes onto a phone at all.
            options |= BuildOptions.Development | BuildOptions.AllowDebugging;

        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = apk,
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            options = options,
        });

        var summary = report.summary;
        if (summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"Android build OK: {apk}, {summary.totalSize / (1024 * 1024)} MB, " +
                      $"{summary.totalTime.TotalSeconds:F0}s, " +
                      $"{(development ? "development" : "release")}.");
            return true;
        }

        Debug.LogError($"Android build {summary.result}: {summary.totalErrors} errors.");
        return false;
    }
}
