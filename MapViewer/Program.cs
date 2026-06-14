using System.Numerics;
using FF11Dungeon.MapGen;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

// --- Map generation state ---
var config = new GenerationConfig
{
    GridRows = 3,
    GridColumns = 3,
    MapWidth = 60,
    MapHeight = 40,
};

GenerationResult currentResult = GenerateMap(config);

// --- Camera state ---
float cameraYaw = -90f;    // degrees
float cameraPitch = 45f;   // degrees (looking down at ~45)
float cameraDistance = 50f;
Vector3 cameraTarget = new(config.MapWidth / 2f, 0f, config.MapHeight / 2f);
const float PanSpeed = 20f;
const float ZoomSpeed = 3f;
const float OrbitSpeed = 0.3f;
const float MinPitch = 5f;
const float MaxPitch = 89f;
const float MinDistance = 5f;
const float MaxDistance = 200f;

// --- Mouse state ---
bool rightMouseDown = false;
Vector2 lastMousePos = Vector2.Zero;

// --- Silk.NET window setup ---
var options = WindowOptions.Default with
{
    Size = new Vector2D<int>(1280, 720),
    Title = "MapViewer 3D - Roguelike Dungeon",
};

IWindow window = Window.Create(options);

GL? gl = null;
IInputContext? inputCtx = null;
uint vao = 0, vbo = 0, shaderProgram = 0;
int vertexCount = 0;
bool needsRebuild = true;

window.Load += OnLoad;
window.Update += OnUpdate;
window.Render += OnRender;
window.Closing += OnClosing;

window.Run();

// =============================================================================
// Functions
// =============================================================================

GenerationResult GenerateMap(GenerationConfig cfg)
{
    var generator = new MapGenerator();
    var result = generator.Generate(cfg);

    if (!result.Success)
    {
        Console.WriteLine($"Generation failed: {result.FailureReason}");
        cfg = new GenerationConfig
        {
            GridRows = cfg.GridRows,
            GridColumns = cfg.GridColumns,
            MapWidth = cfg.MapWidth,
            MapHeight = cfg.MapHeight,
            Seed = (uint)Random.Shared.Next(),
        };
        result = generator.Generate(cfg);
    }

    Console.WriteLine($"Seed: {result.UsedSeed}, Rooms: {result.Rooms?.Count ?? 0}");
    return result;
}

void OnLoad()
{
    gl = window.CreateOpenGL();
    inputCtx = window.CreateInput();

    foreach (var keyboard in inputCtx.Keyboards)
    {
        keyboard.KeyDown += OnKeyDown;
    }

    foreach (var mouse in inputCtx.Mice)
    {
        mouse.MouseDown += OnMouseDown;
        mouse.MouseUp += OnMouseUp;
        mouse.MouseMove += OnMouseMove;
        mouse.Scroll += OnScroll;
    }

    // Create shader program
    shaderProgram = CreateShaderProgram(gl);

    // Create VAO/VBO
    vao = gl.GenVertexArray();
    vbo = gl.GenBuffer();

    gl.BindVertexArray(vao);
    gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);

    // Vertex format: position(vec3) + color(vec3) + normal(vec3) = 9 floats
    const uint stride = 9 * sizeof(float);
    unsafe
    {
        // Position attribute (location 0): 3 floats
        gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
        gl.EnableVertexAttribArray(0);
        // Color attribute (location 1): 3 floats
        gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, (void*)(3 * sizeof(float)));
        gl.EnableVertexAttribArray(1);
        // Normal attribute (location 2): 3 floats
        gl.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, stride, (void*)(6 * sizeof(float)));
        gl.EnableVertexAttribArray(2);
    }

    gl.BindVertexArray(0);

    gl.Enable(EnableCap.DepthTest);
}

