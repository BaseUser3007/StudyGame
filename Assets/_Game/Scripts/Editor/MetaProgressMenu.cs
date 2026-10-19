using UnityEditor;
using UnityEngine;

/// <summary>
/// Debug-пункт меню для тестов: сброс мета-прогресса (meta_progress.json в persistentDataPath).
/// Вне Play Mode файл переносится в копию .reset.bak; в Play Mode — как кнопка Reset progress
/// в лагере: сброс в памяти и перезагрузка сцены в лагерь. Библиотеку кода не трогает.
/// </summary>
public static class MetaProgressMenu
{
    private const string MenuPath = "CodeRogue/Reset Meta Progress";

    [MenuItem(MenuPath)]
    private static void ResetMetaProgress()
    {
        string path = MetaProgress.DefaultFilePath;
        if (!EditorUtility.DisplayDialog("Reset Meta Progress",
                $"Reset unlocks, achievements, hub stock, hub buildings and tutorial?\n\n{path}\n\n" +
                $"The previous file is kept as {MetaProgress.ResetBackupSuffix}. The code library is not touched.",
                "Reset", "Cancel"))
            return;

        if (EditorApplication.isPlaying)
        {
            GameSession.ResetProgress();
            return;
        }

        bool existed = MetaProgress.ResetFile(path);
        Debug.Log(existed
            ? $"Meta progress reset: {path} → {path}{MetaProgress.ResetBackupSuffix}"
            : $"Meta progress: no save file at {path}, nothing to reset");
    }
}
