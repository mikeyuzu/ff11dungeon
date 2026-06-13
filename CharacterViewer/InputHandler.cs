using Silk.NET.Input;
using System.Numerics;

namespace CharacterViewer;

/// <summary>
/// 入力管理 - マウスとキーボードの入力処理
/// </summary>
public class InputHandler
{
    private readonly IInputContext _input;
    private readonly Camera _camera;

    private Vector2 _lastMousePos;
    private bool _leftDragging;
    private bool _rightDragging;
    private bool _enabled = true;

    public event Action<Key>? OnKeyPress;

    /// <summary>
    /// コンストラクタ - 入力コンテキストとカメラを受け取り、マウスとキーボードのイベントハンドラーを登録
    /// </summary>
    /// <param name="input"></param>
    /// <param name="camera"></param>
    public InputHandler(IInputContext input, Camera camera)
    {
        _input = input;
        _camera = camera;

        foreach (var mouse in input.Mice)
        {
            mouse.Scroll += OnMouseScroll;
            mouse.MouseDown += OnMouseDown;
            mouse.MouseUp += OnMouseUp;
            mouse.MouseMove += OnMouseMove;
        }

        foreach (var keyboard in input.Keyboards)
        {
            keyboard.KeyDown += OnKeyDown;
        }
    }

    /// <summary>
    /// 入力の有効/無効を切り替える - 無効にするとドラッグ状態もリセット
    /// </summary>
    /// <param name="enabled"></param>
    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        if (!enabled)
        {
            _leftDragging = false;
            _rightDragging = false;
        }
    }

    /// <summary>
    /// フレームごとの更新 - カメラの自動回転などの時間依存の処理をここで行う
    /// </summary>
    /// <param name="deltaTime"></param>
    public void Update(float deltaTime)
    {
        _camera.Update(deltaTime);
    }

    /// <summary>
    /// マウスのボタンが押されたときの処理 - ドラッグ状態を更新し、マウス位置を記録
    /// </summary>
    /// <param name="mouse"></param>
    /// <param name="button"></param>
    private void OnMouseDown(IMouse mouse, MouseButton button)
    {
        if (!_enabled)
        {
            return;
        }
        var pos = new Vector2(mouse.Position.X, mouse.Position.Y);
        _lastMousePos = pos;

        if (button == MouseButton.Left)
        {
            _leftDragging = true;
        }
        if (button == MouseButton.Right)
        {
            _rightDragging = true;
        }
    }

    /// <summary>
    /// マウスのボタンが離されたときの処理 - ドラッグ状態をリセット
    /// </summary>
    /// <param name="mouse"></param>
    /// <param name="button"></param>
    private void OnMouseUp(IMouse mouse, MouseButton button)
    {
        if (button == MouseButton.Left)
        {
            _leftDragging = false;
        }
        if (button == MouseButton.Right)
        {
            _rightDragging = false;
        }
    }

    /// <summary>
    /// マウスが移動したときの処理 - ドラッグ状態に応じてカメラを回転またはパン
    /// </summary>
    /// <param name="mouse"></param>
    /// <param name="position"></param>
    private void OnMouseMove(IMouse mouse, Vector2 position)
    {
        var delta = position - _lastMousePos;
        _lastMousePos = position;

        if (!_enabled)
        {
            return;
        }

        if (_leftDragging)
        {
            _camera.Orbit(delta.X, delta.Y);
        }
        else if (_rightDragging)
        {
            _camera.Pan(delta.X, delta.Y);
        }
    }

    /// <summary>
    /// マウスホイールがスクロールされたときの処理 - カメラのズームを調整
    /// </summary>
    /// <param name="mouse"></param>
    /// <param name="scroll"></param>
    private void OnMouseScroll(IMouse mouse, ScrollWheel scroll)
    {
        if (!_enabled)
        {
            return;
        }
        _camera.OnScroll(scroll.Y);
    }

    /// <summary>
    /// キーボードのキーが押されたときの処理 - 登録されたイベントハンドラーを呼び出す
    /// </summary>
    /// <param name="keyboard"></param>
    /// <param name="key"></param>
    /// <param name="scancode"></param>
    private void OnKeyDown(IKeyboard keyboard, Key key, int scancode)
    {
        OnKeyPress?.Invoke(key);
    }
}
