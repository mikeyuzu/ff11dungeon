using System.Text.Json;
using System.Text.Json.Serialization;

namespace CharacterViewer;

/// <summary>
/// character_data.json のルート構造
/// </summary>
public class CharacterData
{
    [JsonPropertyName("characters")]
    public Dictionary<string, CharacterTypeData> Characters { get; set; } = [];

    [JsonPropertyName("weaponMotionFolder")]
    public Dictionary<string, string> WeaponMotionFolder { get; set; } = [];

    [JsonPropertyName("weaponDualWieldFlag")]
    public Dictionary<string, bool> WeaponDualWieldFlag { get; set; } = [];

    [JsonPropertyName("weaponCategories")]
    public Dictionary<string, string> WeaponCategories { get; set; } = [];

    [JsonPropertyName("weaponTextures")]
    public Dictionary<string, string> WeaponTextures { get; set; } = [];
}

/// <summary>
/// キャラクタータイプごとのデータ（パーツ、モーション、装備ボーン）
/// </summary>
public class CharacterTypeData
{
    [JsonPropertyName("parts")]
    public Dictionary<string, PartData> Parts { get; set; } = [];

    [JsonPropertyName("motions")]
    public Dictionary<string, MotionPathData> Motions { get; set; } = [];

    [JsonPropertyName("mainAttachBones")]
    public Dictionary<string, string[]> MainAttachBones { get; set; } = [];

    [JsonPropertyName("subAttachBones")]
    public Dictionary<string, SubAttachBoneData> SubAttachBones { get; set; } = [];
}

/// <summary>
/// パーツデータ - バリアントリスト（フォルダ名一覧）とモデル/テクスチャのパスパターン
/// </summary>
public class PartData
{
    [JsonPropertyName("variants")]
    public string[] Variants { get; set; } = [];

    [JsonPropertyName("modelPattern")]
    public string ModelPattern { get; set; } = "";

    [JsonPropertyName("texturePattern")]
    public string TexturePattern { get; set; } = "";
}

/// <summary>
/// モーションパス - 上半身・下半身・腰の3パートのFBXパス
/// </summary>
public class MotionPathData
{
    [JsonPropertyName("upper")]
    public string Upper { get; set; } = "";

    [JsonPropertyName("lower")]
    public string Lower { get; set; } = "";

    [JsonPropertyName("waist")]
    public string Waist { get; set; } = "";
}

/// <summary>
/// サブ装備のボーン - 通常時と二刀流時のボーン名
/// </summary>
public class SubAttachBoneData
{
    [JsonPropertyName("normal")]
    public string Normal { get; set; } = "";

    [JsonPropertyName("dualWield")]
    public string? DualWield { get; set; }
}

/// <summary>
/// character_data.json の読み込みユーティリティ
/// </summary>
public static class CharacterDataLoader
{
    private static CharacterData? _cached;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>
    /// JSONファイルを読み込んでパースする。一度読み込んだらキャッシュする
    /// </summary>
    public static CharacterData Load(string assetBasePath)
    {
        if (_cached != null)
        {
            return _cached;
        }

        var jsonPath = Path.Combine(assetBasePath, "Player", "Data", "character_data.json");
        if (!File.Exists(jsonPath))
        {
            Console.WriteLine($"[CharacterDataLoader] character_data.json not found: {jsonPath}");
            return new CharacterData();
        }

        var json = File.ReadAllText(jsonPath);

        _cached = JsonSerializer.Deserialize<CharacterData>(json, _jsonOptions) ?? new CharacterData();
        Console.WriteLine($"[CharacterDataLoader] Loaded character_data.json ({_cached.Characters.Count} characters)");
        return _cached;
    }

    /// <summary>
    /// キャッシュをクリアして再読み込みを強制する
    /// </summary>
    public static void ClearCache()
    {
        _cached = null;
    }
}