void OnUpdate(double delta)
{
    if (inputCtx == null) return;
    float dt = (float)delta;

    foreach (var keyboard in inputCtx.Keyboards)
    {
        // Compute forward/right in the XZ plane based on camera yaw
        float yawRad = MathF.PI / 180f * cameraYaw;
        Vector3 forward = new(MathF.Cos(yawRad), 0f, MathF.Sin(yawRad));
        Vector3 right = new(-MathF.Sin(yawRad), 0f, MathF.Cos(yawRad));

        if (keyboard.IsKeyPressed(Key.W))
            cameraTarget += forward * PanSpeed * dt;
        if (keyboard.IsKeyPressed(Key.S))
            cameraTarget -= forward * PanSpeed * dt;
        if (keyboard.IsKeyPressed(Key.D))
            cameraTarget += right * PanSpeed * dt;
        if (keyboard.IsKeyPressed(Key.A))
            cameraTarget -= right * PanSpeed * dt;
    }
}

void OnRender(double delta)
{
    if (gl == null) return;

    if (needsRebuild)
    {
        RebuildVertexBuffer();
        needsRebuild = false;
    }

    gl.ClearColor(0.08f, 0.08f, 0.1f, 1.0f);
    gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

    gl.UseProgram(shaderProgram);

    // Build camera position from spherical coordinates
    float yawRad = MathF.PI / 180f * cameraYaw;
    float pitchRad = MathF.PI / 180f * cameraPitch;
    Vector3 cameraOffset = new(
        MathF.Cos(pitchRad) * MathF.Cos(yawRad),
        MathF.Sin(pitchRad),
        MathF.Cos(pitchRad) * MathF.Sin(yawRad)
    );
    Vector3 cameraPos = cameraTarget + cameraOffset * cameraDistance;

    // View matrix
    var view = Matrix4x4.CreateLookAt(cameraPos, cameraTarget, Vector3.UnitY);

    // Projection matrix
    var size = window.Size;
    float aspect = (float)size.X / size.Y;
    var proj = Matrix4x4.CreatePerspectiveFieldOfView(
        MathF.PI / 180f * 60f, aspect, 0.1f, 500f);

    // Set uniforms
    unsafe
    {
        int viewLoc = gl.GetUniformLocation(shaderProgram, "uView");
        gl.UniformMatrix4(viewLoc, 1, false, (float*)&view);

        int projLoc = gl.GetUniformLocation(shaderProgram, "uProjection");
        gl.UniformMatrix4(projLoc, 1, false, (float*)&proj);

        // Light direction (from upper-left, normalized)
        Vector3 lightDir = Vector3.Normalize(new Vector3(-0.5f, 1.0f, -0.3f));
        int lightLoc = gl.GetUniformLocation(shaderProgram, "uLightDir");
        gl.Uniform3(lightLoc, lightDir.X, lightDir.Y, lightDir.Z);
    }

    gl.BindVertexArray(vao);
    gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)vertexCount);
    gl.BindVertexArray(0);
}

void OnClosing()
{
    if (gl == null) return;
    gl.DeleteVertexArray(vao);
    gl.DeleteBuffer(vbo);
    gl.DeleteProgram(shaderProgram);
}

void OnKeyDown(IKeyboard keyboard, Key key, int scancode)
{
    if (key == Key.Space)
    {
        var newConfig = new GenerationConfig
        {
            GridRows = config.GridRows,
            GridColumns = config.GridColumns,
            MapWidth = config.MapWidth,
            MapHeight = config.MapHeight,
            Seed = (uint)Random.Shared.Next(),
        };
        currentResult = GenerateMap(newConfig);
        needsRebuild = true;
    }
    else if (key == Key.Escape)
    {
        window.Close();
    }
}

void OnMouseDown(IMouse mouse, MouseButton button)
{
    if (button == MouseButton.Right)
    {
        rightMouseDown = true;
        lastMousePos = mouse.Position;
    }
}

void OnMouseUp(IMouse mouse, MouseButton button)
{
    if (button == MouseButton.Right)
    {
        rightMouseDown = false;
    }
}

