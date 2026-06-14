using System.Numerics;
using Silk.NET.OpenGL;
using MapViewer.MapGen;

namespace FF11Dungeon.Rendering;

/// <summary>
/// MapGridを3Dジオメトリとして描画するレンダラー。
/// 各タイルをワールド座標に変換し、TileTypeに応じた3Dアセットを配置する。
/// </summary>
public sealed class MapRenderer : IDisposable
{
    private readonly GL _gl;
    private readonly float _tileSize;

    // メッシュデータ
    private float[] _vertices = [];
    private uint[] _indices = [];

    // OpenGLバッファハンドル
    private uint _vao;
    private uint _vbo;
    private uint _ebo;
    private uint _shaderProgram;
    private int _indexCount;

    // Room_Entrance位置リスト（トリガーコライダー配置用）
    private readonly List<Vector3> _entranceTriggerPositions = [];

    /// <summary>
    /// Room_Entranceタイルのワールド座標リスト。
    /// 透明トリガーコライダー配置に使用する。
    /// </summary>
    public IReadOnlyList<Vector3> EntranceTriggerPositions => _entranceTriggerPositions;

    /// <summary>
    /// 現在のタイルサイズを取得する。
    /// </summary>
    public float TileSize => _tileSize;

    /// <summary>
    /// ビルド済みの頂点データを取得する（テスト・デバッグ用）。
    /// </summary>
    public ReadOnlySpan<float> Vertices => _vertices;

    /// <summary>
    /// ビルド済みのインデックスデータを取得する（テスト・デバッグ用）。
    /// </summary>
    public ReadOnlySpan<uint> Indices => _indices;

    public MapRenderer(GL gl, float tileSize = 1.0f)
    {
        _gl = gl ?? throw new ArgumentNullException(nameof(gl));
        _tileSize = tileSize;

        InitializeGLResources();
    }

