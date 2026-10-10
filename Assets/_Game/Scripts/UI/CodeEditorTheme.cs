using UnityEngine;

[CreateAssetMenu(fileName = "CodeEditorTheme", menuName = "CodeRogue/UI/CodeEditorTheme")]
public class CodeEditorTheme : ScriptableObject
{
    [Header("Syntax Highlighting Colors")]
    public Color keywordColor = new Color(0.86f, 0.47f, 0.25f);
    public Color apiColor = new Color(0.40f, 0.78f, 0.94f);
    public Color stringColor = new Color(0.60f, 0.85f, 0.45f);
    public Color commentColor = new Color(0.53f, 0.53f, 0.53f);
    public Color numberColor = new Color(0.78f, 0.60f, 0.94f);
    public Color defaultColor = new Color(0.95f, 0.95f, 0.95f);

    [Header("Log Colors")]
    public Color logColorSuccess = new Color(0.40f, 0.85f, 0.40f);
    public Color logColorWarning = new Color(0.95f, 0.80f, 0.20f);
    public Color logColorError = new Color(0.90f, 0.30f, 0.30f);
    public Color logColorInfo = new Color(0.75f, 0.75f, 0.75f);
}