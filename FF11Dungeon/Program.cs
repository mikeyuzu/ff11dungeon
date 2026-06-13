using Silk.NET.Maths;
using Silk.NET.Windowing;
using Silk.NET.OpenGL;

var options = WindowOptions.Default;
options.Size = new Vector2D<int>(1280, 720);
options.Title = "FF11 Dungeon";
options.VSync = true;
options.API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.ForwardCompatible, new APIVersion(3, 3));

var window = Window.Create(options);
GL? gl = null;

window.Load += () =>
{
    gl = window.CreateOpenGL();
    gl.ClearColor(0.06f, 0.09f, 0.16f, 1.0f);
    Console.WriteLine($"[FF11Dungeon] OpenGL: {gl.GetStringS(StringName.Version)}");
    Console.WriteLine($"[FF11Dungeon] Renderer: {gl.GetStringS(StringName.Renderer)}");
};

window.Render += (dt) =>
{
    gl?.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
};

window.Resize += (size) =>
{
    gl?.Viewport(size);
};

window.Closing += () =>
{
    gl?.Dispose();
    Console.WriteLine("[FF11Dungeon] Shutdown.");
};

window.Run();
