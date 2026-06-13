using System.Numerics;
using Silk.NET.OpenGL;

namespace CharacterViewer;

public enum PartType { Face, Body, Hands, Legs, Feet }
public enum RenderMode { Textured, Wireframe, Normal, Depth, Bone }

/// <summary>
/// キャラクターモデル - パーツの組み立て、スケルトン共有、武器装備、アニメーション
/// </summary>
public class CharacterModel
{
    private readonly AssetManager _assetManager;

    private readonly Dictionary<PartType, LoadedModel?> _parts = [];
    private readonly Dictionary<PartType, uint> _textures = [];
    private readonly Dictionary<PartType, bool> _partVisible = [];

    private readonly List<BoneNode> _skeleton = [];
    private readonly Dictionary<string, BoneNode> _boneByName = [];

    private readonly Dictionary<string, AnimationClip> _animations = [];
    private AnimationClip? _activeClip;
    private float _animTime;
    private float _motionSpeed = 1.0f;
    private string _activeMotion = "idle";
    private bool _paused;
    private int _currentFrame;
    private int _totalFrames;

    private readonly HashSet<string> _upperBones = [];
    private readonly HashSet<string> _lowerBones = [];
    private readonly HashSet<string> _waistBones = [];

    private WeaponInfo? _mainWeapon;
    private WeaponInfo? _subWeapon;
    private LoadedModel? _mainWeaponModel;
    private LoadedModel? _subWeaponModel;
    private uint _mainWeaponTexture;
    private uint _subWeaponTexture;
    private bool _dualWield;
    private string? _attachBoneRightOverride;
    private string? _attachBoneLeftOverride;

    private RenderMode _renderMode = RenderMode.Textured;
    private bool _showBones;

    private static readonly Dictionary<PartType, string> PartPaths = new()
    {
        [PartType.Face] = "Player/HumeFemale/Face/0000/0000.fbx",
        [PartType.Body] = "Player/HumeFemale/Body/00000/00000.fbx",
        [PartType.Hands] = "Player/HumeFemale/Hands/00000/00000.fbx",
        [PartType.Legs] = "Player/HumeFemale/Legs/00000/00000.fbx",
        [PartType.Feet] = "Player/HumeFemale/Feet/00000/00000.fbx",
    };

    private static readonly Dictionary<PartType, string> TexturePaths = new()
    {
        [PartType.Face] = "Player/HumeFemale/Face/0000/tim     hf_h41_1.tga",
        [PartType.Body] = "Player/HumeFemale/Body/00000/tim     hf_ba1_1.tga",
        [PartType.Hands] = "Player/HumeFemale/Hands/00000/tim     hf_ga1_1.tga",
        [PartType.Legs] = "Player/HumeFemale/Legs/00000/tim     hf_la1_1.tga",
        [PartType.Feet] = "Player/HumeFemale/Feet/00000/tim     hf_fa1_1.tga",
    };

