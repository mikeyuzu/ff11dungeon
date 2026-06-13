using System.Numerics;

namespace CharacterViewer;

/// <summary>
/// オービットカメラ - マウスドラッグで回転、右ドラッグでパン、スクロールでズーム
/// </summary>
public class Camera
{
    public Vector3 Position { get; private set; }
    public Vector3 Target { get; set; } = new(0f, 0.9f, 0f);
    public Matrix4x4 ViewMatrix => Matrix4x4.CreateLookAt(Position, Target, Vector3.UnitY);
    public Matrix4x4 ProjectionMatrix { get; private set; }

    private float _distance = 2.5f;
    private float _yaw = 0f;
    private float _pitch = 0.3f;
    private readonly float _fov = 45f;
    private float _aspectRatio;
    private bool _autoRotate;
    private readonly float _autoRotateSpeed = 0.5f;

    public bool AutoRotate
    {
        get => _autoRotate;
        set => _autoRotate = value;
    }

    public Camera(int width, int height)
    {
        _aspectRatio = (float)width / height;
        UpdateProjection();
        UpdatePosition();
    }

    public void Resize(int width, int height)
    {
        if (height == 0) height = 1;
        _aspectRatio = (float)width / height;
        UpdateProjection();
    }

    public void Orbit(float deltaX, float deltaY)
    {
        _yaw -= deltaX * 0.005f;
        _pitch += deltaY * 0.005f;
        _pitch = Math.Clamp(_pitch, -1.5f, 1.5f);
        UpdatePosition();
    }

    public void Pan(float deltaX, float deltaY)
    {
        // カメラの右方向と上方向を使ってパン
        var forward = Vector3.Normalize(Target - Position);
        var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        var up = Vector3.Cross(right, forward);

        var panSpeed = _distance * 0.002f;
        Target += right * (-deltaX * panSpeed) + up * (deltaY * panSpeed);
        UpdatePosition();
    }

    public void OnScroll(float delta)
    {
        _distance -= delta * 0.3f;
        _distance = Math.Clamp(_distance, 0.5f, 10f);
        UpdatePosition();
    }

    public void Update(float deltaTime)
    {
        if (_autoRotate)
        {
            _yaw += _autoRotateSpeed * deltaTime;
            UpdatePosition();
        }
    }

    /// <summary>
    /// モデルのバウンディングボックスに合わせてカメラを調整
    /// </summary>
    public void FitToModel(BoundingBox bounds)
    {
        var center = (bounds.Min + bounds.Max) * 0.5f;
        var size = bounds.Max - bounds.Min;
        var height = size.Y;

        Target = new Vector3(center.X, center.Y + height * 0.1f, center.Z);
        _distance = MathF.Max(size.X, MathF.Max(size.Y, size.Z)) * 1.5f;
        _pitch = 0.2f;
        _yaw = 0f;

        UpdatePosition();
    }

    /// <summary>
    /// レイキャスト用にスクリーン座標からワールド空間のレイを取得
    /// </summary>
    public Ray GetRayFromScreen(float screenX, float screenY, int screenWidth, int screenHeight)
    {
        // NDC coordinates
        var ndcX = (2.0f * screenX / screenWidth) - 1.0f;
        var ndcY = 1.0f - (2.0f * screenY / screenHeight);

        // Inverse projection
        Matrix4x4.Invert(ProjectionMatrix, out var invProj);
        Matrix4x4.Invert(ViewMatrix, out var invView);

        var nearPoint = Vector4.Transform(new Vector4(ndcX, ndcY, -1f, 1f), invProj);
        nearPoint /= nearPoint.W;
        var farPoint = Vector4.Transform(new Vector4(ndcX, ndcY, 1f, 1f), invProj);
        farPoint /= farPoint.W;

        var nearWorld = Vector4.Transform(nearPoint, invView);
        var farWorld = Vector4.Transform(farPoint, invView);

        var origin = new Vector3(nearWorld.X, nearWorld.Y, nearWorld.Z);
        var direction = Vector3.Normalize(
            new Vector3(farWorld.X, farWorld.Y, farWorld.Z) - origin);

        return new Ray(origin, direction);
    }

    private void UpdatePosition()
    {
        var x = _distance * MathF.Cos(_pitch) * MathF.Sin(_yaw);
        var y = _distance * MathF.Sin(_pitch);
        var z = _distance * MathF.Cos(_pitch) * MathF.Cos(_yaw);
        Position = Target + new Vector3(x, y, z);
    }

    private void UpdateProjection()
    {
        ProjectionMatrix = Matrix4x4.CreatePerspectiveFieldOfView(
            _fov * MathF.PI / 180f,
            _aspectRatio,
            0.01f,
            100f
        );
    }
}

public struct Ray(Vector3 origin, Vector3 direction)
{
    public Vector3 Origin = origin;
    public Vector3 Direction = direction;
}

public struct BoundingBox(Vector3 min, Vector3 max)
{
    public Vector3 Min = min;
    public Vector3 Max = max;

    public static BoundingBox Empty => new(
        new Vector3(float.MaxValue), new Vector3(float.MinValue));

    public void Expand(Vector3 point)
    {
        Min = Vector3.Min(Min, point);
        Max = Vector3.Max(Max, point);
    }

    public void Expand(BoundingBox other)
    {
        Min = Vector3.Min(Min, other.Min);
        Max = Vector3.Max(Max, other.Max);
    }
}