    /// <summary>
    /// MapGridから3Dメッシュを構築する。
    /// 各タイル位置をグリッド座標(x, y)からワールド座標(x × tileSize, y × tileSize)に変換し、
    /// TileTypeに対応するジオメトリを生成する。
    /// </summary>
    public void BuildMesh(MapGrid grid)
    {
        ArgumentNullException.ThrowIfNull(grid);

        var vertices = new List<float>();
        var indices = new List<uint>();
        uint vertexOffset = 0;

        _entranceTriggerPositions.Clear();

        for (int y = 0; y < grid.Height; y++)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                var tileType = grid[x, y];
                float worldX = x * _tileSize;
                float worldZ = y * _tileSize;

                switch (tileType)
                {
                    case TileType.Wall:
                        vertexOffset = AppendWallGeometry(vertices, indices, vertexOffset, worldX, worldZ);
                        break;

                    case TileType.Floor:
                    case TileType.Corridor:
                        vertexOffset = AppendFloorGeometry(vertices, indices, vertexOffset, worldX, worldZ);
                        break;

                    case TileType.RoomEntrance:
                        vertexOffset = AppendFloorGeometry(vertices, indices, vertexOffset, worldX, worldZ);
                        // Room_Entrance位置を記録（透明トリガーコライダー配置用）
                        _entranceTriggerPositions.Add(new Vector3(
                            worldX + _tileSize * 0.5f,
                            0f,
                            worldZ + _tileSize * 0.5f));
                        break;

                    case TileType.StairsDown:
                        // 床 + 階段ジオメトリ
                        vertexOffset = AppendFloorGeometry(vertices, indices, vertexOffset, worldX, worldZ);
                        vertexOffset = AppendStairsGeometry(vertices, indices, vertexOffset, worldX, worldZ);
                        break;
                }
            }
        }

        _vertices = [.. vertices];
        _indices = [.. indices];
        _indexCount = _indices.Length;

        UploadMeshToGPU();
    }

    /// <summary>
    /// 構築済みメッシュを描画する。
    /// </summary>
    public void Render(Matrix4x4 viewProjection)
    {
        if (_indexCount == 0) return;

        _gl.UseProgram(_shaderProgram);

        // MVP行列をシェーダーに送信
        int mvpLocation = _gl.GetUniformLocation(_shaderProgram, "uMVP");
        if (mvpLocation >= 0)
        {
            unsafe
            {
                _gl.UniformMatrix4(mvpLocation, 1, false, (float*)&viewProjection);
            }
        }

        _gl.BindVertexArray(_vao);
        unsafe
        {
            _gl.DrawElements(PrimitiveType.Triangles, (uint)_indexCount, DrawElementsType.UnsignedInt, null);
        }
        _gl.BindVertexArray(0);
    }

    /// <summary>
    /// タイル座標からワールド座標に変換する。
    /// </summary>
    public Vector3 TileToWorld(int tileX, int tileY)
    {
        return new Vector3(tileX * _tileSize, 0f, tileY * _tileSize);
    }

    public void Dispose()
    {
        if (_vao != 0)
        {
            _gl.DeleteVertexArray(_vao);
            _vao = 0;
        }
        if (_vbo != 0)
        {
            _gl.DeleteBuffer(_vbo);
            _vbo = 0;
        }
        if (_ebo != 0)
        {
            _gl.DeleteBuffer(_ebo);
            _ebo = 0;
        }
        if (_shaderProgram != 0)
        {
            _gl.DeleteProgram(_shaderProgram);
            _shaderProgram = 0;
        }
    }

    #region Private - GL初期化

    private void InitializeGLResources()
    {
        _vao = _gl.GenVertexArray();
        _vbo = _gl.GenBuffer();
        _ebo = _gl.GenBuffer();
        _shaderProgram = CreateShaderProgram();
    }

    private uint CreateShaderProgram()
    {
        const string vertexShaderSource = @"
#version 330 core
layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec3 aColor;

uniform mat4 uMVP;

out vec3 vColor;

void main()
{
    gl_Position = uMVP * vec4(aPosition, 1.0);
    vColor = aColor;
}
";

        const string fragmentShaderSource = @"
#version 330 core
in vec3 vColor;
out vec4 FragColor;

void main()
{
    FragColor = vec4(vColor, 1.0);
}
";

        uint vertexShader = CompileShader(ShaderType.VertexShader, vertexShaderSource);
        uint fragmentShader = CompileShader(ShaderType.FragmentShader, fragmentShaderSource);

        uint program = _gl.CreateProgram();
        _gl.AttachShader(program, vertexShader);
        _gl.AttachShader(program, fragmentShader);
        _gl.LinkProgram(program);

        _gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out int linkStatus);
        if (linkStatus == 0)
        {
            string infoLog = _gl.GetProgramInfoLog(program);
            throw new InvalidOperationException($"Shader program link failed: {infoLog}");
        }

        _gl.DeleteShader(vertexShader);
        _gl.DeleteShader(fragmentShader);

        return program;
    }

    private uint CompileShader(ShaderType type, string source)
    {
        uint shader = _gl.CreateShader(type);
        _gl.ShaderSource(shader, source);
        _gl.CompileShader(shader);

        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out int compileStatus);
        if (compileStatus == 0)
        {
            string infoLog = _gl.GetShaderInfoLog(shader);
            _gl.DeleteShader(shader);
            throw new InvalidOperationException($"Shader compilation failed ({type}): {infoLog}");
        }

        return shader;
    }

    #endregion

    #region Private - メッシュデータアップロード

    private void UploadMeshToGPU()
    {
        _gl.BindVertexArray(_vao);

        // 頂点バッファ
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        unsafe
        {
            fixed (float* ptr = _vertices)
            {
                _gl.BufferData(BufferTargetARB.ArrayBuffer,
                    (nuint)(_vertices.Length * sizeof(float)),
                    ptr, BufferUsageARB.StaticDraw);
            }
        }

        // インデックスバッファ
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
        unsafe
        {
            fixed (uint* ptr = _indices)
            {
                _gl.BufferData(BufferTargetARB.ElementArrayBuffer,
                    (nuint)(_indices.Length * sizeof(uint)),
                    ptr, BufferUsageARB.StaticDraw);
            }
        }

        // 頂点属性: Position (location=0), 3 floats
        const uint stride = 6 * sizeof(float); // 3 position + 3 color
        _gl.EnableVertexAttribArray(0);
        unsafe
        {
            _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
        }

        // 頂点属性: Color (location=1), 3 floats
        _gl.EnableVertexAttribArray(1);
        unsafe
        {
            _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, (void*)(3 * sizeof(float)));
        }

        _gl.BindVertexArray(0);
    }

    #endregion

    #region Private - ジオメトリ生成

    /// <summary>
    /// 壁タイル用のキューブジオメトリを追加する。
    /// キューブ中心: (worldX + tileSize/2, tileSize/2, worldZ + tileSize/2)
    /// </summary>
    private uint AppendWallGeometry(List<float> vertices, List<uint> indices, uint offset, float worldX, float worldZ)
    {
        float halfSize = _tileSize * 0.5f;
        float cx = worldX + halfSize;
        float cy = halfSize; // 壁はY=0からtileSizeの高さ
        float cz = worldZ + halfSize;

        // 壁の色 (暗灰色)
        float r = 0.3f, g = 0.3f, b = 0.35f;

        // キューブの8頂点 (position + color)
        float[] cubeVertices =
        [
            // Front face (Z+)
            cx - halfSize, cy - halfSize, cz + halfSize, r, g, b,
            cx + halfSize, cy - halfSize, cz + halfSize, r, g, b,
            cx + halfSize, cy + halfSize, cz + halfSize, r, g, b,
            cx - halfSize, cy + halfSize, cz + halfSize, r, g, b,
            // Back face (Z-)
            cx + halfSize, cy - halfSize, cz - halfSize, r, g, b,
            cx - halfSize, cy - halfSize, cz - halfSize, r, g, b,
            cx - halfSize, cy + halfSize, cz - halfSize, r, g, b,
            cx + halfSize, cy + halfSize, cz - halfSize, r, g, b,
            // Top face (Y+)
            cx - halfSize, cy + halfSize, cz + halfSize, r * 1.2f, g * 1.2f, b * 1.2f,
            cx + halfSize, cy + halfSize, cz + halfSize, r * 1.2f, g * 1.2f, b * 1.2f,
            cx + halfSize, cy + halfSize, cz - halfSize, r * 1.2f, g * 1.2f, b * 1.2f,
            cx - halfSize, cy + halfSize, cz - halfSize, r * 1.2f, g * 1.2f, b * 1.2f,
            // Bottom face (Y-)
            cx - halfSize, cy - halfSize, cz - halfSize, r * 0.6f, g * 0.6f, b * 0.6f,
            cx + halfSize, cy - halfSize, cz - halfSize, r * 0.6f, g * 0.6f, b * 0.6f,
            cx + halfSize, cy - halfSize, cz + halfSize, r * 0.6f, g * 0.6f, b * 0.6f,
            cx - halfSize, cy - halfSize, cz + halfSize, r * 0.6f, g * 0.6f, b * 0.6f,
            // Right face (X+)
            cx + halfSize, cy - halfSize, cz + halfSize, r * 0.8f, g * 0.8f, b * 0.8f,
            cx + halfSize, cy - halfSize, cz - halfSize, r * 0.8f, g * 0.8f, b * 0.8f,
            cx + halfSize, cy + halfSize, cz - halfSize, r * 0.8f, g * 0.8f, b * 0.8f,
            cx + halfSize, cy + halfSize, cz + halfSize, r * 0.8f, g * 0.8f, b * 0.8f,
            // Left face (X-)
            cx - halfSize, cy - halfSize, cz - halfSize, r * 0.8f, g * 0.8f, b * 0.8f,
            cx - halfSize, cy - halfSize, cz + halfSize, r * 0.8f, g * 0.8f, b * 0.8f,
            cx - halfSize, cy + halfSize, cz + halfSize, r * 0.8f, g * 0.8f, b * 0.8f,
            cx - halfSize, cy + halfSize, cz - halfSize, r * 0.8f, g * 0.8f, b * 0.8f,
        ];

        vertices.AddRange(cubeVertices);

        // 6面 × 2三角形 = 12三角形 × 3頂点 = 36インデックス
        uint[] cubeIndices =
        [
            // Front
            offset + 0, offset + 1, offset + 2,
            offset + 0, offset + 2, offset + 3,
            // Back
            offset + 4, offset + 5, offset + 6,
            offset + 4, offset + 6, offset + 7,
            // Top
            offset + 8, offset + 9, offset + 10,
            offset + 8, offset + 10, offset + 11,
            // Bottom
            offset + 12, offset + 13, offset + 14,
            offset + 12, offset + 14, offset + 15,
            // Right
            offset + 16, offset + 17, offset + 18,
            offset + 16, offset + 18, offset + 19,
            // Left
            offset + 20, offset + 21, offset + 22,
            offset + 20, offset + 22, offset + 23,
        ];

        indices.AddRange(cubeIndices);
        return offset + 24; // 24頂点追加
    }

    /// <summary>
    /// 床タイル用のフラットクワッドジオメトリを追加する。
    /// Y=0の水平面に配置。
    /// </summary>
    private uint AppendFloorGeometry(List<float> vertices, List<uint> indices, uint offset, float worldX, float worldZ)
    {
        // 床の色 (暖色系の茶色)
        float r = 0.45f, g = 0.35f, b = 0.25f;

        // Y=0の水平クワッド (4頂点)
        float[] quadVertices =
        [
            worldX,             0f, worldZ,             r, g, b,
            worldX + _tileSize, 0f, worldZ,             r, g, b,
            worldX + _tileSize, 0f, worldZ + _tileSize, r, g, b,
            worldX,             0f, worldZ + _tileSize, r, g, b,
        ];

        vertices.AddRange(quadVertices);

        uint[] quadIndices =
        [
            offset + 0, offset + 1, offset + 2,
            offset + 0, offset + 2, offset + 3,
        ];

        indices.AddRange(quadIndices);
        return offset + 4; // 4頂点追加
    }

    /// <summary>
    /// 階段ジオメトリを追加する。
    /// 床の上に小さなステップを積み重ねた形状。
    /// </summary>
    private uint AppendStairsGeometry(List<float> vertices, List<uint> indices, uint offset, float worldX, float worldZ)
    {
        // 階段の色 (やや明るい石色)
        float r = 0.5f, g = 0.5f, b = 0.55f;

        float stepCount = 3;
        float stepHeight = _tileSize * 0.15f;
        float stepDepth = _tileSize / stepCount;

        uint currentOffset = offset;

        for (int i = 0; i < (int)stepCount; i++)
        {
            float sy = i * stepHeight;
            float sz = worldZ + i * stepDepth;
            float stepTop = sy + stepHeight;

            // 各ステップを小さなボックスとして描画（上面のみ簡略化）
            float[] stepVertices =
            [
                // Top face of step
                worldX,             stepTop, sz,
                r, g, b,
                worldX + _tileSize, stepTop, sz,
                r, g, b,
                worldX + _tileSize, stepTop, sz + stepDepth,
                r, g, b,
                worldX,             stepTop, sz + stepDepth,
                r, g, b,
                // Front face of step
                worldX,             sy,      sz,
                r * 0.7f, g * 0.7f, b * 0.7f,
                worldX + _tileSize, sy,      sz,
                r * 0.7f, g * 0.7f, b * 0.7f,
                worldX + _tileSize, stepTop, sz,
                r * 0.7f, g * 0.7f, b * 0.7f,
                worldX,             stepTop, sz,
                r * 0.7f, g * 0.7f, b * 0.7f,
            ];

            vertices.AddRange(stepVertices);

            uint[] stepIndices =
            [
                // Top
                currentOffset + 0, currentOffset + 1, currentOffset + 2,
                currentOffset + 0, currentOffset + 2, currentOffset + 3,
                // Front
                currentOffset + 4, currentOffset + 5, currentOffset + 6,
                currentOffset + 4, currentOffset + 6, currentOffset + 7,
            ];

            indices.AddRange(stepIndices);
            currentOffset += 8; // 8頂点 per step
        }

        return currentOffset;
    }

    #endregion
}
