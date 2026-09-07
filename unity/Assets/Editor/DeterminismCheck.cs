using UnityEditor;
using UnityEngine;

/// <summary>
/// The editor's way of asking <see cref="Determinism"/> the question.
///
/// The check itself is in the runtime assembly, because the answer that is
/// still unknown is IL2CPP's and there is no IL2CPP in an editor. This reports
/// Mono's; a device build reports its own on launch.
/// </summary>
public static class DeterminismCheck
{
    [MenuItem("Croquet/Check determinism")]
    public static void Run() => Debug.Log(Determinism.Report());
}
