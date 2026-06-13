using Silk.NET.Maths;
using Silk.NET.Windowing;
using Silk.NET.OpenGL;
using Silk.NET.Input;
using Silk.NET.OpenGL.Extensions.ImGui;
using System.Numerics;
using ImGuiNET;
using System.Reflection;

namespace CharacterViewer;

/// <summary>
/// メインアプリケーション - ウィンドウ管理とレンダリングループ
/// </summary>
public class Application
{
    private IWindow _window = null!;
    private GL _gl = null!;
    private IInputContext _input = null!;

    private Renderer _renderer = null!;
    private Camera _camera = null!;
    private AssetManager _assetManager = null!;
    private CharacterModel _character = null!;
    private BoneViewer _boneViewer = null!;
    private InputHandler _inputHandler = null!;
    private ImGuiController _imguiController = null!;
    private WeaponUI _weaponUI = null!;

    private bool _boneViewerMode;
    private double _lastFrameTime;
    private bool _assetsLoaded;

    /// <summary>
    /// アプリケーションの開始 - ウィンドウの作成とイベントループの開始
    /// </summary>
    public void Run()
    {
        var options = WindowOptions.Default;
        options.Size = new Vector2D<int>(1280, 720);
        options.Title = "FF11 Dungeon - Character Viewer";
        options.VSync = true;
        options.API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.ForwardCompatible, new APIVersion(3, 3));

        _window = Window.Create(options);
        _window.Load += OnLoad;
        _window.Update += OnUpdate;
        _window.Render += OnRender;
        _window.Closing += OnClosing;
        _window.Resize += OnResize;

