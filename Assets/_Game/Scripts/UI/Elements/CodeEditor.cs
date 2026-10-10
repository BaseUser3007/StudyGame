using System;
using UnityEngine.UIElements;

public class CodeEditor
{
    public event Action<string> OnCodeChanged;

    private readonly TextField _codeField;
    private readonly Label _lineNumbers;

    private bool _focusedByMouse;

    public string Code => _codeField?.value ?? string.Empty;

    public CodeEditor(TextField codeField, Label lineNumbers)
    {
        _codeField = codeField;
        _lineNumbers = lineNumbers;

        RegisterCallbacks();
        UpdateLineNumbers();
    }

    // ─── Callbacks ────────────────────────────────────────────

    private void RegisterCallbacks()
    {
        _codeField.RegisterValueChangedCallback(evt =>
        {
            UpdateLineNumbers();
            OnCodeChanged?.Invoke(evt.newValue);
        });

        _codeField.RegisterCallback<ClickEvent>(
            _ => _focusedByMouse = true);

        _codeField.RegisterCallback<FocusInEvent>(evt =>
        {
            if (!_focusedByMouse)
            {
                _codeField.Blur();
                return;
            }
            _focusedByMouse = false;
            PlayerEventBus.PublishUIFocused();
        });

        _codeField.RegisterCallback<FocusOutEvent>(
            _ => PlayerEventBus.PublishUIUnfocused());
    }

    // ─── Line Numbers ─────────────────────────────────────────

    private void UpdateLineNumbers()
    {
        if (_lineNumbers == null) return;

        string code = _codeField?.value ?? string.Empty;
        int lineCount = 1;

        foreach (char c in code)
            if (c == '\n') lineCount++;

        var sb = new System.Text.StringBuilder();
        for (int i = 1; i <= lineCount; i++)
        {
            sb.Append(i);
            if (i < lineCount) sb.Append('\n');
        }

        _lineNumbers.text = sb.ToString();
    }

    // ─── Public API ───────────────────────────────────────────

    public void SetCode(string code)
    {
        if (_codeField == null) return;
        _codeField.value = code;
        UpdateLineNumbers();
    }
}