void OnMouseMove(IMouse mouse, Vector2 position)
{
    if (rightMouseDown)
    {
        float dx = position.X - lastMousePos.X;
        float dy = position.Y - lastMousePos.Y;

        cameraYaw += dx * OrbitSpeed;
        cameraPitch += dy * OrbitSpeed;
        cameraPitch = Math.Clamp(cameraPitch, MinPitch, MaxPitch);
    }
    lastMousePos = position;
}

void OnScroll(IMouse mouse, ScrollWheel scroll)
{
    cameraDistance -= scroll.Y * ZoomSpeed;
    cameraDistance = Math.Clamp(cameraDistance, MinDistance, MaxDistance);
}

void RebuildVertexBuffer()
{
    if (gl == null || currentResult.Grid == null) return;

    var grid = currentResult.Grid;
    var vertices = new List<float>(grid.Width * grid.Height * 36 * 9); // rough estimate

    // Build geometry for each tile
    for (int y = 0; y < grid.Height; y++)
    {
        for (int x = 0; x < grid.Width; x++)
        {
            var tile = grid[x, y];

            // World position: X maps to world X, Y (grid) maps to world Z
            float wx = x;
            float wz = y;

            if (tile == TileType.Wall)
            {
                // Solid cube at (wx, 0, wz) with size 1.0, extending from Y=0 to Y=1
                AddCube(vertices, wx, 0f, wz, 1f, 1f, 1f, 0.3f, 0.3f, 0.35f);
            }
            else
            {
                // Flat quad at Y=0
                var (r, g, b) = GetTileColor(tile);
                AddFloorQuad(vertices, wx, 0f, wz, 1f, 1f, r, g, b);
            }
        }
    }

    // Player spawn marker: small cube at Y=0.2, size 0.4
    if (currentResult.PlayerSpawn.HasValue)
    {
        var spawn = currentResult.PlayerSpawn.Value;
        float wx = spawn.X + 0.3f; // center the 0.4 cube
        float wz = spawn.Y + 0.3f;
        AddCube(vertices, wx, 0f, wz, 0.4f, 0.4f, 0.4f, 0.1f, 0.9f, 0.2f);
    }

    // Monster spawn markers: small cubes at Y=0.15, size 0.3
    if (currentResult.MonsterSpawns != null)
    {
        foreach (var monster in currentResult.MonsterSpawns)
        {
            float wx = monster.X + 0.35f; // center the 0.3 cube
            float wz = monster.Y + 0.35f;
            AddCube(vertices, wx, 0f, wz, 0.3f, 0.3f, 0.3f, 0.9f, 0.15f, 0.15f);
        }
    }

    vertexCount = vertices.Count / 9; // 9 floats per vertex

    // Upload to GPU
    gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
    unsafe
    {
        var data = vertices.ToArray();
        fixed (float* ptr = data)
        {
            gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(data.Length * sizeof(float)),
                ptr, BufferUsageARB.DynamicDraw);
        }
    }
    gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);

    // Reset camera target to map center
    cameraTarget = new Vector3(grid.Width / 2f, 0f, grid.Height / 2f);
    cameraDistance = MathF.Max(grid.Width, grid.Height) * 0.8f;
}

static (float r, float g, float b) GetTileColor(TileType tile) => tile switch
{
    TileType.Wall => (0.3f, 0.3f, 0.35f),
    TileType.Floor => (0.55f, 0.42f, 0.28f),
    TileType.Corridor => (0.45f, 0.38f, 0.25f),
    TileType.RoomEntrance => (0.6f, 0.75f, 0.2f),
    TileType.StairsDown => (0.2f, 0.3f, 0.8f),
    _ => (0.3f, 0.3f, 0.35f),
};