        _window.Run();
    }

    /// <summary>
    /// ウィンドウとOpenGLの初期化、アセットの非同期読み込み開始、ImGuiのセットアップ
    /// </summary>
    private void OnLoad()
    {
        _gl = _window.CreateOpenGL();
        _input = _window.CreateInput();

        _camera = new Camera(_window.Size.X, _window.Size.Y);
        _renderer = new Renderer(_gl);
        _assetManager = new AssetManager(_gl);
        _character = new CharacterModel(_assetManager);
        _boneViewer = new BoneViewer(_assetManager, _renderer);
        _inputHandler = new InputHandler(_input, _camera);
        _weaponUI = new WeaponUI(_character);

        // ImGui初期化
        _imguiController = new ImGuiController(_gl, _window, _input);

        // Asset フォルダを探す（複数の候補を試す）
        var candidates = new[]
        {
            // CharacterViewer/ から見た相対パス（dotnet run 時の CWD）
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "Assets")),
            // bin/Debug/net10.0/ から見た相対パス（実行ファイルからの相対）
            Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "Assets")),
            // ワークスペースルート直指定
            @"C:\project\ff11_dungeon\Assets",
        };

        var AssetBasePath = candidates.FirstOrDefault(Directory.Exists) ?? candidates[^1];
        // 日本語フォントを読み込む
        TryLoadJapaneseFont($"{AssetBasePath}\\Fonts\\meiryo.ttc", 18.0f);

        _inputHandler.OnKeyPress += OnKeyPress;

        Console.WriteLine($"[App] OpenGL: {_gl.GetStringS(StringName.Version)}");
        Console.WriteLine($"[App] Renderer: {_gl.GetStringS(StringName.Renderer)}");

        // アセットの非同期読み込み開始
        _ = LoadAssetsAsync();
    }

    /// <summary>
    /// 日本語フォントの読み込みとImGuiへの反映を試みる
    /// </summary>
    /// <param name="fontPath"></param>
    /// <param name="fontSize"></param>
    private void TryLoadJapaneseFont(string fontPath, float fontSize)
    {
        try
        {
            var io = ImGui.GetIO();
            io.Fonts.Clear();

            var glyphRanges = io.Fonts.GetGlyphRangesChineseFull();
            io.Fonts.AddFontFromFileTTF(fontPath, fontSize, null, glyphRanges);

            var controllerType = _imguiController?.GetType();
            if (controllerType != null)
            {
                string[] candidateNames =
                [
                    "RecreateFontDeviceTexture",
                    "RecreateFontTexture",
                    "RebuildFontAtlas",
                    "RebuildFonts",
                    "CreateDeviceObjects",
                    "RecreateDeviceObjects",
                    "ReloadFontTexture",
                    "UpdateFontTexture"
                ];

                bool invoked = false;
                foreach (var name in candidateNames)
                {
                    var method = controllerType.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (method != null && method.GetParameters().Length == 0)
                    {
                        method.Invoke(_imguiController, null);
                        if (_imguiController != null)
                        {
                            Console.WriteLine($"[App] ImGui font texture refreshed via {_imguiController.GetType().Name}.{name}()");
                        }
                        invoked = true;
                        break;
                    }
                }

                if (!invoked)
                {
                    var fallback = controllerType.GetMethod("CreateDeviceObjects", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (fallback != null && fallback.GetParameters().Length == 0)
                    {
                        fallback.Invoke(_imguiController, null);
                        if (_imguiController != null)
                        {
                            Console.WriteLine($"[App] ImGui font texture refreshed via {_imguiController.GetType().Name}.CreateDeviceObjects()");
                        }
                        invoked = true;
                    }
                }

                if (!invoked)
                {
                    Console.WriteLine("[App] 警告: ImGuiController でフォントテクスチャを再生成できませんでした。");
                }
            }
            else
            {
                Console.WriteLine("[App] 警告: _imguiController が null のためフォントテクスチャの再生成をスキップしました。");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[App] フォント読み込みエラー: {ex.Message}");
        }
    }

    /// <summary>
    /// キャラクターモデルと関連アセットの非同期読み込み。完了後にカメラの調整やUIの初期化を行う
    /// </summary>
    /// <returns></returns>
    private async Task LoadAssetsAsync()
    {
        try
        {
            Console.WriteLine("[App] Loading assets...");
            await _character.LoadAsync();
            Console.WriteLine("[App] Character loaded successfully.");

            // ボーンビューワー用にもデータを共有
            _boneViewer.SetCharacterData(_character);

            // カメラをキャラクターに合わせる
            _camera.FitToModel(CharacterModel.GetBounds());

            // 武器UIのボーンリスト初期化
            _weaponUI.InitBoneList();
            _assetsLoaded = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[App] Error loading assets: {ex.Message}");
        }
    }

    /// <summary>
    /// キーボード入力の処理 - Tabキーで表示モードを切り替える
    /// </summary>
    /// <param name="key"></param>
    private void OnKeyPress(Key key)
    {
        // ImGuiがキーボードをキャプチャ中は無視
        if (ImGuiNET.ImGui.GetIO().WantCaptureKeyboard) return;

        switch (key)
        {
            case Key.Tab:
                _boneViewerMode = !_boneViewerMode;
                Console.WriteLine($"[App] Mode: {(_boneViewerMode ? "Bone Viewer" : "Character Viewer")}");
                break;
        }
    }

    /// <summary>
    /// フレームごとの更新処理 - 入力の更新、キャラクターとカメラの状態更新を行う
    /// </summary>
    /// <param name="deltaTime"></param>
    private void OnUpdate(double deltaTime)
    {
        // ImGuiがマウスをキャプチャ中はカメラ操作を無効化
        _inputHandler.SetEnabled(!ImGuiNET.ImGui.GetIO().WantCaptureMouse);
        _inputHandler.Update((float)deltaTime);

        if (_boneViewerMode)
        {
            _boneViewer.Update((float)deltaTime);
        }
        else
        {
            _character.Update((float)deltaTime);
        }

        _lastFrameTime = deltaTime;
    }

    /// <summary>
    /// フレームごとの描画処理 - 3Dシーンの描画とImGuiの描画を行う
    /// </summary>
    /// <param name="deltaTime"></param>
    private void OnRender(double deltaTime)
    {
        _renderer.BeginFrame(_camera);
        _renderer.DrawGrid();

        if (_boneViewerMode)
        {
            _boneViewer.Render(_renderer, _camera);
        }
        else
        {
            _character.Render(_renderer, _camera);
        }

        Renderer.EndFrame();

        // ImGui描画（3Dの上に重ねる）
        _imguiController.Update((float)deltaTime);
        if (_assetsLoaded)
        {
            _weaponUI.Draw();
        }
        _imguiController.Render();
    }

    /// <summary>
    /// ウィンドウサイズ変更時の処理 - OpenGLのビューポートとカメラのアスペクト比を更新する
    /// </summary>
    /// <param name="size"></param>
    private void OnResize(Vector2D<int> size)
    {
        _gl.Viewport(size);
        _camera.Resize(size.X, size.Y);
    }

    /// <summary>
    /// アプリケーション終了時の処理 - 各種リソースの解放とシャットダウンログの出力
    /// </summary>
    private void OnClosing()
    {
        _imguiController.Dispose();
        _renderer.Dispose();
        _assetManager.Dispose();
        _input.Dispose();
        _gl.Dispose();
        Console.WriteLine("[App] Shutdown complete.");
    }
}
