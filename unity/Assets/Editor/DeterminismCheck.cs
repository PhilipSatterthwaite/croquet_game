using Croquet.Core;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Asks this build whether it agrees with the pinned reference match.
///
/// The test suite runs on CoreCLR. The game runs on Mono in the editor and
/// IL2CPP on a device, and nothing about a green suite says those three arrive
/// at the same doubles. Online play that sends STROKES rather than positions
/// rests entirely on their agreeing, so this makes it a question that can be
/// answered rather than assumed -- here, and on a device, from the same code.
///
/// Deliberately an editor menu item rather than a Unity test: the point is to
/// be able to run it inside a player on real hardware, which is where the
/// answer might actually be different.
/// </summary>
public static class DeterminismCheck
{
    [MenuItem("Croquet/Check determinism")]
    public static void Run() => Debug.Log(Report());

    /// <summary>The result as a line of text, so a player build can show it too.</summary>
    public static string Report()
    {
        var match = Reference.Play();
        bool agrees = match.Hash == Reference.Hash;

        var where = $"{Application.platform}, scripting {ScriptingBackend()}";

        if (agrees)
            return $"Determinism OK on {where}: {match.Count} strokes, " +
                   $"0x{match.Hash:X16} as pinned.";

        return $"DETERMINISM MISMATCH on {where}: this build lands on " +
               $"0x{match.Hash:X16}, the pinned value is 0x{Reference.Hash:X16}. " +
               "Sending strokes between this build and one that disagrees would " +
               "drift; send positions, or find the divergence first.";
    }

    static string ScriptingBackend()
    {
#if ENABLE_IL2CPP
        return "IL2CPP";
#else
        return "Mono";
#endif
    }
}
