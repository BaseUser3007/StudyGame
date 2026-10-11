using UnityEngine;
using UnityEngine.InputSystem;

public class CameraController : MonoBehaviour
{
    [SerializeField] private Camera mainCamera;

    [Header("Follow Settings")]
    [SerializeField] private float followSpeed = 5f;

    [Header("Pan Settings")]
    [SerializeField] private float panSpeed = 10f;

    [Header("Zoom Settings")]
    [Tooltip("Насколько меняется масштаб за один щелчок колеса (доля от текущего)")]
    [Range(0.01f, 0.5f)]
    [SerializeField] private float zoomStep = 0.15f;
    [Tooltip("Минимальный orthographic size — максимальное приближение")]
    [SerializeField] private float minZoom = 2f;
    [Tooltip("Максимальный orthographic size — максимальное отдаление")]
    [SerializeField] private float maxZoom = 12f;
    [SerializeField] private float zoomSmoothSpeed = 10f;

    private IMapDataProvider _mapData;
    private IPointerOverUIProvider _uiPointer;
    private Transform _playerTransform;
    private float _targetZoom;
    private CameraMode _currentMode;
    private Vector3 _targetPosition;
    private Vector2 _moveInput;

    private GameInputActions _inputActions;
    private InputAction _moveAction;
    private InputAction _returnAction;

    private enum CameraMode { Following, Panning }

    // ─── Инициализация ────────────────────────────────────────

    public void Initialize(
        IMapDataProvider mapData,
        Transform playerTransform,
        IPointerOverUIProvider uiPointer)
    {
        ValidateReferences(mapData, playerTransform);

        _mapData = mapData;
        _uiPointer = uiPointer;
        _playerTransform = playerTransform;
        _targetZoom = Mathf.Clamp(mainCamera.orthographicSize, minZoom, maxZoom);
        _currentMode = CameraMode.Following;
        _targetPosition = playerTransform.position;

        SetupInputActions();

        PlayerEventBus.OnUIFocused += OnUIFocused;
        PlayerEventBus.OnUIUnfocused += OnUIUnfocused;

        SnapToPlayer();
    }

    private void OnDestroy()
    {
        PlayerEventBus.OnUIFocused -= OnUIFocused;
        PlayerEventBus.OnUIUnfocused -= OnUIUnfocused;

        _inputActions?.Camera.Disable();
        _inputActions?.Dispose();
    }

    // ─── Validation ───────────────────────────────────────────

    private void ValidateReferences(IMapDataProvider mapData, Transform playerTransform)
    {
        Debug.Assert(mainCamera != null, "CameraController: mainCamera is not assigned");
        Debug.Assert(mainCamera == null || mainCamera.orthographic,
            "CameraController: zoom expects an orthographic camera");
        Debug.Assert(mapData != null, "CameraController: mapData is not assigned");
        Debug.Assert(playerTransform != null, "CameraController: playerTransform is not assigned");
    }

    // ─── Input Setup ──────────────────────────────────────────

    private void SetupInputActions()
    {
        _inputActions = new GameInputActions();

        _moveAction = _inputActions.Camera.Move;
        _returnAction = _inputActions.Camera.ReturnToPlayer;

        Debug.Assert(_moveAction != null, "CameraController: Move action not found");
        Debug.Assert(_returnAction != null, "CameraController: ReturnToPlayer action not found");

        _returnAction.performed += _ => ReturnToPlayer();

        _inputActions.Camera.Enable();
    }

    // ─── UI Focus ─────────────────────────────────────────────

    private void OnUIFocused()
    {
        _inputActions.Camera.Disable();
        _moveInput = Vector2.zero;
    }

    private void OnUIUnfocused()
    {
        _inputActions.Camera.Enable();
    }

    // ─── Update ───────────────────────────────────────────────

    private void Update()
    {
        HandleInput();
        HandleZoomInput();
        MoveCamera();
        ZoomCamera();
    }

    /// <summary>
    /// Колесо мыши читается напрямую (не через Camera action map): зум работает,
    /// даже пока пишут код, — но только если курсор над картой, а не над интерфейсом.
    /// </summary>
    private void HandleZoomInput()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null) return;

        float scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Approximately(scroll, 0f)) return;

        if (_uiPointer != null && _uiPointer.IsPointerOverUI(mouse.position.ReadValue()))
            return;

        // Величина scroll зависит от платформы и настроек Input System (±1 или ±120 за щелчок) —
        // берём только направление. Вверх — приближение.
        float factor = scroll > 0f ? 1f - zoomStep : 1f + zoomStep;
        _targetZoom = Mathf.Clamp(_targetZoom * factor, minZoom, maxZoom);
    }

    private void ZoomCamera()
    {
        mainCamera.orthographicSize = Mathf.Lerp(
            mainCamera.orthographicSize,
            _targetZoom,
            zoomSmoothSpeed * Time.deltaTime);
    }

    private void OnValidate()
    {
        minZoom = Mathf.Max(0.5f, minZoom);
        maxZoom = Mathf.Max(minZoom, maxZoom);
    }

    private void HandleInput()
    {
        _moveInput = _moveAction.ReadValue<Vector2>();

        if (_moveInput != Vector2.zero)
        {
            if (_currentMode != CameraMode.Panning)
                PlayerEventBus.PublishCameraMoving();

            _currentMode = CameraMode.Panning;
            _targetPosition += (Vector3)(_moveInput * panSpeed * Time.deltaTime);
            _targetPosition = ClampToBounds(_targetPosition);
        }
        else if (_currentMode == CameraMode.Panning)
        {
            PlayerEventBus.PublishCameraStopped();
        }

        if (_currentMode == CameraMode.Following)
            _targetPosition = _playerTransform.position;
    }

    private void ReturnToPlayer()
    {
        _currentMode = CameraMode.Following;
        _targetPosition = _playerTransform.position;
        PlayerEventBus.PublishCameraStopped();
    }

    private void MoveCamera()
    {
        Vector3 currentPos = mainCamera.transform.position;
        Vector3 targetWithZ = new Vector3(
            _targetPosition.x,
            _targetPosition.y,
            currentPos.z);

        mainCamera.transform.position = Vector3.Lerp(
            currentPos,
            targetWithZ,
            followSpeed * Time.deltaTime);
    }

    // ─── Helpers ──────────────────────────────────────────────

    private Vector3 ClampToBounds(Vector3 position)
    {
        return new Vector3(
            Mathf.Clamp(position.x, 0f, _mapData.MapWidth),
            Mathf.Clamp(position.y, 0f, _mapData.MapHeight),
            position.z);
    }

    private void SnapToPlayer()
    {
        Vector3 pos = _playerTransform.position;
        mainCamera.transform.position = new Vector3(
            pos.x,
            pos.y,
            mainCamera.transform.position.z);
    }
}