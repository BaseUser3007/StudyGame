using UnityEngine.UIElements;

public class InventoryPopupController
{
    private readonly VisualElement _popup;
    private readonly Button _toggleButton;
    private bool _isOpen;

    public InventoryPopupController(Button toggleButton, VisualElement popup)
    {
        _toggleButton = toggleButton;
        _popup = popup;

        RegisterCallbacks();
    }

    private void RegisterCallbacks()
    {
        _toggleButton.clicked += Toggle;
    }

    private void Toggle()
    {
        _isOpen = !_isOpen;

        if (_isOpen)
            _popup.RemoveFromClassList("hidden");
        else
            _popup.AddToClassList("hidden");
    }

    public void Close()
    {
        _isOpen = false;
        _popup.AddToClassList("hidden");
    }
}