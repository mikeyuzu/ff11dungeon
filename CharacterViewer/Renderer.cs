using Silk.NET.OpenGL;
using System.Numerics;

namespace CharacterViewer;

/// <summary>
/// OpenGLレンダラー - シェーダー管理、描画処理
/// </summary>
public class Renderer : IDisposable
{
    private readonly GL _gl;

    // シェーダープログラム
    private uint _basicProgram;      // 単色描画用
    private uint _texturedProgram;   // テクスチャ描画用
    private uint _boneProgram;       // ボーン表示用

    // グリッド
    private uint _gridVao;
    private uint _gridVbo;
    private int _gridVertexCount;

    public GL GL => _gl;

    /// <summary>
    /// レンダラーの初期化 - OpenGLの基本設定とシェーダー、グリッドのセットアップ
    /// </summary>
    /// <param name="gl"></param>
    public Renderer(GL gl)
    {
        _gl = gl;
        _gl.Enable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.CullFace);
        _gl.ClearColor(0.06f, 0.09f, 0.16f, 1.0f);

        InitBasicShader();
        InitTexturedShader();
        InitBoneShader();
        InitGrid();
    }

    /// <summary>
    /// フレームの開始 - 画面のクリアとカメラのビュー・プロジェクション行列のシェーダーへの設定
    /// </summary>
    /// <param name="camera"></param>
    public void BeginFrame(Camera camera)
    {
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        _gl.UseProgram(_basicProgram);
        SetUniformMatrix4(_basicProgram, "uView", camera.ViewMatrix);
        SetUniformMatrix4(_basicProgram, "uProjection", camera.ProjectionMatrix);
    }

    /// <summary>
    /// グリッドの描画 - シェーダーを切り替えて、半透明のグリッド線を描画
    /// </summary>
    public void DrawGrid()
    {
        _gl.UseProgram(_basicProgram);
        SetUniformMatrix4(_basicProgram, "uModel", Matrix4x4.Identity);
        SetUniformVec4(_basicProgram, "uColor", new Vector4(0.23f, 0.51f, 0.96f, 0.3f));

        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        _gl.BindVertexArray(_gridVao);
        _gl.DrawArrays(PrimitiveType.Lines, 0, (uint)_gridVertexCount);
        _gl.BindVertexArray(0);

        _gl.Disable(EnableCap.Blend);
    }

    /// <summary>
    /// メッシュの描画 - テクスチャの有無に応じてシェーダーを切り替え、ライティングとテクスチャを適用して描画
    /// </summary>
    /// <param name="vao"></param>
    /// <param name="indexCount"></param>
    /// <param name="textureId"></param>
    /// <param name="model"></param>
    /// <param name="camera"></param>
    /// <param name="opacity"></param>
    /// <param name="wireframe"></param>
    public void DrawMesh(uint vao, int indexCount, uint textureId, Matrix4x4 model, Camera camera,
        float opacity = 1.0f, bool wireframe = false)
    {
        if (wireframe)
        {
            _gl.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Line);
        }

        _gl.UseProgram(_texturedProgram);
        SetUniformMatrix4(_texturedProgram, "uModel", model);
        SetUniformMatrix4(_texturedProgram, "uView", camera.ViewMatrix);
        SetUniformMatrix4(_texturedProgram, "uProjection", camera.ProjectionMatrix);
        SetUniformFloat(_texturedProgram, "uOpacity", opacity);

        SetUniformVec3(_texturedProgram, "uLightDir", Vector3.Normalize(new Vector3(2f, 4f, 3f)));
        SetUniformVec3(_texturedProgram, "uLightColor", new Vector3(1f, 1f, 1f));
        SetUniformVec3(_texturedProgram, "uAmbient", new Vector3(0.3f, 0.3f, 0.35f));
        SetUniformVec3(_texturedProgram, "uViewPos", camera.Position);

        if (textureId > 0)
        {
            _gl.ActiveTexture(TextureUnit.Texture0);
            _gl.BindTexture(TextureTarget.Texture2D, textureId);
            SetUniformInt(_texturedProgram, "uTexture", 0);
            SetUniformInt(_texturedProgram, "uHasTexture", 1);
        }
        else
        {
            SetUniformInt(_texturedProgram, "uHasTexture", 0);
        }

        _gl.BindVertexArray(vao);
        unsafe
        {
            _gl.DrawElements(PrimitiveType.Triangles, (uint)indexCount, DrawElementsType.UnsignedInt, (void*)0);
        }
        _gl.BindVertexArray(0);

        if (wireframe)
        {
            _gl.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Fill);
        }
    }

    /// <summary>
    /// スキンメッシュの描画 - 現状は通常のメッシュ描画と同じだが、将来的にスキニング用のシェーダーに切り替える可能性がある
    /// </summary>
    /// <param name="vao"></param>
    /// <param name="indexCount"></param>
    /// <param name="textureId"></param>
    /// <param name="model"></param>
    /// <param name="camera"></param>
    /// <param name="opacity"></param>
    /// <param name="wireframe"></param>
    public void DrawSkinnedMesh(uint vao, int indexCount, uint textureId, Matrix4x4 model,
        Camera camera, float opacity = 1.0f, bool wireframe = false)
    {
        DrawMesh(vao, indexCount, textureId, model, camera, opacity, wireframe);
    }

    /// <summary>
    /// ボーンの描画 - 単色シェーダーを使用して、指定された位置と半径の球を描画。深度テストを一時的に無効化して常に前面に表示する
    /// </summary>
    /// <param name="position"></param>
    /// <param name="radius"></param>
    /// <param name="color"></param>
    /// <param name="camera"></param>
    public void DrawBoneSphere(Vector3 position, float radius, Vector4 color, Camera camera)
    {
        _gl.UseProgram(_boneProgram);

        var model = Matrix4x4.CreateScale(radius) * Matrix4x4.CreateTranslation(position);
        SetUniformMatrix4(_boneProgram, "uModel", model);
        SetUniformMatrix4(_boneProgram, "uView", camera.ViewMatrix);
        SetUniformMatrix4(_boneProgram, "uProjection", camera.ProjectionMatrix);
        SetUniformVec4(_boneProgram, "uColor", color);

        _gl.Disable(EnableCap.DepthTest);
        DrawSphere();
        _gl.Enable(EnableCap.DepthTest);
    }

    /// <summary>
    /// ボーンの接続線の描画 - 単色シェーダーを使用して、指定された始点と終点を結ぶ線を描画。深度テストを一時的に無効化して常に前面に表示する
    /// </summary>
    /// <param name="from"></param>
    /// <param name="to"></param>
    /// <param name="color"></param>
    /// <param name="camera"></param>
    public void DrawBoneLine(Vector3 from, Vector3 to, Vector4 color, Camera camera)
    {
        _gl.UseProgram(_basicProgram);
        SetUniformMatrix4(_basicProgram, "uModel", Matrix4x4.Identity);
        SetUniformMatrix4(_basicProgram, "uView", camera.ViewMatrix);
        SetUniformMatrix4(_basicProgram, "uProjection", camera.ProjectionMatrix);
        SetUniformVec4(_basicProgram, "uColor", color);

        float[] lineVerts = [from.X, from.Y, from.Z, to.X, to.Y, to.Z];
        DrawLineImmediate(lineVerts);
    }

    /// <summary>
    /// ビューとプロジェクション行列の設定 - 指定されたシェーダープログラムに対して、カメラのビュー行列とプロジェクション行列をユニフォームとして設定する
    /// </summary>
    /// <param name="program"></param>
    /// <param name="camera"></param>
    public void SetViewProjection(uint program, Camera camera)
    {
        SetUniformMatrix4(program, "uView", camera.ViewMatrix);
        SetUniformMatrix4(program, "uProjection", camera.ProjectionMatrix);
    }

    /// <summary>
    /// フレームの終了 - 現状は特に処理はないが、将来的に後処理やバッファのスワップなどをここで行う可能性がある
    /// </summary>
    public static void EndFrame()
    {
    }

    /// <summary>
    /// 基本的なシェーダープログラムの初期化 - 単色描画用の頂点シェーダーとフラグメントシェーダーを定義し、コンパイルしてプログラムを作成する
    /// </summary>
    private void InitBasicShader()
    {
        var vert = @"
#version 330 core
layout(location = 0) in vec3 aPosition;
uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProjection;
void main() {
    gl_Position = uProjection * uView * uModel * vec4(aPosition, 1.0);
}";

        var frag = @"
#version 330 core
out vec4 FragColor;
uniform vec4 uColor;
void main() {
    FragColor = uColor;
}";

        _basicProgram = CreateShaderProgram(vert, frag);
    }

    /// <summary>
    /// テクスチャ描画用のシェーダープログラムの初期化 - ライティングとテクスチャを組み合わせた頂点シェーダーとフラグメントシェーダーを定義し、コンパイルしてプログラムを作成する
    /// </summary>
    private void InitTexturedShader()
    {
        var vert = @"
#version 330 core
layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec3 aNormal;
layout(location = 2) in vec2 aTexCoord;

uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProjection;

out vec3 vNormal;
out vec2 vTexCoord;
out vec3 vWorldPos;

void main() {
    vec4 worldPos = uModel * vec4(aPosition, 1.0);
    vWorldPos = worldPos.xyz;
    vNormal = mat3(transpose(inverse(uModel))) * aNormal;
    vTexCoord = aTexCoord;
    gl_Position = uProjection * uView * worldPos;
}";

        var frag = @"
#version 330 core
in vec3 vNormal;
in vec2 vTexCoord;
in vec3 vWorldPos;
out vec4 FragColor;

uniform sampler2D uTexture;
uniform int uHasTexture;
uniform float uOpacity;
uniform vec3 uLightDir;
uniform vec3 uLightColor;
uniform vec3 uAmbient;
uniform vec3 uViewPos;

void main() {
    vec3 normal = normalize(vNormal);
    vec3 lightDir = normalize(uLightDir);
    
    float diff = max(dot(normal, lightDir), 0.0);
    vec3 diffuse = diff * uLightColor;
    
    vec3 viewDir = normalize(uViewPos - vWorldPos);
    vec3 halfDir = normalize(lightDir + viewDir);
    float spec = pow(max(dot(normal, halfDir), 0.0), 32.0);
    vec3 specular = spec * uLightColor * 0.3;
    
    float rim = 1.0 - max(dot(viewDir, normal), 0.0);
    vec3 rimColor = vec3(0.376, 0.647, 0.98) * pow(rim, 3.0) * 0.4;
    
    vec3 lighting = uAmbient + diffuse + specular + rimColor;
    
    vec4 texColor;
    if (uHasTexture == 1) {
        texColor = texture(uTexture, vTexCoord);
        if (texColor.a < 0.01) discard;
    } else {
        texColor = vec4(0.6, 0.65, 0.7, 1.0);
    }
    
    FragColor = vec4(texColor.rgb * lighting, texColor.a * uOpacity);
}";

        _texturedProgram = CreateShaderProgram(vert, frag);
    }

    /// <summary>
    /// ボーン表示用のシェーダープログラムの初期化 - 単色描画用のシェーダーをベースに、ボーン表示に特化した頂点シェーダーとフラグメントシェーダーを定義し、コンパイルしてプログラムを作成する
    /// </summary>
    private void InitBoneShader()
    {
        var vert = @"
#version 330 core
layout(location = 0) in vec3 aPosition;
uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProjection;
void main() {
    gl_Position = uProjection * uView * uModel * vec4(aPosition, 1.0);
}";

        var frag = @"
#version 330 core
out vec4 FragColor;
uniform vec4 uColor;
void main() {
    FragColor = uColor;
}";

        _boneProgram = CreateShaderProgram(vert, frag);
    }

    /// <summary>
    /// グリッドの初期化 - XZ平面にグリッド線を生成し、頂点データをVBOにアップロードしてVAOをセットアップする。グリッドは-5から+5の範囲で、20分割される
    /// </summary>
    private void InitGrid()
    {
        var vertices = new List<float>();
        const int gridSize = 5;
        const int divisions = 20;
        var step = (float)gridSize * 2 / divisions;

        for (int i = 0; i <= divisions; i++)
        {
            var pos = -gridSize + i * step;
            vertices.AddRange([pos, 0f, -(float)gridSize]);
            vertices.AddRange([pos, 0f, (float)gridSize]);
            vertices.AddRange([-(float)gridSize, 0f, pos]);
            vertices.AddRange([(float)gridSize, 0f, pos]);
        }

        _gridVertexCount = vertices.Count / 3;
        var data = vertices.ToArray();

        _gridVao = _gl.GenVertexArray();
        _gridVbo = _gl.GenBuffer();

        _gl.BindVertexArray(_gridVao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _gridVbo);

        unsafe
        {
            fixed (float* ptr = data)
            {
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(data.Length * sizeof(float)), ptr, BufferUsageARB.StaticDraw);
            }
        }

        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
        _gl.BindVertexArray(0);
    }

    private uint _sphereVao;
    private uint _sphereVbo;
    private uint _sphereEbo;
    private int _sphereIndexCount;
    private bool _sphereInitialized;

    /// <summary>
    /// 球の初期化 - UV球の頂点とインデックスを生成し、VBOとEBOにアップロードしてVAOをセットアップする。スタック数とスライス数は固定で、球の品質を決定する
    /// </summary>
    private void InitSphere()
    {
        if (_sphereInitialized) return;

        const int stacks = 8;
        const int slices = 12;
        var vertices = new List<float>();
        var indices = new List<uint>();

        for (int i = 0; i <= stacks; i++)
        {
            float phi = MathF.PI * i / stacks;
            for (int j = 0; j <= slices; j++)
            {
                float theta = 2.0f * MathF.PI * j / slices;
                float x = MathF.Sin(phi) * MathF.Cos(theta);
                float y = MathF.Cos(phi);
                float z = MathF.Sin(phi) * MathF.Sin(theta);
                vertices.AddRange([x, y, z]);
            }
        }

        for (int i = 0; i < stacks; i++)
        {
            for (int j = 0; j < slices; j++)
            {
                uint first = (uint)(i * (slices + 1) + j);
                uint second = first + (uint)(slices + 1);
                indices.AddRange([first, second, first + 1]);
                indices.AddRange([second, second + 1, first + 1]);
            }
        }

        _sphereIndexCount = indices.Count;
        var vertData = vertices.ToArray();
        var idxData = indices.ToArray();

        _sphereVao = _gl.GenVertexArray();
        _sphereVbo = _gl.GenBuffer();
        _sphereEbo = _gl.GenBuffer();

        _gl.BindVertexArray(_sphereVao);

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _sphereVbo);
        unsafe
        {
            fixed (float* ptr = vertData)
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertData.Length * sizeof(float)), ptr, BufferUsageARB.StaticDraw);
        }

        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _sphereEbo);
        unsafe
        {
            fixed (uint* ptr = idxData)
                _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(idxData.Length * sizeof(uint)), ptr, BufferUsageARB.StaticDraw);
        }

        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
        _gl.BindVertexArray(0);

        _sphereInitialized = true;
    }

    /// <summary>
    /// 球の描画 - 球のVAOをバインドして、インデックスを使用して三角形として描画する。描画後にVAOをアンバインドする
    /// </summary>
    private void DrawSphere()
    {
        InitSphere();
        _gl.BindVertexArray(_sphereVao);
        unsafe
        {
            _gl.DrawElements(PrimitiveType.Triangles, (uint)_sphereIndexCount, DrawElementsType.UnsignedInt, (void*)0);
        }
        _gl.BindVertexArray(0);
    }

    private uint _lineVao;
    private uint _lineVbo;
    private bool _lineInitialized;

    /// <summary>
    /// 線の即時描画 - 指定された頂点配列を使用して、線分を描画する。最初の呼び出しでVAOとVBOを生成し、以降は頂点データを更新して描画する。描画後にVAOをアンバインドする
    /// </summary>
    /// <param name="vertices"></param>
    private void DrawLineImmediate(float[] vertices)
    {
        if (!_lineInitialized)
        {
            _lineVao = _gl.GenVertexArray();
            _lineVbo = _gl.GenBuffer();
            _lineInitialized = true;
        }

        _gl.BindVertexArray(_lineVao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _lineVbo);

        unsafe
        {
            fixed (float* ptr = vertices)
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertices.Length * sizeof(float)), ptr, BufferUsageARB.DynamicDraw);
        }

        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
        _gl.DrawArrays(PrimitiveType.Lines, 0, (uint)(vertices.Length / 3));
        _gl.BindVertexArray(0);
    }

    /// <summary>
    /// シェーダープログラムの作成 - 頂点シェーダーとフラグメントシェーダーのソースコードを受け取り、それぞれをコンパイルしてからプログラムにリンクする。コンパイルやリンクに失敗した場合は例外をスローする
    /// </summary>
    /// <param name="vertexSource"></param>
    /// <param name="fragmentSource"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    private uint CreateShaderProgram(string vertexSource, string fragmentSource)
    {
        var vertexShader = CompileShader(ShaderType.VertexShader, vertexSource);
        var fragmentShader = CompileShader(ShaderType.FragmentShader, fragmentSource);

        var program = _gl.CreateProgram();
        _gl.AttachShader(program, vertexShader);
        _gl.AttachShader(program, fragmentShader);
        _gl.LinkProgram(program);

        _gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out var status);
        if (status == 0)
        {
            var log = _gl.GetProgramInfoLog(program);
            throw new Exception($"Shader link error: {log}");
        }

        _gl.DeleteShader(vertexShader);
        _gl.DeleteShader(fragmentShader);

        return program;
    }

    /// <summary>
    /// シェーダーのコンパイル - 指定されたシェーダータイプとソースコードを使用してシェーダーをコンパイルする。コンパイルに失敗した場合は例外をスローする
    /// </summary>
    /// <param name="type"></param>
    /// <param name="source"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    private uint CompileShader(ShaderType type, string source)
    {
        var shader = _gl.CreateShader(type);
        _gl.ShaderSource(shader, source);
        _gl.CompileShader(shader);

        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out var status);
        if (status == 0)
        {
            var log = _gl.GetShaderInfoLog(shader);
            throw new Exception($"Shader compile error ({type}): {log}");
        }

        return shader;
    }

    /// <summary>
    /// ユニフォーム行列の設定 - 指定されたシェーダープログラムとユニフォーム名に対して、4x4行列をユニフォームとして設定する。ユニフォームが存在しない場合は何もしない
    /// </summary>
    /// <param name="program"></param>
    /// <param name="name"></param>
    /// <param name="matrix"></param>
    public unsafe void SetUniformMatrix4(uint program, string name, Matrix4x4 matrix)
    {
        var location = _gl.GetUniformLocation(program, name);
        if (location >= 0)
        {
            _gl.UniformMatrix4(location, 1, false, (float*)&matrix);
        }
    }

    /// <summary>
    /// ユニフォームベクトルの設定 - 指定されたシェーダープログラムとユニフォーム名に対して、4成分のベクトルをユニフォームとして設定する。ユニフォームが存在しない場合は何もしない
    /// </summary>
    /// <param name="program"></param>
    /// <param name="name"></param>
    /// <param name="value"></param>
    private void SetUniformVec4(uint program, string name, Vector4 value)
    {
        var location = _gl.GetUniformLocation(program, name);
        if (location >= 0)
            _gl.Uniform4(location, value.X, value.Y, value.Z, value.W);
    }

    /// <summary>
    /// ユニフォームベクトルの設定 - 指定されたシェーダープログラムとユニフォーム名に対して、3成分のベクトルをユニフォームとして設定する。ユニフォームが存在しない場合は何もしない
    /// </summary>
    /// <param name="program"></param>
    /// <param name="name"></param>
    /// <param name="value"></param>
    private void SetUniformVec3(uint program, string name, Vector3 value)
    {
        var location = _gl.GetUniformLocation(program, name);
        if (location >= 0)
            _gl.Uniform3(location, value.X, value.Y, value.Z);
    }

    /// <summary>
    /// ユニフォームフロートの設定 - 指定されたシェーダープログラムとユニフォーム名に対して、単一のフロート値をユニフォームとして設定する。ユニフォームが存在しない場合は何もしない
    /// </summary>
    /// <param name="program"></param>
    /// <param name="name"></param>
    /// <param name="value"></param>
    private void SetUniformFloat(uint program, string name, float value)
    {
        var location = _gl.GetUniformLocation(program, name);
        if (location >= 0)
            _gl.Uniform1(location, value);
    }

    /// <summary>
    /// ユニフォーム整数の設定 - 指定されたシェーダープログラムとユニフォーム名に対して、単一の整数値をユニフォームとして設定する。ユニフォームが存在しない場合は何もしない
    /// </summary>
    /// <param name="program"></param>
    /// <param name="name"></param>
    /// <param name="value"></param>
    private void SetUniformInt(uint program, string name, int value)
    {
        var location = _gl.GetUniformLocation(program, name);
        if (location >= 0)
            _gl.Uniform1(location, value);
    }

    /// <summary>
    /// リソースの解放 - シェーダープログラム、VAO、VBOなどのOpenGLリソースを削除してクリーンアップする。GC.SuppressFinalizeを呼び出して、ファイナライザが呼び出されないようにする
    /// </summary>
    public void Dispose()
    {
        _gl.DeleteProgram(_basicProgram);
        _gl.DeleteProgram(_texturedProgram);
        _gl.DeleteProgram(_boneProgram);
        _gl.DeleteVertexArray(_gridVao);
        _gl.DeleteBuffer(_gridVbo);

        if (_sphereInitialized)
        {
            _gl.DeleteVertexArray(_sphereVao);
            _gl.DeleteBuffer(_sphereVbo);
            _gl.DeleteBuffer(_sphereEbo);
        }
        if (_lineInitialized)
        {
            _gl.DeleteVertexArray(_lineVao);
            _gl.DeleteBuffer(_lineVbo);
        }
        GC.SuppressFinalize(this);
    }
}
