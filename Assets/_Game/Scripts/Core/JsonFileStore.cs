using System;
using System.IO;
using UnityEngine;

/// <summary>
/// Чтение и запись JSON-файлов сохранений (JsonUtility) в Application.persistentDataPath.
/// </summary>
public static class JsonFileStore
{
    public static string GetPath(string fileName) => Path.Combine(Application.persistentDataPath, fileName);

    /// <summary>
    /// Читает файл. Нет файла — false без предупреждений. Испорченный файл не затирается молча:
    /// рядом откладывается копия .bak, чтобы данные можно было достать руками.
    /// </summary>
    public static bool TryLoad<T>(string path, out T data) where T : class
    {
        data = null;
        if (!File.Exists(path)) return false;

        try
        {
            data = JsonUtility.FromJson<T>(File.ReadAllText(path));
            return data != null;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"JsonFileStore: failed to read {path}: {e.Message}");
            try { File.Copy(path, path + ".bak", true); }
            catch (Exception copyError) { Debug.LogWarning($"JsonFileStore: backup failed: {copyError.Message}"); }
            return false;
        }
    }

    /// <summary>
    /// Пишет во временный файл и подменяет им основной — при сбое старый файл остаётся целым.
    /// Исключения ввода-вывода пробрасываются вызывающему.
    /// </summary>
    public static void Write(string path, object data)
    {
        string tempPath = path + ".tmp";
        File.WriteAllText(tempPath, JsonUtility.ToJson(data, true));

        if (File.Exists(path))
            File.Replace(tempPath, path, null);
        else
            File.Move(tempPath, path);
    }
}