    private static readonly Dictionary<string, (string upper, string lower, string waist)> MotionPaths = new()
    {
        ["idle"] = ("Player/HumeFemale/Motion/Base/idl0.fbx", "Player/HumeFemale/Motion/Base/idl1.fbx", "Player/HumeFemale/Motion/Base/idl2.fbx"),
        ["walk"] = ("Player/HumeFemale/Motion/Base/wlk0.fbx", "Player/HumeFemale/Motion/Base/wlk1.fbx", "Player/HumeFemale/Motion/Base/wlk2.fbx"),
        ["run"] = ("Player/HumeFemale/Motion/Base/run0.fbx", "Player/HumeFemale/Motion/Base/run1.fbx", "Player/HumeFemale/Motion/Base/run2.fbx"),
        ["ability"] = ("Player/HumeFemale/Motion/Base/cm00.fbx", "Player/HumeFemale/Motion/Base/cm01.fbx", "Player/HumeFemale/Motion/Base/cm02.fbx"),
        ["item"] = ("Player/HumeFemale/Motion/Base/mi20.fbx", "Player/HumeFemale/Motion/Base/mi21.fbx", "Player/HumeFemale/Motion/Base/mi22.fbx"),
        ["throw"] = ("Player/HumeFemale/Motion/Base/na30.fbx", "Player/HumeFemale/Motion/Base/na31.fbx", "Player/HumeFemale/Motion/Base/na32.fbx"),
        ["bmagic"] = ("Player/HumeFemale/Motion/Base/mb10.fbx", "Player/HumeFemale/Motion/Base/mb11.fbx", "Player/HumeFemale/Motion/Base/mb12.fbx"),
        ["wmagic"] = ("Player/HumeFemale/Motion/Base/mw10.fbx", "Player/HumeFemale/Motion/Base/mw11.fbx", "Player/HumeFemale/Motion/Base/mw12.fbx"),
        ["ninjutsu"] = ("Player/HumeFemale/Motion/Base/mn10.fbx", "Player/HumeFemale/Motion/Base/mn11.fbx", "Player/HumeFemale/Motion/Base/mn12.fbx"),
        ["summoner"] = ("Player/HumeFemale/Motion/Base/ms10.fbx", "Player/HumeFemale/Motion/Base/ms11.fbx", "Player/HumeFemale/Motion/Base/ms12.fbx"),
        ["heal"] = ("Player/HumeFemale/Motion/Base/rx10.fbx", "Player/HumeFemale/Motion/Base/rx11.fbx", "Player/HumeFemale/Motion/Base/rx12.fbx"),
        ["death"] = ("Player/HumeFemale/Motion/Base/ded0.fbx", "Player/HumeFemale/Motion/Base/ded1.fbx", "Player/HumeFemale/Motion/Base/ded2.fbx"),
        ["reflesh"] = ("Player/HumeFemale/Motion/Base/std0.fbx", "Player/HumeFemale/Motion/Base/std1.fbx", "Player/HumeFemale/Motion/Base/std2.fbx"),
    };

    private static readonly Dictionary<int, string> WeaponMotionFolder = new()
    {
        [0] = "none", [1] = "hand", [2] = "dagger", [3] = "sword",
        [4] = "gsword", [5] = "axe", [6] = "gaxe", [7] = "scythe",
        [8] = "polearm", [9] = "katana", [10] = "gkatana", [11] = "club", [12] = "gclub",
    };

    private static readonly Dictionary<int, bool> WeaponDualWieldFlag = new()
    {
        [0] = false, [1] = false, [2] = true, [3] = true,
        [4] = false, [5] = true, [6] = false, [7] = false,
        [8] = false, [9] = true, [10] = false, [11] = true, [12] = false,
    };

    private static readonly Dictionary<int, string[]> MainAttachBones = new()
    {
        [0] = ["bone0090"], [1] = ["bone0093"], [2] = ["bone0086"], [3] = ["bone0090"],
        [4] = ["bone0071"], [5] = ["bone0095"], [6] = ["bone0092"], [7] = ["bone0092"],
        [8] = ["bone0092"], [9] = ["bone0083"], [10] = ["bone0090"], [11] = ["bone0095"], [12] = ["bone0077"],
    };

    private static readonly Dictionary<int, (string normal, string? dualWield)> SubAttachBones = new()
    {
        [0] = ("bone0090", null), [1] = ("bone0094", null),
        [2] = ("bone0086", "bone0075"), [3] = ("bone0090", "bone0075"),
        [5] = ("bone0095", "bone0096"), [9] = ("bone0083", "bone0084"),
        [11] = ("bone0095", "bone0096"),
    };

    public static readonly Dictionary<int, string> WeaponCategories = new()
    {
        [0] = "盾", [1] = "格闘", [2] = "短剣", [3] = "片手剣",
        [4] = "両手剣", [5] = "片手斧", [6] = "両手斧", [7] = "両手鎌",
        [8] = "両手槍", [9] = "片手刀", [10] = "両手刀", [11] = "片手棍", [12] = "両手棍",
    };

