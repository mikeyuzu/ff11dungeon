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

    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        if (!enabled)
        {
            _leftDragging = false;
            _rightDragging = false;
        }
    }

    public void Update(float deltaTime)
    {
        _camera.Update(deltaTime);
    }

    private void OnMouseDown(IMouse mouse, MouseButton button)
    {
        if (!_enabled) return;
        var pos = new Vector2(mouse.Position.X, mouse.Position.Y);
        _lastMousePos = pos;

        if (button == MouseButton.Left) _leftDragging = true;
        if (button == MouseButton.Right) _rightDragging = true;
    }

    private void OnMouseUp(IMouse mouse, MouseButton button)
    {
        if (button == MouseButton.Left) _leftDragging = false;
        if (button == MouseButton.Right) _rightDragging = false;
    }

    private void OnMouseMove(IMouse mouse, Vector2 position)
    {
        var delta = position - _lastMousePos;
        _lastMousePos = position;

        if (!_enabled) return;

        if (_leftDragging)
        {
            _camera.Orbit(delta.X, delta.Y);
        }
        else if (_rightDragging)
        {
            _camera.Pan(delta.X, delta.Y);
        }
    }

    private void OnMouseScroll(IMouse mouse, ScrollWheel scroll)
    {
        if (!_enabled) return;
        _camera.OnScroll(scroll.Y);
    }

    private void OnKeyDown(IKeyboard keyboard, Key key, int scancode)
    {
        OnKeyPress?.Invoke(key);
    }
}
