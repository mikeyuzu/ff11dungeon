using Silk.NET.OpenGL;
using System;
using System.IO;
using System.Numerics;

namespace CharacterViewer;

public enum CharacterType
{
    HumeMale,
    HumeFemale,
    ElvaanMale,
    ElvaanFemale,
    TarutaruMale,
    TarutaruFemale,
    Mithra,
    Galka
}

public enum PartType
{
    Face,
    Body,
    Hands,
    Legs,
    Feet
}
public enum RenderMode
{
    Textured,
    Wireframe,
    Normal,
    Depth,
    Bone
}

/// <summary>
/// キャラクターモデル - パーツの組み立て、スケルトン共有、武器装備、アニメーション
/// </summary>
public class CharacterModel
{
    private readonly AssetManager _assetManager;
    private readonly CharacterData _characterData = new();

    private string _currentCharacter = "HumeFemale";

    private readonly Dictionary<PartType, LoadedModel?> _parts = [];
    private readonly Dictionary<PartType, uint> _textures = [];
    private readonly Dictionary<PartType, bool> _partVisible = [];
    private readonly Dictionary<PartType, int> _partVariantIndex = [];

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

    private readonly string _basePlayerPath = "Player";

    /// <summary>
    /// コンストラクタ - アセットマネージャを受け取って初期化する。アセットマネージャはモデルやテクスチャの読み込みに使用される。キャラクターデータの読み込みは行わないので、LoadAsync() を呼び出す必要がある
    /// </summary>
    /// <param name="assetManager"></param>
    public CharacterModel(AssetManager assetManager)
    {
        _assetManager = assetManager;
        _characterData = CharacterDataLoader.Load(assetManager.AssetBasePath);
        foreach (PartType part in Enum.GetValues<PartType>())
        {
            _parts[part] = null;
            _textures[part] = 0;
            _partVisible[part] = true;
            _partVariantIndex[part] = 0;
        }
    }

    /// <summary>
    /// キャラクターデータの読み込み - モデル、テクスチャ、アニメーションを一括で読み込む
    /// </summary>
    /// <returns></returns>
    public async Task LoadAsync()
    {
        LoadBoneInfo();

        var charData = GetCurrentCharacterData();
        if (charData != null)
        {
            // パーツのモデルとテクスチャを読み込む（バリアントインデックスに従う）
            foreach (PartType part in Enum.GetValues<PartType>())
            {
                var partName = part.ToString();
                if (!charData.Parts.TryGetValue(partName, out var partData) || partData.Variants.Length == 0)
                {
                    continue;
                }

                var variantIndex = _partVariantIndex.GetValueOrDefault(part, 0);
                var variant = partData.Variants[Math.Clamp(variantIndex, 0, partData.Variants.Length - 1)];

                var modelPath = partData.ModelPattern.Replace("{variant}", variant);
                var texturePath = partData.TexturePattern.Replace("{variant}", variant);

                var fullModelPath = $"{_basePlayerPath}/{_currentCharacter}/{modelPath}";
                var fullTexturePath = $"{_basePlayerPath}/{_currentCharacter}/{texturePath}";

                _textures[part] = _assetManager.LoadTexture(fullTexturePath);
                var model = _assetManager.LoadFbx(fullModelPath);
                _parts[part] = model;
                if (model != null && part == PartType.Face)
                {
                    BuildSkeleton(model);
                }
            }

            // モーションを読み込む
            foreach (var (motionName, paths) in charData.Motions)
            {
                var upperClip = _assetManager.LoadFbx($"{_basePlayerPath}/{_currentCharacter}/{paths.Upper}")?.Animations.FirstOrDefault();
                var lowerClip = _assetManager.LoadFbx($"{_basePlayerPath}/{_currentCharacter}/{paths.Lower}")?.Animations.FirstOrDefault();
                var waistClip = _assetManager.LoadFbx($"{_basePlayerPath}/{_currentCharacter}/{paths.Waist}")?.Animations.FirstOrDefault();
                _animations[motionName] = MergeAnimationClips(upperClip, lowerClip, waistClip, motionName);
            }
        }

        foreach (var (part, model) in _parts)
        {
            if (part == PartType.Face || model == null)
            {
                continue;
            }
            MergeBonesIntoSkeleton(model);
        }

        LoadBattleMotions();
        UpdateWorldTransforms();
        PlayMotion("idle");
        await Task.CompletedTask;
    }