    private static readonly Dictionary<string, string> WeaponTextures = new()
    {
        ["01001"] = "tim     hf_clw1_.tga", ["01022"] = "tim     hf_clw7_.tga",
        ["02001"] = "tim     hf_knf1_.tga", ["02029"] = "tim     hf_knf9_.tga",
        ["03001"] = "tim     hf_rap2r.tga", ["03020"] = "tim     hf_swo3_.tga",
        ["04001"] = "tim     hf_2hs1_.tga", ["04010"] = "tim     hf_2hs5_.tga",
        ["05001"] = "tim     hf_axe1_.tga", ["05006"] = "tim     hf_axe2_.tga",
        ["06001"] = "tim     hf_b_ax1.tga", ["06005"] = "tim     hf_b_ax2.tga",
        ["07001"] = "tim     hf_scy1_.tga", ["08001"] = "tim     hf_spea1.tga",
        ["09001"] = "tim     hf_shi1_.tga", ["10001"] = "tim     hf_kata1.tga",
        ["11001"] = "tim     stksp01 .tga", ["12001"] = "tim     hf_wand2.tga",
    };

    public CharacterModel(AssetManager assetManager)
    {
        _assetManager = assetManager;
        foreach (PartType part in Enum.GetValues<PartType>())
        {
            _parts[part] = null; _textures[part] = 0; _partVisible[part] = true;
        }
    }

    public async Task LoadAsync()
    {
        LoadBoneInfo();
        foreach (var (part, path) in TexturePaths) _textures[part] = _assetManager.LoadTexture(path);
        foreach (var (part, path) in PartPaths)
        {
            var model = _assetManager.LoadFbx(path);
            _parts[part] = model;
            if (model != null && part == PartType.Face) BuildSkeleton(model);
        }
        foreach (var (part, model) in _parts)
        {
            if (part == PartType.Face || model == null) continue;
            MergeBonesIntoSkeleton(model);
        }
        foreach (var (motionName, paths) in MotionPaths)
        {
            var upperClip = _assetManager.LoadFbx(paths.upper)?.Animations.FirstOrDefault();
            var lowerClip = _assetManager.LoadFbx(paths.lower)?.Animations.FirstOrDefault();
            var waistClip = _assetManager.LoadFbx(paths.waist)?.Animations.FirstOrDefault();
            _animations[motionName] = MergeAnimationClips(upperClip, lowerClip, waistClip, motionName);
        }
        LoadBattleMotions();
        UpdateWorldTransforms();
        PlayMotion("idle");
        await Task.CompletedTask;
    }

