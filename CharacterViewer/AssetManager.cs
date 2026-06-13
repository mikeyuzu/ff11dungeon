using Silk.NET.OpenGL;
using Silk.NET.Assimp;
using System.Numerics;
using Pfim;

namespace CharacterViewer;

/// <summary>
/// アセット管理 - FBXモデルとTGAテクスチャの読み込み (Assimp + Pfim)
/// </summary>
public class AssetManager : IDisposable
{
    private readonly GL _gl;
    public GL GL => _gl;
    private readonly Assimp _assimp;
    private readonly Dictionary<string, uint> _textureCache = [];
    private readonly Dictionary<string, LoadedModel> _modelCache = [];

    public string AssetBasePath { get; set; }

    /// <summary>
    /// コンストラクタ - OpenGLコンテキストとAssimpの初期化、アセットディレクトリの検出
    /// </summary>
    /// <param name="gl"></param>
    public AssetManager(GL gl)
    {
        _gl = gl;
        _assimp = Assimp.GetApi();

        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "Assets")),
            Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "Assets")),
            @"C:\project\ff11_dungeon\Assets",
        };

        AssetBasePath = candidates.FirstOrDefault(Directory.Exists) ?? candidates[^1];
        Console.WriteLine($"[AssetManager] Asset path: {AssetBasePath}");
        Console.WriteLine($"[AssetManager] Exists: {Directory.Exists(AssetBasePath)}");
    }

    /// <summary>
    /// FBXファイルの読み込み - AssimpでパースしてLoadedModelに変換、GPUメッシュの作成、キャッシュ保存
    /// </summary>
    /// <param name="relativePath"></param>
    /// <returns></returns>
    public unsafe LoadedModel? LoadFbx(string relativePath)
    {
        if (_modelCache.TryGetValue(relativePath, out var cached))
        {
            return cached;
        }

        var fullPath = Path.Combine(AssetBasePath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!System.IO.File.Exists(fullPath))
        {
            Console.WriteLine($"[AssetManager] FBX not found: {fullPath}");
            return null;
        }

        Console.WriteLine($"[AssetManager] Loading FBX: {relativePath}");

        var scene = _assimp.ImportFile(fullPath,
            (uint)(PostProcessSteps.Triangulate |
                   PostProcessSteps.GenerateNormals |
                   PostProcessSteps.FlipUVs |
                   PostProcessSteps.CalculateTangentSpace));

        if (scene == null || scene->MRootNode == null)
        {
            var error = _assimp.GetErrorStringS();
            Console.WriteLine($"[AssetManager] Assimp error: {(string.IsNullOrEmpty(error) ? "scene is null" : error)}");
            return null;
        }

        var model = new LoadedModel
        {
            Name = Path.GetFileNameWithoutExtension(relativePath),
            RootTransform = Matrix4x4.Transpose(scene->MRootNode->MTransformation)
        };

        var meshTransforms = new Dictionary<int, Matrix4x4>();
        CollectMeshTransforms(scene->MRootNode, Matrix4x4.Identity, meshTransforms);

        float meshMaxCoord = 0;
        float meshMaxZ = 0;
        for (uint i = 0; i < scene->MNumMeshes; i++)
        {
            var m = scene->MMeshes[i];
            for (uint v = 0; v < m->MNumVertices; v++)
            {
                var vy = MathF.Abs(m->MVertices[v].Y);
                var vz = MathF.Abs(m->MVertices[v].Z);
                if (vy > meshMaxCoord) meshMaxCoord = vy;
                if (vz > meshMaxCoord) meshMaxCoord = vz;
                if (vz > meshMaxZ) meshMaxZ = vz;
            }
        }

        float unitScale = meshMaxCoord > 10f ? 0.01f : 1.0f;
        model.UnitScale = unitScale;

        bool isZUp = meshMaxZ > meshMaxCoord * 0.8f && meshMaxCoord < 10f;
        if (isZUp)
        {
            model.AxisCorrection = Matrix4x4.CreateRotationX(-MathF.PI / 2f) * Matrix4x4.CreateRotationY(MathF.PI / 2f);
            Console.WriteLine($"[AssetManager] {model.Name}: Z-Up detected, applying axis correction");
        }
        else
        {
            model.AxisCorrection = Matrix4x4.Identity;
        }

        for (uint i = 0; i < scene->MNumMeshes; i++)
        {
            var mesh = scene->MMeshes[i];
            var transform = meshTransforms.TryGetValue((int)i, out var t) ? t : Matrix4x4.Identity;
            var meshData = ProcessMesh(mesh, transform);
            if (meshData != null)
            {
                model.Meshes.Add(meshData);
            }
        }

        ProcessNodeHierarchy(scene->MRootNode, model, null);

        for (uint i = 0; i < scene->MNumAnimations; i++)
        {
            var anim = scene->MAnimations[i];
            var clip = ProcessAnimation(anim);
            if (clip != null)
            {
                model.Animations.Add(clip);
            }
        }

        foreach (var mesh in model.Meshes)
        {
            CreateGpuMesh(mesh);
        }

        _assimp.ReleaseImport(scene);
        _modelCache[relativePath] = model;

        Console.WriteLine($"[AssetManager] Loaded: {model.Meshes.Count} meshes, {model.Bones.Count} bones, {model.Animations.Count} animations");
        return model;
    }

    /// <summary>
    /// TGAファイルの読み込み - PfimでパースしてOpenGLテクスチャを作成、キャッシュ保存
    /// </summary>
    /// <param name="relativePath"></param>
    /// <returns></returns>
    public uint LoadTexture(string relativePath)
    {
        if (_textureCache.TryGetValue(relativePath, out var cached))
        {
            return cached;
        }

        var fullPath = Path.Combine(AssetBasePath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!System.IO.File.Exists(fullPath))
        {
            Console.WriteLine($"[AssetManager] Texture not found: {fullPath}");
            return 0;
        }

        try
        {
            using var image = Pfimage.FromFile(fullPath);

            var internalFormat = InternalFormat.Rgba;
            var pixelFormat = PixelFormat.Rgba;

            switch (image.Format)
            {
                case ImageFormat.Rgba32:
                    internalFormat = InternalFormat.Rgba;
                    pixelFormat = PixelFormat.Bgra;
                    break;
                case ImageFormat.Rgb24:
                    internalFormat = InternalFormat.Rgb;
                    pixelFormat = PixelFormat.Bgr;
                    break;
                default:
                    internalFormat = InternalFormat.Rgba;
                    pixelFormat = PixelFormat.Bgra;
                    break;
            }

            var textureId = _gl.GenTexture();
            _gl.BindTexture(TextureTarget.Texture2D, textureId);

            unsafe
            {
                fixed (byte* ptr = image.Data)
                {
                    _gl.TexImage2D(TextureTarget.Texture2D, 0, internalFormat,
                        (uint)image.Width, (uint)image.Height, 0,
                        pixelFormat, PixelType.UnsignedByte, ptr);
                }
            }

            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)Silk.NET.OpenGL.TextureWrapMode.Repeat);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)Silk.NET.OpenGL.TextureWrapMode.Repeat);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            _gl.GenerateMipmap(TextureTarget.Texture2D);

            _textureCache[relativePath] = textureId;
            return textureId;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AssetManager] Texture load error: {ex.Message}");
            return 0;
        }
    }

    /// <summary>
    /// AssimpのMesh構造体からMeshDataへの変換 - 頂点データの展開、骨情報の整理、GPUメッシュの準備
    /// </summary>
    /// <param name="mesh"></param>
    /// <param name="nodeTransform"></param>
    /// <returns></returns>
    private unsafe MeshData? ProcessMesh(Silk.NET.Assimp.Mesh* mesh, Matrix4x4 nodeTransform)
    {
        var vertices = new List<float>();
        var indices = new List<uint>();
        var boneNames = new List<string>();
        var boneOffsets = new List<Matrix4x4>();
        var boneWeights = new Dictionary<uint, List<(int boneIndex, float weight)>>();

        var positions = new Vector3[mesh->MNumVertices];
        var normals = new Vector3[mesh->MNumVertices];
        var texCoords = new Vector2[mesh->MNumVertices];

        for (uint i = 0; i < mesh->MNumVertices; i++)
        {
            var pos = mesh->MVertices[i];
            positions[i] = new Vector3(pos.X, pos.Y, pos.Z);
            if (mesh->MNormals != null)
            {
                var n = mesh->MNormals[i];
                normals[i] = new Vector3(n.X, n.Y, n.Z);
            }
            else
            {
                normals[i] = Vector3.UnitY;
            }
            if (mesh->MTextureCoords[0] != null)
            {
                var tc = mesh->MTextureCoords[0][i];
                texCoords[i] = new Vector2(tc.X, tc.Y);
            }
        }

        for (uint i = 0; i < mesh->MNumFaces; i++)
        {
            var face = mesh->MFaces[i];
            for (uint j = 0; j < face.MNumIndices; j++)
            {
                indices.Add(face.MIndices[j]);
            }
        }

        for (uint i = 0; i < mesh->MNumBones; i++)
        {
            var bone = mesh->MBones[i];
            boneNames.Add(bone->MName.AsString);
            boneOffsets.Add(Matrix4x4.Transpose(bone->MOffsetMatrix));
            for (uint j = 0; j < bone->MNumWeights; j++)
            {
                var weight = bone->MWeights[j];
                if (!boneWeights.ContainsKey(weight.MVertexId))
                {
                    boneWeights[weight.MVertexId] = [];
                }
                boneWeights[weight.MVertexId].Add(((int)i, weight.MWeight));
            }
        }

        for (uint i = 0; i < mesh->MNumVertices; i++)
        {
            vertices.Add(positions[i].X);
            vertices.Add(positions[i].Y);
            vertices.Add(positions[i].Z);
            vertices.Add(normals[i].X);
            vertices.Add(normals[i].Y);
            vertices.Add(normals[i].Z);
            vertices.Add(texCoords[i].X);
            vertices.Add(texCoords[i].Y);
        }

        var meshData = new MeshData
        {
            Name = mesh->MName.AsString,
            Vertices = [.. vertices],
            Indices = [.. indices],
            BoneNames = [.. boneNames],
            HasBones = mesh->MNumBones > 0,
            VertexCount = (int)mesh->MNumVertices,
            NodeTransform = nodeTransform,
            OrigPositions = positions,
            OrigNormals = normals,
            OrigTexCoords = texCoords,
            BoneOffsets = [.. boneOffsets],
        };

        var vbw = new VertexBoneWeight[mesh->MNumVertices];
        for (uint vi = 0; vi < mesh->MNumVertices; vi++)
        {
            if (boneWeights.TryGetValue(vi, out var weights))
            {
                var sorted = weights.OrderByDescending(w => w.weight).Take(4).ToList();
                if (sorted.Count > 0)
                {
                    vbw[vi].Bone0 = sorted[0].boneIndex;
                    vbw[vi].Weight0 = sorted[0].weight;
                }
                if (sorted.Count > 1)
                {
                    vbw[vi].Bone1 = sorted[1].boneIndex;
                    vbw[vi].Weight1 = sorted[1].weight;
                }
                if (sorted.Count > 2)
                {
                    vbw[vi].Bone2 = sorted[2].boneIndex;
                    vbw[vi].Weight2 = sorted[2].weight;
                }
                if (sorted.Count > 3)
                {
                    vbw[vi].Bone3 = sorted[3].boneIndex;
                    vbw[vi].Weight3 = sorted[3].weight;
                }
            }
        }
        meshData.BoneWeights = vbw;

        return meshData;
    }

    /// <summary>
    /// AssimpのNode構造体からBoneNodeへの変換 - 階層構造を再帰的に処理してBoneNodeツリーを構築
    /// </summary>
    /// <param name="node"></param>
    /// <param name="model"></param>
    /// <param name="parentName"></param>
    private unsafe void ProcessNodeHierarchy(Node* node, LoadedModel model, string? parentName)
    {
        var name = node->MName.AsString;
        var boneNode = new BoneNode
        {
            Name = name,
            ParentName = parentName,
            LocalTransform = Matrix4x4.Transpose(node->MTransformation),
        };
        model.Bones.Add(boneNode);

        for (uint i = 0; i < node->MNumChildren; i++)
        {
            ProcessNodeHierarchy(node->MChildren[i], model, name);
        }
    }

    /// <summary>
    /// AssimpのNode構造体を再帰的に処理して、各Meshが属するNodeのワールド変換行列を計算してmeshTransformsに保存
    /// </summary>
    /// <param name="node"></param>
    /// <param name="parentTransform"></param>
    /// <param name="meshTransforms"></param>
    private unsafe void CollectMeshTransforms(Node* node, Matrix4x4 parentTransform, Dictionary<int, Matrix4x4> meshTransforms)
    {
        var localTransform = Matrix4x4.Transpose(node->MTransformation);
        var worldTransform = localTransform * parentTransform;

        for (uint i = 0; i < node->MNumMeshes; i++)
        {
            var meshIndex = (int)node->MMeshes[i];
            meshTransforms[meshIndex] = worldTransform;
        }

        for (uint i = 0; i < node->MNumChildren; i++)
        {
            CollectMeshTransforms(node->MChildren[i], worldTransform, meshTransforms);
        }
    }

    /// <summary>
    /// AssimpのAnimation構造体からAnimationClipへの変換 - 各チャネルのキーフレームを展開してAnimationTrackにまとめる
    /// </summary>
    /// <param name="anim"></param>
    /// <returns></returns>
    private unsafe AnimationClip? ProcessAnimation(Animation* anim)
    {
        var clip = new AnimationClip
        {
            Name = anim->MName.AsString,
            Duration = (float)(anim->MDuration / anim->MTicksPerSecond),
            TicksPerSecond = (float)anim->MTicksPerSecond,
        };

        for (uint i = 0; i < anim->MNumChannels; i++)
        {
            var channel = anim->MChannels[i];
            var track = new AnimationTrack
            {
                BoneName = channel->MNodeName.AsString
            };

            for (uint k = 0; k < channel->MNumPositionKeys; k++)
            {
                var key = channel->MPositionKeys[k];
                track.PositionKeys.Add(new VectorKey { Time = (float)(key.MTime / anim->MTicksPerSecond), Value = new Vector3(key.MValue.X, key.MValue.Y, key.MValue.Z) });
            }
            for (uint k = 0; k < channel->MNumRotationKeys; k++)
            {
                var key = channel->MRotationKeys[k];
                track.RotationKeys.Add(new QuaternionKey { Time = (float)(key.MTime / anim->MTicksPerSecond), Value = new Quaternion(key.MValue.X, key.MValue.Y, key.MValue.Z, key.MValue.W) });
            }
            for (uint k = 0; k < channel->MNumScalingKeys; k++)
            {
                var key = channel->MScalingKeys[k];
                track.ScaleKeys.Add(new VectorKey { Time = (float)(key.MTime / anim->MTicksPerSecond), Value = new Vector3(key.MValue.X, key.MValue.Y, key.MValue.Z) });
            }
            clip.Tracks.Add(track);
        }
        return clip;
    }

    /// <summary>
    /// MeshDataの頂点とインデックスをGPUバッファにアップロードして、VAO/VBO/EBOを作成してMeshDataに保存
    /// </summary>
    /// <param name="mesh"></param>
    private void CreateGpuMesh(MeshData mesh)
    {
        mesh.Vao = _gl.GenVertexArray();
        mesh.Vbo = _gl.GenBuffer();
        mesh.Ebo = _gl.GenBuffer();

        _gl.BindVertexArray(mesh.Vao);

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, mesh.Vbo);
        unsafe
        {
            fixed (float* ptr = mesh.Vertices)
            {
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(mesh.Vertices.Length * sizeof(float)), ptr,
                    mesh.HasBones ? BufferUsageARB.DynamicDraw : BufferUsageARB.StaticDraw);
            }
        }

        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, mesh.Ebo);
        unsafe
        {
            fixed (uint* ptr = mesh.Indices)
            {
                _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(mesh.Indices.Length * sizeof(uint)), ptr, BufferUsageARB.StaticDraw);
            }
        }

        const uint stride = 8 * sizeof(float);
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));
        _gl.EnableVertexAttribArray(2);
        _gl.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, stride, 6 * sizeof(float));

        _gl.BindVertexArray(0);
    }

    /// <summary>
    /// キャッシュされたテクスチャとGPUメッシュをすべて削除して、Assimpのリソースも解放する。GC.SuppressFinalizeでファイナライザの呼び出しを抑制。
    /// </summary>
    public void Dispose()
    {
        foreach (var tex in _textureCache.Values)
        {
            _gl.DeleteTexture(tex);
        }
        foreach (var model in _modelCache.Values)
        {
            foreach (var mesh in model.Meshes)
            {
                if (mesh.Vao != 0)
                {
                    _gl.DeleteVertexArray(mesh.Vao);
                }
                if (mesh.Vbo != 0)
                {
                    _gl.DeleteBuffer(mesh.Vbo);
                }
                if (mesh.Ebo != 0)
                {
                    _gl.DeleteBuffer(mesh.Ebo);
                }
            }
        }
        _assimp.Dispose();
        GC.SuppressFinalize(this);
    }
}