/// <summary>
/// Adds a floor quad (two triangles) at the given position, lying flat at Y=py.
/// Normal points up (0, 1, 0).
/// </summary>
static void AddFloorQuad(List<float> verts, float px, float py, float pz,
    float width, float depth, float r, float g, float b)
{
    // Normal: up
    float nx = 0f, ny = 1f, nz = 0f;

    // Corners: top-left = (px, py, pz), extends in +X and +Z
    float x0 = px, x1 = px + width;
    float z0 = pz, z1 = pz + depth;

    // Triangle 1
    AddVertex(verts, x0, py, z0, r, g, b, nx, ny, nz);
    AddVertex(verts, x1, py, z0, r, g, b, nx, ny, nz);
    AddVertex(verts, x1, py, z1, r, g, b, nx, ny, nz);
    // Triangle 2
    AddVertex(verts, x0, py, z0, r, g, b, nx, ny, nz);
    AddVertex(verts, x1, py, z1, r, g, b, nx, ny, nz);
    AddVertex(verts, x0, py, z1, r, g, b, nx, ny, nz);
}

/// <summary>
/// Adds a cube (6 faces, 12 triangles, 36 vertices) at the given position.
/// Position is the min corner; size extends in +X, +Y, +Z.
/// </summary>
static void AddCube(List<float> verts, float px, float py, float pz,
    float sx, float sy, float sz, float r, float g, float b)
{
    float x0 = px, x1 = px + sx;
    float y0 = py, y1 = py + sy;
    float z0 = pz, z1 = pz + sz;

    // Front face (Z+) normal (0,0,1)
    AddVertex(verts, x0, y0, z1, r, g, b, 0, 0, 1);
    AddVertex(verts, x1, y0, z1, r, g, b, 0, 0, 1);
    AddVertex(verts, x1, y1, z1, r, g, b, 0, 0, 1);
    AddVertex(verts, x0, y0, z1, r, g, b, 0, 0, 1);
    AddVertex(verts, x1, y1, z1, r, g, b, 0, 0, 1);
    AddVertex(verts, x0, y1, z1, r, g, b, 0, 0, 1);

    // Back face (Z-) normal (0,0,-1)
    AddVertex(verts, x1, y0, z0, r, g, b, 0, 0, -1);
    AddVertex(verts, x0, y0, z0, r, g, b, 0, 0, -1);
    AddVertex(verts, x0, y1, z0, r, g, b, 0, 0, -1);
    AddVertex(verts, x1, y0, z0, r, g, b, 0, 0, -1);
    AddVertex(verts, x0, y1, z0, r, g, b, 0, 0, -1);
    AddVertex(verts, x1, y1, z0, r, g, b, 0, 0, -1);

    // Top face (Y+) normal (0,1,0)
    AddVertex(verts, x0, y1, z0, r, g, b, 0, 1, 0);
    AddVertex(verts, x1, y1, z0, r, g, b, 0, 1, 0);
    AddVertex(verts, x1, y1, z1, r, g, b, 0, 1, 0);
    AddVertex(verts, x0, y1, z0, r, g, b, 0, 1, 0);
    AddVertex(verts, x1, y1, z1, r, g, b, 0, 1, 0);
    AddVertex(verts, x0, y1, z1, r, g, b, 0, 1, 0);

    // Bottom face (Y-) normal (0,-1,0)
    AddVertex(verts, x0, y0, z1, r, g, b, 0, -1, 0);
    AddVertex(verts, x1, y0, z1, r, g, b, 0, -1, 0);
    AddVertex(verts, x1, y0, z0, r, g, b, 0, -1, 0);
    AddVertex(verts, x0, y0, z1, r, g, b, 0, -1, 0);
    AddVertex(verts, x1, y0, z0, r, g, b, 0, -1, 0);
    AddVertex(verts, x0, y0, z0, r, g, b, 0, -1, 0);

    // Right face (X+) normal (1,0,0)
    AddVertex(verts, x1, y0, z0, r, g, b, 1, 0, 0);
    AddVertex(verts, x1, y0, z1, r, g, b, 1, 0, 0);
    AddVertex(verts, x1, y1, z1, r, g, b, 1, 0, 0);
    AddVertex(verts, x1, y0, z0, r, g, b, 1, 0, 0);
    AddVertex(verts, x1, y1, z1, r, g, b, 1, 0, 0);
    AddVertex(verts, x1, y1, z0, r, g, b, 1, 0, 0);

    // Left face (X-) normal (-1,0,0)
    AddVertex(verts, x0, y0, z1, r, g, b, -1, 0, 0);
    AddVertex(verts, x0, y0, z0, r, g, b, -1, 0, 0);
    AddVertex(verts, x0, y1, z0, r, g, b, -1, 0, 0);
    AddVertex(verts, x0, y0, z1, r, g, b, -1, 0, 0);
    AddVertex(verts, x0, y1, z0, r, g, b, -1, 0, 0);
    AddVertex(verts, x0, y1, z1, r, g, b, -1, 0, 0);
}