    private void LoadBoneInfo()
    {
        var csvPath = Path.Combine(_assetManager.AssetBasePath, "Player", "Data", "bone_info.csv");
        if (!File.Exists(csvPath)) { Console.WriteLine($"[CharacterModel] bone_info.csv not found"); return; }
        var lines = File.ReadAllLines(csvPath);
        bool inTarget = false;
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed == ",,,") continue;
            var parts = trimmed.Split(',');
            if (parts.Length < 4) continue;
            var first = parts[0].Trim();
            if (!first.StartsWith("bone", StringComparison.OrdinalIgnoreCase))
            { inTarget = first.Equals("HumeFemale", StringComparison.OrdinalIgnoreCase); continue; }
            if (!inTarget) continue;
            var boneName = first.ToLower();
            if (parts[1].Trim().Equals("TRUE", StringComparison.OrdinalIgnoreCase)) _upperBones.Add(boneName);
            if (parts[2].Trim().Equals("TRUE", StringComparison.OrdinalIgnoreCase)) _lowerBones.Add(boneName);
            if (parts[3].Trim().Equals("TRUE", StringComparison.OrdinalIgnoreCase)) _waistBones.Add(boneName);
        }
    }

    private void BuildSkeleton(LoadedModel model)
    {
        _skeleton.Clear(); _boneByName.Clear();
        foreach (var bone in model.Bones) { _skeleton.Add(bone); _boneByName[bone.Name.ToLower()] = bone; }
    }

    private void MergeBonesIntoSkeleton(LoadedModel model)
    {
        foreach (var bone in model.Bones)
        {
            var key = bone.Name.ToLower();
            if (!_boneByName.ContainsKey(key)) { _skeleton.Add(bone); _boneByName[key] = bone; }
        }
    }

    /// <summary>
    /// バトルモーション（構え/攻撃/収め等）を武器種ごとに読み込み
    /// </summary>
    private void LoadBattleMotions()
    {
        foreach (var (category, folder) in WeaponMotionFolder)
        {
            var basePath = $"Player/HumeFemale/Motion/Battle/{folder}";

            // 戦闘待機(btl)モーション
            LoadBattleClip($"btl_idl_{folder}", basePath, "btl");

            // 攻撃(at0)モーション
            LoadBattleClip($"btl_attack0_{folder}", basePath, "at0");

            // 攻撃(at1)モーション
            LoadBattleClip($"btl_attack1_{folder}", basePath, "at1");

            if (WeaponDualWieldFlag.TryGetValue(category, out var dual) && dual)
            {
                // 二刀流用戦闘待機モーション
                LoadBattleClip($"btl_idl_{folder}_dual", $"{basePath}r", "btl");

                // 二刀流用攻撃モーション
                LoadBattleClip($"btl_attack0_{folder}_dual", $"{basePath}r", "ar0");

                // 二刀流用攻撃モーション（左手）
                LoadBattleClip($"btl_attack1_{folder}_dual", $"{basePath}l", "al0");
            }
        }

        Console.WriteLine($"[CharacterModel] Battle motions loaded: {_animations.Count(a => a.Key.Contains('_'))} clips");
    }

    private void LoadBattleClip(string clipName, string basePath, string prefix)
    {
        // base/内: 末尾0=upper, 末尾1=lower
        var upperPath = $"{basePath}/base/{prefix}0.fbx";
        var lowerPath = $"{basePath}/base/{prefix}1.fbx";
        // waist0/内: 末尾2=waist
        var waistPath = $"{basePath}/waist0/{prefix}2.fbx";

        var upperClip = _assetManager.LoadFbx(upperPath)?.Animations.FirstOrDefault();
        var lowerClip = _assetManager.LoadFbx(lowerPath)?.Animations.FirstOrDefault();
        var waistClip = _assetManager.LoadFbx(waistPath)?.Animations.FirstOrDefault();

        if (upperClip != null || lowerClip != null || waistClip != null)
        {
            var merged = MergeAnimationClips(upperClip, lowerClip, waistClip, clipName);
            _animations[clipName] = merged;
        }
    }

    private AnimationClip MergeAnimationClips(AnimationClip? upper, AnimationClip? lower, AnimationClip? waist, string name)
    {
        var merged = new AnimationClip { Name = name };
        float maxDuration = 0f;
        void Process(AnimationClip? clip, HashSet<string> allowed)
        {
            if (clip == null) return;
            foreach (var track in clip.Tracks)
            {
                var bn = track.BoneName.ToLower();
                if (bn.Contains(':')) bn = bn.Split(':').Last();
                if (bn.Contains('|')) bn = bn.Split('|').Last();
                if (allowed.Contains(bn)) { merged.Tracks.Add(track); if (clip.Duration > maxDuration) maxDuration = clip.Duration; }
            }
        }
        Process(upper, _upperBones); Process(lower, _lowerBones); Process(waist, _waistBones);
        merged.Duration = maxDuration;
        return merged;
    }

    // ===== Public API =====
    public string ActiveMotion => _activeMotion;
    public bool IsPaused => _paused;
    public int CurrentFrame => _currentFrame;
    public int TotalFrames => _totalFrames;
    public float AnimTime => _animTime;
    public float AnimDuration => _activeClip?.Duration ?? 0f;
    public float MotionSpeed { get => _motionSpeed; set => _motionSpeed = value; }
    public bool DualWield => _dualWield;

    public void LoadCharacter(string name) => Console.WriteLine($"[CharacterModel] LoadCharacter: {name}");

    public void PlayMotion(string motionKey)
    {
        _activeMotion = motionKey; _paused = false;

        var fullKey = motionKey;
        if (motionKey.StartsWith("btl") && _dualWield)
            fullKey = $"{motionKey}_dual";

        if (_animations.TryGetValue(fullKey, out var clip))
        {
            _activeClip = clip;
            _animTime = 0;
            _currentFrame = 0;
            _totalFrames = Math.Max(1, (int)MathF.Ceiling(clip.Duration * 30f));
        }
    }

    public void TogglePause() => _paused = !_paused;
    public void SetPaused(bool p) => _paused = p;

    public void SeekToFrame(int frame)
    {
        if (_activeClip == null || _totalFrames <= 0) return;
        frame = Math.Clamp(frame, 0, _totalFrames - 1);
        _currentFrame = frame; _animTime = (frame / (float)(_totalFrames - 1)) * (_activeClip?.Duration ?? 0f);
        _paused = true; ApplyAnimation(_activeClip!, _animTime); ApplySkinning();
    }

    public void StepForward() { if (_activeClip != null) SeekToFrame((_currentFrame + 1) % _totalFrames); }
    public void StepBackward() { if (_activeClip != null) SeekToFrame(_currentFrame - 1 < 0 ? _totalFrames - 1 : _currentFrame - 1); }

    public void SetRenderMode(RenderMode mode) { _renderMode = mode; _showBones = mode == RenderMode.Bone; }
    public void SetPartVisible(PartType part, bool visible) => _partVisible[part] = visible;

    public void EquipMainWeapon(WeaponInfo? weapon)
    {
        _mainWeapon = weapon; _mainWeaponModel = null; _mainWeaponTexture = 0;
        _attachBoneRightOverride = null; _attachBoneLeftOverride = null;
        if (weapon == null) { _subWeapon = null; _subWeaponModel = null; _subWeaponTexture = 0; return; }
        var path = $"Player/HumeFemale/Main/{weapon.Id}/{weapon.Id}.fbx";
        _mainWeaponModel = _assetManager.LoadFbx(path);
        _mainWeaponTexture = LoadWeaponTexture("Main", weapon.Id);
        if (weapon.Category == 1) { _subWeapon = weapon; _subWeaponModel = _assetManager.LoadFbx($"Player/HumeFemale/Sub/{weapon.Id}/{weapon.Id}.fbx"); _subWeaponTexture = LoadWeaponTexture("Sub", weapon.Id); }
        else if (_dualWield && !IsTwoHanded(weapon.Category)) { _subWeapon = weapon; _subWeaponModel = _assetManager.LoadFbx($"Player/HumeFemale/Sub/{weapon.Id}/{weapon.Id}.fbx"); _subWeaponTexture = LoadWeaponTexture("Sub", weapon.Id); }
        else { _subWeapon = null; _subWeaponModel = null; _subWeaponTexture = 0; }
    }

    public void SetAttachBone(string hand, string boneName)
    {
        if (hand == "right") _attachBoneRightOverride = boneName; else _attachBoneLeftOverride = boneName;
    }

    public void SetDualWield(bool enabled) { _dualWield = enabled; if (_mainWeapon != null) EquipMainWeapon(_mainWeapon); }

    public static bool IsTwoHanded(int cat) => WeaponCategories.TryGetValue(cat, out var n) && n.StartsWith("両手");
    public static string? GetWeaponMotionFolder(int cat) => WeaponMotionFolder.TryGetValue(cat, out var f) ? f : null;
    public static string[]? GetMainAttachBones(int cat) => MainAttachBones.GetValueOrDefault(cat);
    public static string[]? GetSubAttachBones(int cat, bool dw)
    {
        if (!SubAttachBones.TryGetValue(cat, out var e)) return null;
        var bone = dw && e.dualWield != null ? e.dualWield : e.normal;
        return [bone];
    }

    private uint LoadWeaponTexture(string slot, string weaponId)
    {
        if (!WeaponTextures.TryGetValue(weaponId, out var texName)) return 0;
        return _assetManager.LoadTexture($"Player/HumeFemale/{slot}/{weaponId}/{texName}");
    }

    public static BoundingBox GetBounds()
    {
        var bounds = BoundingBox.Empty;
        bounds.Expand(new Vector3(-0.3f, 0f, -0.3f));
        bounds.Expand(new Vector3(0.3f, 1.7f, 0.3f));
        return bounds;
    }

    public List<BoneNode> GetAllBones() => _skeleton;
    public Dictionary<string, BoneNode> GetBoneByName() => _boneByName;

    // ===== Update & Render =====
    public void Update(float deltaTime)
    {
        if (_activeClip != null && _activeClip.Duration > 0)
        {
            if (!_paused) { _animTime += deltaTime * _motionSpeed; if (_animTime > _activeClip.Duration) _animTime %= _activeClip.Duration; _currentFrame = (int)MathF.Round((_animTime / _activeClip.Duration) * (_totalFrames - 1)); }
            ApplyAnimation(_activeClip, _animTime);
        }
        ApplySkinning();
    }

    public void Render(Renderer renderer, Camera camera)
    {
        bool wireframe = _renderMode == RenderMode.Wireframe;
        float opacity = _renderMode == RenderMode.Bone ? 0.25f : 1.0f;

        foreach (var (part, model) in _parts)
        {
            if (model == null || !_partVisible[part]) continue;
            var textureId = _textures[part];
            var partMatrix = Matrix4x4.CreateScale(model.UnitScale);
            foreach (var mesh in model.Meshes)
            {
                if (mesh.Vao == 0) continue;
                renderer.DrawMesh(mesh.Vao, mesh.Indices.Length, textureId, partMatrix, camera, opacity, wireframe);
            }
        }

        DrawWeapon(renderer, camera, _mainWeaponModel, _mainWeapon, _mainWeaponTexture, "right", opacity, wireframe);
        DrawWeapon(renderer, camera, _subWeaponModel, _subWeapon, _subWeaponTexture, "left", opacity, wireframe);

        if (_showBones) RenderBones(renderer, camera);
    }

    private void DrawWeapon(Renderer renderer, Camera camera, LoadedModel? weaponModel, WeaponInfo? weapon, uint textureId, string hand, float opacity, bool wireframe)
    {
        if (weaponModel == null || weapon == null) return;
        string? boneName = hand == "right" ? _attachBoneRightOverride ?? MainAttachBones.GetValueOrDefault(weapon.Category)?.FirstOrDefault()
            : _attachBoneLeftOverride ?? (SubAttachBones.TryGetValue(weapon.Category, out var e) ? (_dualWield && e.dualWield != null ? e.dualWield : e.normal) : null);

        Matrix4x4 attachMatrix = Matrix4x4.CreateScale(0.01f);
        if (boneName != null && _boneByName.TryGetValue(boneName.ToLower(), out var boneNode))
            attachMatrix = boneNode.WorldTransform * Matrix4x4.CreateScale(0.01f);

        var weaponRotation = Matrix4x4.CreateRotationX(MathF.PI / 2f);
        foreach (var mesh in weaponModel.Meshes)
        {
            if (mesh.Vao == 0) continue;
            renderer.DrawMesh(mesh.Vao, mesh.Indices.Length, textureId, mesh.NodeTransform * weaponRotation * attachMatrix, camera, opacity, wireframe);
        }
    }

    private void RenderBones(Renderer renderer, Camera camera)
    {
        var boneColor = new Vector4(0.27f, 0.67f, 1f, 0.85f);
        var lineColor = new Vector4(0.2f, 0.4f, 0.6f, 0.6f);
        foreach (var bone in _skeleton)
        {
            if (!bone.Name.StartsWith("bone", StringComparison.OrdinalIgnoreCase)) continue;
            var pos = new Vector3(bone.WorldTransform.M41, bone.WorldTransform.M42, bone.WorldTransform.M43) * 0.01f;
            renderer.DrawBoneSphere(pos, 0.005f, boneColor, camera);
            if (bone.ParentName != null && _boneByName.TryGetValue(bone.ParentName.ToLower(), out var parent))
            {
                var pp = new Vector3(parent.WorldTransform.M41, parent.WorldTransform.M42, parent.WorldTransform.M43) * 0.01f;
                renderer.DrawBoneLine(pos, pp, lineColor, camera);
            }
        }
    }

    private void ApplyAnimation(AnimationClip clip, float time)
    {
        foreach (var track in clip.Tracks)
        {
            var boneName = track.BoneName.ToLower();
            if (boneName.Contains(':')) boneName = boneName.Split(':').Last();
            if (boneName.Contains('|')) boneName = boneName.Split('|').Last();
            if (!_boneByName.TryGetValue(boneName, out var bone)) continue;

            var pos = Interpolate(track.PositionKeys, time, Vector3.Zero);
            var rot = InterpolateRot(track.RotationKeys, time);
            var scl = Interpolate(track.ScaleKeys, time, Vector3.One);

            bone.LocalTransform = Matrix4x4.CreateScale(scl) * Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(pos);
        }
        UpdateWorldTransforms();
    }

    private void UpdateWorldTransforms()
    {
        foreach (var bone in _skeleton)
        {
            if (bone.ParentName == null) bone.WorldTransform = bone.LocalTransform;
            else if (_boneByName.TryGetValue(bone.ParentName.ToLower(), out var parent))
                bone.WorldTransform = bone.LocalTransform * parent.WorldTransform;
        }
    }

    private void ApplySkinning()
    {
        var gl = _assetManager.GL;
        if (gl == null) return;
        foreach (var (_, model) in _parts)
        {
            if (model == null) continue;
            foreach (var mesh in model.Meshes)
            {
                if (!mesh.HasBones || mesh.BoneWeights.Length == 0 || mesh.Vbo == 0) continue;
                var boneMatrices = new Matrix4x4[mesh.BoneNames.Length];
                for (int bi = 0; bi < mesh.BoneNames.Length; bi++)
                {
                    if (_boneByName.TryGetValue(mesh.BoneNames[bi].ToLower(), out var bn))
                        boneMatrices[bi] = mesh.BoneOffsets[bi] * bn.WorldTransform;
                    else boneMatrices[bi] = Matrix4x4.Identity;
                }

                var vertexData = new float[mesh.VertexCount * 8];
                for (int vi = 0; vi < mesh.VertexCount; vi++)
                {
                    var bw = mesh.BoneWeights[vi];
                    var op = mesh.OrigPositions[vi]; var on = mesh.OrigNormals[vi];
                    Vector3 sp = Vector3.Zero, sn = Vector3.Zero;
                    if (bw.Weight0 > 0) { sp += Vector3.Transform(op, boneMatrices[bw.Bone0]) * bw.Weight0; sn += Vector3.TransformNormal(on, boneMatrices[bw.Bone0]) * bw.Weight0; }
                    if (bw.Weight1 > 0) { sp += Vector3.Transform(op, boneMatrices[bw.Bone1]) * bw.Weight1; sn += Vector3.TransformNormal(on, boneMatrices[bw.Bone1]) * bw.Weight1; }
                    if (bw.Weight2 > 0) { sp += Vector3.Transform(op, boneMatrices[bw.Bone2]) * bw.Weight2; sn += Vector3.TransformNormal(on, boneMatrices[bw.Bone2]) * bw.Weight2; }
                    if (bw.Weight3 > 0) { sp += Vector3.Transform(op, boneMatrices[bw.Bone3]) * bw.Weight3; sn += Vector3.TransformNormal(on, boneMatrices[bw.Bone3]) * bw.Weight3; }
                    float tw = bw.Weight0 + bw.Weight1 + bw.Weight2 + bw.Weight3;
                    if (tw < 0.001f) { sp = op; sn = on; } else sn = Vector3.Normalize(sn);
                    int off = vi * 8;
                    vertexData[off] = sp.X; vertexData[off+1] = sp.Y; vertexData[off+2] = sp.Z;
                    vertexData[off+3] = sn.X; vertexData[off+4] = sn.Y; vertexData[off+5] = sn.Z;
                    vertexData[off+6] = mesh.OrigTexCoords[vi].X; vertexData[off+7] = mesh.OrigTexCoords[vi].Y;
                }
                gl.BindBuffer(BufferTargetARB.ArrayBuffer, mesh.Vbo);
                unsafe { fixed (float* ptr = vertexData) { gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, (nuint)(vertexData.Length * sizeof(float)), ptr); } }
            }
        }
    }

    private static Vector3 Interpolate(List<VectorKey> keys, float time, Vector3 def)
    {
        if (keys.Count == 0) return def;
        if (keys.Count == 1) return keys[0].Value;
        for (int i = 0; i < keys.Count - 1; i++)
            if (time < keys[i + 1].Time) { var t = (time - keys[i].Time) / (keys[i + 1].Time - keys[i].Time); return Vector3.Lerp(keys[i].Value, keys[i + 1].Value, t); }
        return keys[^1].Value;
    }

    private static Quaternion InterpolateRot(List<QuaternionKey> keys, float time)
    {
        if (keys.Count == 0) return Quaternion.Identity;
        if (keys.Count == 1) return keys[0].Value;
        for (int i = 0; i < keys.Count - 1; i++)
            if (time < keys[i + 1].Time) { var t = (time - keys[i].Time) / (keys[i + 1].Time - keys[i].Time); return Quaternion.Slerp(keys[i].Value, keys[i + 1].Value, t); }
        return keys[^1].Value;
    }
}