// ===== データモデル =====

public class LoadedModel
{
    public string Name { get; set; } = "";
    public float UnitScale { get; set; } = 1.0f;
    public Matrix4x4 AxisCorrection { get; set; } = Matrix4x4.Identity;
    public Matrix4x4 RootTransform { get; set; } = Matrix4x4.Identity;
    public List<MeshData> Meshes { get; set; } = [];
    public List<BoneNode> Bones { get; set; } = [];
    public List<AnimationClip> Animations { get; set; } = [];
}

public class MeshData
{
    public string Name { get; set; } = "";
    public float[] Vertices { get; set; } = [];
    public uint[] Indices { get; set; } = [];
    public string[] BoneNames { get; set; } = [];
    public bool HasBones { get; set; }
    public int VertexCount { get; set; }
    public Matrix4x4 NodeTransform { get; set; } = Matrix4x4.Identity;
    public Vector3[] OrigPositions { get; set; } = [];
    public Vector3[] OrigNormals { get; set; } = [];
    public Vector2[] OrigTexCoords { get; set; } = [];
    public Matrix4x4[] BoneOffsets { get; set; } = [];
    public VertexBoneWeight[] BoneWeights { get; set; } = [];
    public uint Vao { get; set; }
    public uint Vbo { get; set; }
    public uint Ebo { get; set; }
}

public struct VertexBoneWeight
{
    public int Bone0, Bone1, Bone2, Bone3;
    public float Weight0, Weight1, Weight2, Weight3;
}

public class BoneNode
{
    public string Name { get; set; } = "";
    public string? ParentName { get; set; }
    public Matrix4x4 LocalTransform { get; set; } = Matrix4x4.Identity;
    public Matrix4x4 WorldTransform { get; set; } = Matrix4x4.Identity;
    public List<BoneNode> Children { get; set; } = [];
}

public class AnimationClip
{
    public string Name { get; set; } = "";
    public float Duration { get; set; }
    public float TicksPerSecond { get; set; }
    public List<AnimationTrack> Tracks { get; set; } = [];
}

public class AnimationTrack
{
    public string BoneName { get; set; } = "";
    public List<VectorKey> PositionKeys { get; set; } = [];
    public List<QuaternionKey> RotationKeys { get; set; } = [];
    public List<VectorKey> ScaleKeys { get; set; } = [];
}

public struct VectorKey
{
    public float Time;
    public Vector3 Value;
}

public struct QuaternionKey
{
    public float Time;
    public Quaternion Value;
}

public class WeaponInfo
{
    public int Category { get; set; }
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
}
