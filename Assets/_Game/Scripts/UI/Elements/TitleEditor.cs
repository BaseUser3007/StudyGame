using System;
using UnityEngine;
using UnityEngine.UIElements;

public class TitleEditor
{
    public event Action<string> OnTitleChanged;

    private readonly Label _titleLabel;
    private readonly TextField _titleEdit;
    private readonly IIdentifierValidator _validator;

    private double _lastClickTime;
    private const double DoubleClickThreshold = 0.3;

    private bool _hasBeenRenamed;
    private string _currentTitle;

    public TitleEditor(Label titleLabel, TextField titleEdit, IIdentifierValidator validator)
    {
        _titleLabel = titleLabel;
        _titleEdit = titleEdit;
        _validator = validator;

        RegisterCallbacks();
    }

    // ─── Callbacks ────────────────────────────────────────────

    private void RegisterCallbacks()
    {
        _titleLabel.RegisterCallback<PointerDownEvent>(OnLabelPointerDown);

        _titleEdit.RegisterCallback<FocusInEvent>(
            _ => PlayerEventBus.PublishUIFocused());

        _titleEdit.RegisterCallback<FocusOutEvent>(_ =>
        {
            PlayerEventBus.PublishUIUnfocused();
            FinishEditing();
        });

        _titleEdit.RegisterCallback<KeyDownEvent>(OnTitleEditKeyDown);

        // Валидация в реальном времени
        _titleEdit.RegisterValueChangedCallback(evt =>
            ValidateInput(evt.newValue));
    }

    private void OnLabelPointerDown(PointerDownEvent evt)
    {
        double currentTime = Time.timeAsDouble;

        if (currentTime - _lastClickTime < DoubleClickThreshold)
            StartEditing();

        _lastClickTime = currentTime;
    }

    private void OnTitleEditKeyDown(KeyDownEvent evt)
    {
        if (evt.keyCode == KeyCode.Return ||
            evt.keyCode == KeyCode.KeypadEnter)
        {
            FinishEditing();
            _titleEdit.Blur();
        }
        else if (evt.keyCode == KeyCode.Escape)
        {
            CancelEditing();
            _titleEdit.Blur();
        }
    }

    // ─── Validation ───────────────────────────────────────────

    private void ValidateInput(string value)
    {
        bool isValid = _validator.IsValid(value);

        if (isValid)
            _titleEdit.RemoveFromClassList("title-edit-invalid");
        else
            _titleEdit.AddToClassList("title-edit-invalid");
    }

    // ─── Editing Logic ────────────────────────────────────────

    private void StartEditing()
    {
        _titleLabel.AddToClassList("hidden");
        _titleEdit.RemoveFromClassList("hidden");
        _titleEdit.value = _hasBeenRenamed ? _currentTitle : string.Empty;
        _titleEdit.Focus();
    }

    private void FinishEditing()
    {
        string newTitle = _titleEdit.value.Trim();

        if (_validator.IsValid(newTitle))
        {
            _currentTitle = newTitle;
            _hasBeenRenamed = true;
            OnTitleChanged?.Invoke(newTitle);
        }

        StopEditing();
    }

    private void CancelEditing()
    {
        StopEditing();
    }

    private void StopEditing()
    {
        _titleEdit.RemoveFromClassList("title-edit-invalid");
        _titleEdit.AddToClassList("hidden");
        _titleLabel.RemoveFromClassList("hidden");
    }

    // ─── Public API ───────────────────────────────────────────

    public void SetTitle(string title)
    {
        _currentTitle = title;
        _hasBeenRenamed = true;
        _titleEdit.value = title;
    }
}