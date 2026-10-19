using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Тост «Achievement unlocked»: название, описание, награды. Появляется сверху по центру, исчезает сам
/// через <see cref="DisplayMs"/>, клик закрывает раньше. Несколько выполненных подряд — очередь, по одному.
/// Клавиатуру не трогает (ничего фокусируемого), мышь перехватывает только сам видимый тост.
/// Элементы — экземпляр шаблона AchievementToast.uxml (хост выдаёт RunScreens.GetOverlay).
/// </summary>
public class AchievementToasts
{
    private const string HiddenClass = "hidden";
    private const string VisibleClass = "achievement-toast--visible";
    private const long DisplayMs = 5000;
    // Должно совпадать с transition-duration в AchievementToast.uss
    private const long FadeMs = 300;

    private readonly Queue<AchievementDefinition> _queue = new Queue<AchievementDefinition>();
    private readonly VisualElement _toast;
    private readonly VisualElement _icon;
    private readonly Label _title;
    private readonly Label _description;
    private readonly Label _rewards;
    private readonly Label _more;

    private AchievementDefinition _current;
    private IVisualElementScheduledItem _hideTimer;
    private bool _hiding;

    /// <param name="root">Хост шаблона AchievementToast.uxml.</param>
    public AchievementToasts(VisualElement root)
    {
        _toast = root.Q<VisualElement>("achievement-toast");
        _icon = root.Q<VisualElement>("toast-icon");
        _title = root.Q<Label>("toast-title");
        _description = root.Q<Label>("toast-description");
        _rewards = root.Q<Label>("toast-rewards");
        _more = root.Q<Label>("toast-more");

        Debug.Assert(_toast != null && _icon != null && _title != null && _description != null
                     && _rewards != null && _more != null, "AchievementToasts: elements not found");

        // Тексты из ассетов — не разметка
        _title.enableRichText = false;
        _description.enableRichText = false;
        _rewards.enableRichText = false;

        _toast.RegisterCallback<PointerDownEvent>(HandleToastPointerDown);
    }

    // ─── Public API ───────────────────────────────────────────

    public void Enqueue(AchievementDefinition definition)
    {
        if (definition == null) return;

        _queue.Enqueue(definition);
        if (_current == null)
            ShowNext();
        else
            RefreshMore();
    }

    // ─── Show / hide ──────────────────────────────────────────

    private void ShowNext()
    {
        if (_queue.Count == 0)
        {
            _current = null;
            return;
        }

        _current = _queue.Dequeue();
        _hiding = false;

        _title.text = Loc.T(_current.Title);
        _description.text = Loc.T(_current.Description);
        _description.EnableInClassList(HiddenClass, string.IsNullOrEmpty(_current.Description));

        string rewards = AchievementText.FormatRewards(_current);
        _rewards.text = string.IsNullOrEmpty(rewards) ? string.Empty : Loc.F("Rewards: {0}", rewards);
        _rewards.EnableInClassList(HiddenClass, string.IsNullOrEmpty(rewards));

        _icon.EnableInClassList(HiddenClass, _current.Icon == null);
        _icon.style.backgroundImage = _current.Icon != null ? new StyleBackground(_current.Icon) : StyleKeyword.None;

        RefreshMore();

        // Сначала display, затем (в следующем кадре) класс видимости — чтобы сработал переход
        _toast.RemoveFromClassList(HiddenClass);
        _toast.schedule.Execute(FadeIn).StartingIn(20);

        _hideTimer?.Pause();
        _hideTimer = _toast.schedule.Execute(Hide).StartingIn(DisplayMs);
    }

    private void FadeIn()
    {
        // Тост могли закрыть кликом раньше, чем он начал появляться
        if (_current != null && !_hiding)
            _toast.AddToClassList(VisibleClass);
    }

    private void Hide()
    {
        if (_current == null || _hiding) return;
        _hiding = true;

        _hideTimer?.Pause();
        _toast.RemoveFromClassList(VisibleClass);
        _toast.schedule.Execute(HandleFadedOut).StartingIn(FadeMs);
    }

    private void HandleFadedOut()
    {
        _toast.AddToClassList(HiddenClass);
        _current = null;
        ShowNext();
    }

    private void RefreshMore()
    {
        _more.text = _queue.Count > 0 ? Loc.F("+{0} more", _queue.Count) : string.Empty;
        _more.EnableInClassList(HiddenClass, _queue.Count == 0);
    }

    private void HandleToastPointerDown(PointerDownEvent evt)
    {
        evt.StopPropagation();
        Hide();
    }
}