    /// <summary>
    /// bone_info.csv を読み込んで、上半身・下半身・腰のボーンを分類する
    /// </summary>
    private void LoadBoneInfo()
    {
        var csvPath = Path.Combine(_assetManager.AssetBasePath, "Player", "Data", "bone_info.csv");
        if (!File.Exists(csvPath))
        {
            Console.WriteLine($"[CharacterModel] bone_info.csv not found");
            return;
        }
        _upperBones.Clear();
        _lowerBones.Clear();
        _waistBones.Clear();
        var lines = File.ReadAllLines(csvPath);
        bool inTarget = false;
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed == ",,,")
            {
                continue;
            }
            var parts = trimmed.Split(',');
            if (parts.Length < 4)
            {
                continue;
            }
            var first = parts[0].Trim();
            if (!first.StartsWith("bone", StringComparison.OrdinalIgnoreCase))
            {
                inTarget = first.Equals( _currentCharacter, StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!inTarget)
            {
                continue;
            }
            var boneName = first.ToLower();
            if (parts[1].Trim().Equals("TRUE", StringComparison.OrdinalIgnoreCase))
            {
                _upperBones.Add(boneName);
            }
            if (parts[2].Trim().Equals("TRUE", StringComparison.OrdinalIgnoreCase))
            {
                _lowerBones.Add(boneName);
            }
            if (parts[3].Trim().Equals("TRUE", StringComparison.OrdinalIgnoreCase))
            {
                _waistBones.Add(boneName);
            }
        }
    }

    /// <summary>
    /// 最初に読み込んだモデルのボーン構造をスケルトンとして採用し、以降のモデルはスケルトンにないボーンのみ追加していく
    /// </summary>
    /// <param name="model"></param>
    private void BuildSkeleton(LoadedModel model)
    {
        _skeleton.Clear();
        _boneByName.Clear();
        foreach (var bone in model.Bones)
        {
            _skeleton.Add(bone);
            _boneByName[bone.Name.ToLower()] = bone;
        }
    }

    /// <summary>
    /// スケルトンにないボーンを以降のモデルから追加していく。これにより、全パーツで共通のスケルトンを構築する
    /// </summary>
    /// <param name="model"></param>
    private void MergeBonesIntoSkeleton(LoadedModel model)
    {
        foreach (var bone in model.Bones)
        {
            var key = bone.Name.ToLower();
            if (!_boneByName.ContainsKey(key))
            {
                _skeleton.Add(bone);
                _boneByName[key] = bone;
            }
        }
    }

    /// <summary>
    /// バトルモーションを武器種ごとに読み込み
    /// </summary>
    private void LoadBattleMotions()
    {
        foreach (var (categoryStr, folder) in _characterData.WeaponMotionFolder)
        {
            if (!int.TryParse(categoryStr, out var category))
            {
                continue;
            }
            var basePath = $"Player/{_currentCharacter}/Motion/Battle/{folder}";

            // 戦闘待機(btl)モーション
            LoadBattleClip($"btl_idl_{folder}", basePath, "btl");

            // 攻撃(at0)モーション
            LoadBattleClip($"btl_attack0_{folder}", basePath, "at0");

            // 攻撃(at1)モーション
            LoadBattleClip($"btl_attack1_{folder}", basePath, "at1");

            if (_characterData.WeaponDualWieldFlag.TryGetValue(categoryStr, out var dual) && dual)
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

    /// <summary>
    /// 上半身・下半身・腰のクリップをマージして、武器種ごとの戦闘モーションを作成する
    /// </summary>
    /// <param name="clipName"></param>
    /// <param name="basePath"></param>
    /// <param name="prefix"></param>
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

    /// <summary>
    /// 上半身・下半身・腰のクリップをマージして1つのクリップを作成する。各トラックのボーン名を元に、bone_info.csvで分類した上半身・下半身・腰のボーンに対応するトラックのみを抽出してマージする
    /// </summary>
    /// <param name="upper"></param>
    /// <param name="lower"></param>
    /// <param name="waist"></param>
    /// <param name="name"></param>
    /// <returns></returns>
    private AnimationClip MergeAnimationClips(AnimationClip? upper, AnimationClip? lower, AnimationClip? waist, string name)
    {
        var merged = new AnimationClip
        {
            Name = name
        };
        float maxDuration = 0f;
        void Process(AnimationClip? clip, HashSet<string> allowed)
        {
            if (clip == null)
            {
                return;
            }
            foreach (var track in clip.Tracks)
            {
                var bn = track.BoneName.ToLower();
                if (bn.Contains(':'))
                {
                    bn = bn.Split(':').Last();
                }
                if (bn.Contains('|'))
                {
                    bn = bn.Split('|').Last();
                }
                if (allowed.Contains(bn))
                {
                    merged.Tracks.Add(track);
                    if (clip.Duration > maxDuration)
                    {
                        maxDuration = clip.Duration;
                    }
                }
            }
        }
        Process(upper, _upperBones);
        Process(lower, _lowerBones);
        Process(waist, _waistBones);
        merged.Duration = maxDuration;
        return merged;
    }

    // ===== Public API =====
    public string CurrentCharacter => _currentCharacter;
    public string ActiveMotion => _activeMotion;
    public bool IsPaused => _paused;
    public int CurrentFrame => _currentFrame;
    public int TotalFrames => _totalFrames;
    public float AnimTime => _animTime;
    public float AnimDuration => _activeClip?.Duration ?? 0f;
    public float MotionSpeed { get => _motionSpeed; set => _motionSpeed = value; }
    public bool DualWield => _dualWield;
    public CharacterData Data => _characterData;

    /// <summary>
    /// 現在のキャラクタータイプのデータをJSONから取得する
    /// </summary>
    private CharacterTypeData? GetCurrentCharacterData()
    {
        return _characterData.Characters.GetValueOrDefault(_currentCharacter);
    }

    /// <summary>
    /// 指定パーツのバリアント一覧を取得する
    /// </summary>
    public string[] GetPartVariants(PartType part)
    {
        var charData = GetCurrentCharacterData();
        if (charData == null) return [];
        return charData.Parts.GetValueOrDefault(part.ToString())?.Variants ?? [];
    }

    /// <summary>
    /// 指定パーツの現在のバリアントインデックスを取得する
    /// </summary>
    public int GetPartVariantIndex(PartType part) => _partVariantIndex.GetValueOrDefault(part, 0);

    /// <summary>
    /// 指定パーツのバリアントを切り替える。インデックスが変わった場合はモデルとテクスチャを再読み込みする
    /// </summary>
    public void SetPartVariant(PartType part, int variantIndex)
    {
        var charData = GetCurrentCharacterData();
        if (charData == null) return;
        var partName = part.ToString();
        if (!charData.Parts.TryGetValue(partName, out var partData) || partData.Variants.Length == 0) return;

        variantIndex = Math.Clamp(variantIndex, 0, partData.Variants.Length - 1);
        if (_partVariantIndex.GetValueOrDefault(part, 0) == variantIndex) return;

        _partVariantIndex[part] = variantIndex;
        var variant = partData.Variants[variantIndex];

        var modelPath = partData.ModelPattern.Replace("{variant}", variant);
        var texturePath = partData.TexturePattern.Replace("{variant}", variant);

        var fullModelPath = $"{_basePlayerPath}/{_currentCharacter}/{modelPath}";
        var fullTexturePath = $"{_basePlayerPath}/{_currentCharacter}/{texturePath}";

        _textures[part] = _assetManager.LoadTexture(fullTexturePath);
        var model = _assetManager.LoadFbx(fullModelPath);
        _parts[part] = model;

        if (model != null)
        {
            MergeBonesIntoSkeleton(model);
        }

        Console.WriteLine($"[CharacterModel] Part variant changed: {part} -> {variant}");
    }

    /// <summary>
    /// WeaponCategoriesをJSONから取得する（UIなどで使用）
    /// </summary>
    public Dictionary<int, string> GetWeaponCategories()
    {
        var result = new Dictionary<int, string>();
        foreach (var (key, value) in _characterData.WeaponCategories)
        {
            if (int.TryParse(key, out var k))
            {
                result[k] = value;
            }
        }
        return result;
    }

    /// <summary>
    /// キャラクターデータの切り替え - モデル、テクスチャ、アニメーションをすべて切り替える。現在はロード済みのデータを切り替えるだけで、再読み込みは行わない
    /// </summary>
    /// <param name="name"></param>
    public void LoadCharacter(string name)
    {
        _currentCharacter = name;
        using var _ = LoadAsync();
        Console.WriteLine($"[CharacterModel] LoadCharacter: {name}");
    }

    /// <summary>
    /// モーションの切り替え - 指定したモーションキーに対応するアニメーションクリップをアクティブにして再生する。現在はロード済みのクリップを切り替えるだけで、再読み込みは行わない
    /// </summary>
    /// <param name="motionKey"></param>
    public void PlayMotion(string motionKey)
    {
        _activeMotion = motionKey;
        _paused = false;

        var fullKey = motionKey;
        if (motionKey.StartsWith("btl") && _dualWield)
        {
            fullKey = $"{motionKey}_dual";
        }

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

    /// <summary>
    /// フレーム単位でシークする。フレーム数は現在のアクティブクリップの長さから計算される。シーク後は一時停止状態になる
    /// </summary>
    /// <param name="frame"></param>
    public void SeekToFrame(int frame)
    {
        if (_activeClip == null || _totalFrames <= 0)
        {
            return;
        }
        frame = Math.Clamp(frame, 0, _totalFrames - 1);
        _currentFrame = frame;
        _animTime = (frame / (float)(_totalFrames - 1)) * (_activeClip?.Duration ?? 0f);
        _paused = true;
        ApplyAnimation(_activeClip!, _animTime);
        ApplySkinning();
    }

    /// <summary>
    /// 現在のフレームから1フレーム進める。フレーム数は現在のアクティブクリップの長さから計算される。シーク後は一時停止状態になる
    /// </summary>
    public void StepForward()
    {
        if (_activeClip != null)
        {
            SeekToFrame((_currentFrame + 1) % _totalFrames);
        }
    }

    /// <summary>
    /// 現在のフレームから1フレーム戻す。フレーム数は現在のアクティブクリップの長さから計算される。シーク後は一時停止状態になる
    /// </summary>
    public void StepBackward()
    {
        if (_activeClip != null)
        {
            SeekToFrame(_currentFrame - 1 < 0 ? _totalFrames - 1 : _currentFrame - 1);
        }
    }

    /// <summary>
    /// 描画モードの切り替え - テクスチャあり、ワイヤーフレーム、法線表示、深度表示、ボーン表示を切り替える。ボーン表示はスケルトンを半透明で表示し、武器や装備は非表示にする
    /// </summary>
    /// <param name="mode"></param>
    public void SetRenderMode(RenderMode mode)
    {
        _renderMode = mode;
        _showBones = mode == RenderMode.Bone;
    }

    /// <summary>
    /// パーツの表示切り替え - 指定したパーツの表示・非表示を切り替える。非表示にしたパーツは描画されなくなるが、アニメーションやスケルトンには影響しない
    /// </summary>
    /// <param name="part"></param>
    /// <param name="visible"></param>
    public void SetPartVisible(PartType part, bool visible) => _partVisible[part] = visible;

    /// <summary>
    /// 武器の装備 - 指定した武器を装備する。武器のカテゴリに応じて、対応するモデルとテクスチャを読み込んで装備する。二刀流可能な武器の場合は、サブにも同じ武器を装備する。二刀流不可能な武器の場合は、サブは空になる。装備する武器によって、戦闘モーションも切り替わる
    /// </summary>
    /// <param name="weapon"></param>
    public void EquipMainWeapon(WeaponInfo? weapon)
    {
        _mainWeapon = weapon;
        _mainWeaponModel = null;
        _mainWeaponTexture = 0;
        _attachBoneRightOverride = null;
        _attachBoneLeftOverride = null;
        if (weapon == null)
        {
            _subWeapon = null;
            _subWeaponModel = null;
            _subWeaponTexture = 0;
            return;
        }
        var path = $"Player/{_currentCharacter}/Main/{weapon.Id}/{weapon.Id}.fbx";
        _mainWeaponModel = _assetManager.LoadFbx(path);
        _mainWeaponTexture = LoadWeaponTexture("Main", weapon.Id);
        if (weapon.Category == 1)
        {
            _subWeapon = weapon;
            _subWeaponModel = _assetManager.LoadFbx($"Player/{_currentCharacter}/Sub/{weapon.Id}/{weapon.Id}.fbx");
            _subWeaponTexture = LoadWeaponTexture("Sub", weapon.Id);
        }
        else if (_dualWield && !IsTwoHanded(weapon.Category))
        {
            _subWeapon = weapon;
            _subWeaponModel = _assetManager.LoadFbx($"Player/{_currentCharacter}/Sub/{weapon.Id}/{weapon.Id}.fbx");
            _subWeaponTexture = LoadWeaponTexture("Sub", weapon.Id);
        }
        else
        {
            _subWeapon = null;
            _subWeaponModel = null;
            _subWeaponTexture = 0;
        }
    }

    /// <summary>
    /// 武器の装備 - 指定した武器をサブに装備する。主に二刀流用。武器のカテゴリに応じて、対応するモデルとテクスチャを読み込んで装備する。装備する武器によって、戦闘モーションも切り替わる
    /// </summary>
    /// <param name="hand"></param>
    /// <param name="boneName"></param>
    public void SetAttachBone(string hand, string boneName)
    {
        if (hand == "right")
        {
            _attachBoneRightOverride = boneName;
        }
        else
        {
            _attachBoneLeftOverride = boneName;
        }
    }

    /// <summary>
    /// 二刀流の切り替え - 二刀流のオンオフを切り替える。二刀流をオンにすると、二刀流可能な武器はサブにも同じ武器を装備する。二刀流をオフにすると、サブの武器は外れる。二刀流の状態によって、戦闘モーションも切り替わる
    /// </summary>
    /// <param name="enabled"></param>
    public void SetDualWield(bool enabled)
    {
        _dualWield = enabled;
        if (_mainWeapon != null)
        {
            EquipMainWeapon(_mainWeapon);
        }
    }

    /// <summary>
    /// 武器カテゴリが両手武器かどうかを判定する。両手武器の場合は二刀流できないので、サブに装備できる武器はない。二刀流可能な武器の場合は、サブに同じ武器を装備する
    /// </summary>
    /// <param name="cat"></param>
    /// <returns></returns>
    public bool IsTwoHanded(int cat)
    {
        var categories = _characterData.WeaponCategories;
        return categories.TryGetValue(cat.ToString(), out var n) && n.StartsWith("両手");
    }

    /// <summary>
    /// 武器カテゴリに対応する戦闘モーションのフォルダ名を取得する。フォルダ名がない場合はnullを返す
    /// </summary>
    /// <param name="cat"></param>
    /// <returns></returns>
    public string? GetWeaponMotionFolder(int cat) => _characterData.WeaponMotionFolder.TryGetValue(cat.ToString(), out var f) ? f : null;

    /// <summary>
    /// 武器カテゴリに対応するメイン装備のボーン名を取得する。カテゴリがない場合はnullを返す
    /// </summary>
    /// <param name="cat"></param>
    /// <returns></returns>
    public string[]? GetMainAttachBones(int cat)
    {
        var charData = GetCurrentCharacterData();
        return charData?.MainAttachBones.GetValueOrDefault(cat.ToString());
    }
    /// <summary>
    /// 武器カテゴリに対応するサブ装備のボーン名を取得する。二刀流可能な武器の場合は、通常と二刀流で異なるボーン名を返す。カテゴリがない場合はnullを返す
    /// </summary>
    /// <param name="cat"></param>
    /// <param name="dw"></param>
    /// <returns></returns>
    public string[]? GetSubAttachBones(int cat, bool dw)
    {
        var charData = GetCurrentCharacterData();
        if (charData == null) return null;
        if (!charData.SubAttachBones.TryGetValue(cat.ToString(), out var e)) return null;
        var bone = dw && e.DualWield != null ? e.DualWield : e.Normal;
        return [bone];
    }

    /// <summary>
    /// 武器IDに対応するテクスチャを読み込む。テクスチャがない場合は0を返す
    /// </summary>
    /// <param name="slot"></param>
    /// <param name="weaponId"></param>
    /// <returns></returns>
    private uint LoadWeaponTexture(string slot, string weaponId)
    {
        if (!_characterData.WeaponTextures.TryGetValue(weaponId, out var texName)) return 0;
        return _assetManager.LoadTexture($"Player/{_currentCharacter}/{slot}/{weaponId}/{texName}");
    }

    /// <summary>
    /// キャラクターのバウンディングボックスを取得する。モデルの原点を中心に、XZ平面に-0.3から0.3、Y方向に0から1.7の範囲を想定している。必要に応じて、装備やモーションに合わせて調整すること
    /// </summary>
    /// <returns></returns>
    public static BoundingBox GetBounds()
    {
        var bounds = BoundingBox.Empty;
        bounds.Expand(new Vector3(-0.3f, 0f, -0.3f));
        bounds.Expand(new Vector3(0.3f, 1.7f, 0.3f));
        return bounds;
    }

    /// <summary>
    /// キャラクターのスケルトンを取得する。スケルトンは最初に読み込んだモデルのボーン構造をベースに、以降のモデルからスケルトンにないボーンを追加して構築される。スケルトンは全パーツで共通で、アニメーションや描画に使用される
    /// </summary>
    /// <returns></returns>
    public List<BoneNode> GetAllBones() => _skeleton;

    /// <summary>
    /// キャラクターのボーンを名前で検索して取得する。大文字小文字を区別せず、コロンやパイプで区切られた場合は最後の部分を使用する。見つからない場合はnullを返す
    /// </summary>
    /// <returns></returns>
    public Dictionary<string, BoneNode> GetBoneByName() => _boneByName;

    /// <summary>
    /// アクティブなアニメーションクリップをフレーム単位で更新する。アニメーションが再生中の場合は、経過時間に応じて現在のフレームを計算して更新する。アニメーションがループする場合は、経過時間がクリップの長さを超えたら余りを使用してループさせる。更新後は、現在のフレームに対応するボーンの変換をスケルトンに適用する
    /// </summary>
    /// <param name="deltaTime"></param>
    public void Update(float deltaTime)
    {
        if (_activeClip != null && _activeClip.Duration > 0)
        {
            if (!_paused)
            {
                _animTime += deltaTime * _motionSpeed;
                if (_animTime > _activeClip.Duration)
                {
                    _animTime %= _activeClip.Duration;
                    _currentFrame = (int)MathF.Round((_animTime / _activeClip.Duration) * (_totalFrames - 1));
                }
            }
            ApplyAnimation(_activeClip, _animTime);
        }
        ApplySkinning();
    }

    /// <summary>
    /// キャラクターを描画する。描画モードやパーツの表示状態に応じて、モデルのメッシュをテクスチャ付きやワイヤーフレームで描画する。武器は装備している場合のみ描画する。ボーン表示モードの場合は、スケルトンを半透明で描画し、武器や装備は非表示にする
    /// </summary>
    /// <param name="renderer"></param>
    /// <param name="camera"></param>
    public void Render(Renderer renderer, Camera camera)
    {
        bool wireframe = _renderMode == RenderMode.Wireframe;
        float opacity = _renderMode == RenderMode.Bone ? 0.25f : 1.0f;

        foreach (var (part, model) in _parts)
        {
            if (model == null || !_partVisible[part])
            {
                continue;
            }
            var textureId = _textures[part];
            var partMatrix = Matrix4x4.CreateScale(model.UnitScale);
            foreach (var mesh in model.Meshes)
            {
                if (mesh.Vao == 0)
                {
                    continue;
                }
                renderer.DrawMesh(mesh.Vao, mesh.Indices.Length, textureId, partMatrix, camera, opacity, wireframe);
            }
        }

        DrawWeapon(renderer, camera, _mainWeaponModel, _mainWeapon, _mainWeaponTexture, "right", opacity, wireframe);
        DrawWeapon(renderer, camera, _subWeaponModel, _subWeapon, _subWeaponTexture, "left", opacity, wireframe);

        if (_showBones)
        {
            RenderBones(renderer, camera);
        }
    }

    /// <summary>
    /// 武器を描画する。装備している武器のモデルとテクスチャを使用して、指定した手のボーンに武器を装着して描画する。装着するボーンは、武器のカテゴリに応じて、あらかじめ定義されたボーン名から選択される。ボーン名のオーバーライドが設定されている場合は、そちらが優先される。装備している武器がない場合は描画しない
    /// </summary>
    /// <param name="renderer"></param>
    /// <param name="camera"></param>
    /// <param name="weaponModel"></param>
    /// <param name="weapon"></param>
    /// <param name="textureId"></param>
    /// <param name="hand"></param>
    /// <param name="opacity"></param>
    /// <param name="wireframe"></param>
    private void DrawWeapon(Renderer renderer, Camera camera, LoadedModel? weaponModel, WeaponInfo? weapon, uint textureId, string hand, float opacity, bool wireframe)
    {
        if (weaponModel == null || weapon == null)
        {
            return;
        }
        var charData = GetCurrentCharacterData();
        string? boneName = null;
        if (hand == "right")
        {
            boneName = _attachBoneRightOverride ?? charData?.MainAttachBones.GetValueOrDefault(weapon.Category.ToString())?.FirstOrDefault();
        }
        else
        {
            if (_attachBoneLeftOverride != null)
            {
                boneName = _attachBoneLeftOverride;
            }
            else if (charData?.SubAttachBones.TryGetValue(weapon.Category.ToString(), out var e) == true)
            {
                boneName = _dualWield && e.DualWield != null ? e.DualWield : e.Normal;
            }
        }

        Matrix4x4 attachMatrix = Matrix4x4.CreateScale(0.01f);
        if (boneName != null && _boneByName.TryGetValue(boneName.ToLower(), out var boneNode))
        {
            attachMatrix = boneNode.WorldTransform * Matrix4x4.CreateScale(0.01f);
        }

        var weaponRotation = Matrix4x4.CreateRotationX(MathF.PI / 2f);
        foreach (var mesh in weaponModel.Meshes)
        {
            if (mesh.Vao == 0)
            {
                continue;
            }
            renderer.DrawMesh(mesh.Vao, mesh.Indices.Length, textureId, mesh.NodeTransform * weaponRotation * attachMatrix, camera, opacity, wireframe);
        }
    }

    /// <summary>
    /// スケルトンを描画する。スケルトンの各ボーンを半透明の球で描画し、親子関係を線で結ぶ。ボーンの名前が "bone" で始まるもののみ描画する。描画モードがボーン表示の場合は、武器や装備は非表示にして、スケルトンのみを強調して表示する
    /// </summary>
    /// <param name="renderer"></param>
    /// <param name="camera"></param>
    private void RenderBones(Renderer renderer, Camera camera)
    {
        var boneColor = new Vector4(0.27f, 0.67f, 1f, 0.85f);
        var lineColor = new Vector4(0.2f, 0.4f, 0.6f, 0.6f);
        foreach (var bone in _skeleton)
        {
            if (!bone.Name.StartsWith("bone", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var pos = new Vector3(bone.WorldTransform.M41, bone.WorldTransform.M42, bone.WorldTransform.M43) * 0.01f;
            renderer.DrawBoneSphere(pos, 0.005f, boneColor, camera);
            if (bone.ParentName != null && _boneByName.TryGetValue(bone.ParentName.ToLower(), out var parent))
            {
                var pp = new Vector3(parent.WorldTransform.M41, parent.WorldTransform.M42, parent.WorldTransform.M43) * 0.01f;
                renderer.DrawBoneLine(pos, pp, lineColor, camera);
            }
        }
    }

    /// <summary>
    /// アクティブなアニメーションクリップの現在のフレームに対応するボーンの変換をスケルトンに適用する。各トラックのボーン名を元に、スケルトンの対応するボーンを検索して、トラックの位置・回転・スケールを線形補間して適用する。見つからないボーンやトラックがない場合は、スケルトンの既存の変換を維持する
    /// </summary>
    /// <param name="clip"></param>
    /// <param name="time"></param>
    private void ApplyAnimation(AnimationClip clip, float time)
    {
        foreach (var track in clip.Tracks)
        {
            var boneName = track.BoneName.ToLower();
            if (boneName.Contains(':'))
            {
                boneName = boneName.Split(':').Last();
            }
            if (boneName.Contains('|'))
            {
                boneName = boneName.Split('|').Last();
            }
            if (!_boneByName.TryGetValue(boneName, out var bone))
            {
                continue;
            }

            var pos = Interpolate(track.PositionKeys, time, Vector3.Zero);
            var rot = InterpolateRot(track.RotationKeys, time);
            var scl = Interpolate(track.ScaleKeys, time, Vector3.One);

            bone.LocalTransform = Matrix4x4.CreateScale(scl) * Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(pos);
        }
        UpdateWorldTransforms();
    }

    /// <summary>
    /// スケルトンの各ボーンのローカル変換を元に、ワールド変換を更新する。ワールド変換は、親ボーンのワールド変換とローカル変換を掛け合わせて計算される。親ボーンがいない場合は、ローカル変換がそのままワールド変換になる。スケルトンの全ボーンに対してこれを行うことで、アニメーションの結果がスケルトン全体に反映される
    /// </summary>
    private void UpdateWorldTransforms()
    {
        foreach (var bone in _skeleton)
        {
            if (bone.ParentName == null)
            {
                bone.WorldTransform = bone.LocalTransform;
            }
            else if (_boneByName.TryGetValue(bone.ParentName.ToLower(), out var parent))
            {
                bone.WorldTransform = bone.LocalTransform * parent.WorldTransform;
            }
        }
    }

    /// <summary>
    /// スケルトンのボーンのワールド変換を元に、モデルのメッシュの頂点をスキニングして変換する。各メッシュのボーンウェイトとボーンオフセットを使用して、頂点の位置と法線を変換する。変換後の頂点データをVBOにアップロードして、描画に反映させる。スキニングはCPUで行うため、頂点数が多い場合はパフォーマンスに注意すること
    /// </summary>
    private void ApplySkinning()
    {
        var gl = _assetManager.GL;
        if (gl == null)
        {
            return;
        }
        foreach (var (_, model) in _parts)
        {
            if (model == null)
            {
                continue;
            }
            foreach (var mesh in model.Meshes)
            {
                if (!mesh.HasBones || mesh.BoneWeights.Length == 0 || mesh.Vbo == 0)
                {
                    continue;
                }
                var boneMatrices = new Matrix4x4[mesh.BoneNames.Length];
                for (int bi = 0; bi < mesh.BoneNames.Length; bi++)
                {
                    if (_boneByName.TryGetValue(mesh.BoneNames[bi].ToLower(), out var bn))
                    {
                        boneMatrices[bi] = mesh.BoneOffsets[bi] * bn.WorldTransform;
                    }
                    else
                    {
                        boneMatrices[bi] = Matrix4x4.Identity;
                    }
                }

                var vertexData = new float[mesh.VertexCount * 8];
                for (int vi = 0; vi < mesh.VertexCount; vi++)
                {
                    var bw = mesh.BoneWeights[vi];
                    var op = mesh.OrigPositions[vi];
                    var on = mesh.OrigNormals[vi];
                    Vector3 sp = Vector3.Zero, sn = Vector3.Zero;
                    if (bw.Weight0 > 0)
                    {
                        sp += Vector3.Transform(op, boneMatrices[bw.Bone0]) * bw.Weight0;
                        sn += Vector3.TransformNormal(on, boneMatrices[bw.Bone0]) * bw.Weight0;
                    }
                    if (bw.Weight1 > 0)
                    {
                        sp += Vector3.Transform(op, boneMatrices[bw.Bone1]) * bw.Weight1;
                        sn += Vector3.TransformNormal(on, boneMatrices[bw.Bone1]) * bw.Weight1;
                    }
                    if (bw.Weight2 > 0)
                    {
                        sp += Vector3.Transform(op, boneMatrices[bw.Bone2]) * bw.Weight2;
                        sn += Vector3.TransformNormal(on, boneMatrices[bw.Bone2]) * bw.Weight2;
                    }
                    if (bw.Weight3 > 0)
                    {
                        sp += Vector3.Transform(op, boneMatrices[bw.Bone3]) * bw.Weight3;
                        sn += Vector3.TransformNormal(on, boneMatrices[bw.Bone3]) * bw.Weight3;
                    }
                    float tw = bw.Weight0 + bw.Weight1 + bw.Weight2 + bw.Weight3;
                    if (tw < 0.001f)
                    {
                        sp = op;
                        sn = on;
                    }
                    else
                    {
                        sn = Vector3.Normalize(sn);
                    }
                    int off = vi * 8;
                    vertexData[off] = sp.X;
                    vertexData[off+1] = sp.Y;
                    vertexData[off+2] = sp.Z;
                    vertexData[off+3] = sn.X;
                    vertexData[off+4] = sn.Y;
                    vertexData[off+5] = sn.Z;
                    vertexData[off+6] = mesh.OrigTexCoords[vi].X;
                    vertexData[off+7] = mesh.OrigTexCoords[vi].Y;
                }
                gl.BindBuffer(BufferTargetARB.ArrayBuffer, mesh.Vbo);
                unsafe
                {
                    fixed (float* ptr = vertexData)
                    {
                        gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, (nuint)(vertexData.Length * sizeof(float)), ptr);
                    }
                }
            }
        }
    }

    /// <summary>
    /// アニメーショントラックの位置・回転・スケールのキーを線形補間して、指定した時間の位置・回転・スケールを計算する。キーがない場合はデフォルト値を使用する。キーが1つだけの場合はその値を使用する。キーが複数ある場合は、時間に応じて前後のキーを見つけて線形補間する。時間が最後のキーを超える場合は最後のキーの値を使用する
    /// </summary>
    /// <param name="keys"></param>
    /// <param name="time"></param>
    /// <param name="def"></param>
    /// <returns></returns>
    private static Vector3 Interpolate(List<VectorKey> keys, float time, Vector3 def)
    {
        if (keys.Count == 0)
        {
            return def;
        }
        if (keys.Count == 1)
        {
            return keys[0].Value;
        }
        for (int i = 0; i < keys.Count - 1; i++)
        {
            if (time < keys[i + 1].Time)
            {
                var t = (time - keys[i].Time) / (keys[i + 1].Time - keys[i].Time);
                return Vector3.Lerp(keys[i].Value, keys[i + 1].Value, t);
            }
        }
        return keys[^1].Value;
    }

    /// <summary>
    /// アニメーショントラックの回転のキーを球面線形補間して、指定した時間の回転を計算する。キーがない場合はデフォルト値を使用する。キーが1つだけの場合はその値を使用する。キーが複数ある場合は、時間に応じて前後のキーを見つけて球面線形補間する。時間が最後のキーを超える場合は最後のキーの値を使用する
    /// </summary>
    /// <param name="keys"></param>
    /// <param name="time"></param>
    /// <returns></returns>
    private static Quaternion InterpolateRot(List<QuaternionKey> keys, float time)
    {
        if (keys.Count == 0)
        {
            return Quaternion.Identity;
        }
        if (keys.Count == 1)
        {
            return keys[0].Value;
        }
        for (int i = 0; i < keys.Count - 1; i++)
        {
            if (time < keys[i + 1].Time)
            {
                var t = (time - keys[i].Time) / (keys[i + 1].Time - keys[i].Time);
                return Quaternion.Slerp(keys[i].Value, keys[i + 1].Value, t);
            }
        }
        return keys[^1].Value;
    }
}