static void AddVertex(List<float> verts, float x, float y, float z,
    float r, float g, float b, float nx, float ny, float nz)
{
    verts.Add(x);
    verts.Add(y);
    verts.Add(z);
    verts.Add(r);
    verts.Add(g);
    verts.Add(b);
    verts.Add(nx);
    verts.Add(ny);
    verts.Add(nz);
}

static uint CreateShaderProgram(GL gl)
{
    const string vertexSource = @"
#version 330 core
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aColor;
layout(location = 2) in vec3 aNormal;

out vec3 vColor;
out vec3 vNormal;
out vec3 vWorldPos;

uniform mat4 uView;
uniform mat4 uProjection;

void main()
{
    vWorldPos = aPos;
    vNormal = aNormal;
    vColor = aColor;
    gl_Position = uProjection * uView * vec4(aPos, 1.0);
}
";

    const string fragmentSource = @"
#version 330 core
in vec3 vColor;
in vec3 vNormal;
in vec3 vWorldPos;

out vec4 FragColor;

uniform vec3 uLightDir;

void main()
{
    // Simple directional diffuse lighting
    vec3 norm = normalize(vNormal);
    float diff = max(dot(norm, uLightDir), 0.0);
    // Ambient + diffuse
    float ambient = 0.3;
    float lighting = ambient + (1.0 - ambient) * diff;
    vec3 color = vColor * lighting;
    FragColor = vec4(color, 1.0);
}
";

    uint vertexShader = gl.CreateShader(ShaderType.VertexShader);
    gl.ShaderSource(vertexShader, vertexSource);
    gl.CompileShader(vertexShader);
    CheckShaderCompile(gl, vertexShader, "vertex");

    uint fragmentShader = gl.CreateShader(ShaderType.FragmentShader);
    gl.ShaderSource(fragmentShader, fragmentSource);
    gl.CompileShader(fragmentShader);
    CheckShaderCompile(gl, fragmentShader, "fragment");

    uint program = gl.CreateProgram();
    gl.AttachShader(program, vertexShader);
    gl.AttachShader(program, fragmentShader);
    gl.LinkProgram(program);
    CheckProgramLink(gl, program);

    gl.DeleteShader(vertexShader);
    gl.DeleteShader(fragmentShader);

    return program;
}

static void CheckShaderCompile(GL gl, uint shader, string type)
{
    gl.GetShader(shader, ShaderParameterName.CompileStatus, out int status);
    if (status == 0)
    {
        string log = gl.GetShaderInfoLog(shader);
        Console.WriteLine($"ERROR: {type} shader compilation failed:\n{log}");
    }
}

static void CheckProgramLink(GL gl, uint program)
{
    gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out int status);
    if (status == 0)
    {
        string log = gl.GetProgramInfoLog(program);
        Console.WriteLine($"ERROR: Shader program link failed:\n{log}");
    }
}
