using System.Numerics;

namespace CharacterViewer;

/// <summary>
/// ボーンビューワー - bone-viewer.htmlの機能をC#に移植
/// ボーンの可視化、選択、検索、パーツ切り替え
/// </summary>
public class BoneViewer(AssetManager assetManager, Renderer renderer)
{
    private readonly AssetManager _assetManager = assetManager;
    private readonly Renderer _renderer = renderer;

    // ボーンデータ
    private readonly List<BoneEntry> _bones = [];
    private int _selectedBoneIndex = -1;
    private readonly int _hoveredBoneIndex = -1;
    private string _searchQuery = "";

    // パーツタブ
    private string _currentPart = "all";

    // アニメーション
    private CharacterModel? _character;
    private bool _playIdle = true;

    // 色設定
    private static readonly Vector4 BoneColor = new(0.27f, 0.67f, 1f, 0.85f);
    private static readonly Vector4 BoneHoverColor = new(1f, 0.8f, 0f, 0.9f);
    private static readonly Vector4 BoneSelectedColor = new(1f, 0.4f, 0.27f, 1f);
    private static readonly Vector4 LineColor = new(0.2f, 0.4f, 0.6f, 0.6f);
    private const float BoneRadius = 0.005f;

    /// <summary>
    /// キャラクターモデルのデータを設定
    /// </summary>
    public void SetCharacterData(CharacterModel character)
    {
        _character = character;
        RebuildBoneList();
    }

    /// <summary>
    /// ボーンリストを再構築
    /// </summary>
    private void RebuildBoneList()
    {
        _bones.Clear();
        if (_character == null) return;

        var allBones = _character.GetAllBones();
        var boneByName = _character.GetBoneByName();

        foreach (var bone in allBones)
        {
            // ボーン名にマッチするノードのみ
            if (!bone.Name.StartsWith("bone", StringComparison.OrdinalIgnoreCase)) continue;

            var entry = new BoneEntry
            {
                Node = bone,
                WorldPosition = ExtractPosition(bone.WorldTransform) * 0.01f,
            };

            // 親を探す
            if (bone.ParentName != null &&
                boneByName.TryGetValue(bone.ParentName.ToLower(), out var parent) &&
                parent.Name.StartsWith("bone", StringComparison.OrdinalIgnoreCase))
            {
                entry.ParentPosition = ExtractPosition(parent.WorldTransform) * 0.01f;
                entry.HasParent = true;
            }

            _bones.Add(entry);
        }

        Console.WriteLine($"[BoneViewer] Rebuilt bone list: {_bones.Count} bones");
    }

    /// <summary>
    /// ボーンを選択
    /// </summary>
    public void SelectBone(int index)
    {
        if (index < 0 || index >= _bones.Count) return;

        _selectedBoneIndex = index;
        var bone = _bones[index];
        Console.WriteLine($"[BoneViewer] Selected: {bone.Node.Name} at ({bone.WorldPosition.X:F4}, {bone.WorldPosition.Y:F4}, {bone.WorldPosition.Z:F4})");
    }

    /// <summary>
    /// レイキャストでボーンを検出
    /// </summary>
    public int RaycastBone(Ray ray)
    {
        float closestDist = float.MaxValue;
        int closestIdx = -1;

        for (int i = 0; i < _bones.Count; i++)
        {
            var bone = _bones[i];
            // 球との交差判定
            var oc = ray.Origin - bone.WorldPosition;
            var a = Vector3.Dot(ray.Direction, ray.Direction);
            var b = 2.0f * Vector3.Dot(oc, ray.Direction);
            var c = Vector3.Dot(oc, oc) - BoneRadius * BoneRadius;
            var discriminant = b * b - 4 * a * c;

            if (discriminant > 0)
            {
                var t = (-b - MathF.Sqrt(discriminant)) / (2 * a);
                if (t > 0 && t < closestDist)
                {
                    closestDist = t;
                    closestIdx = i;
                }
            }
        }

        return closestIdx;
    }

    /// <summary>
    /// 検索クエリを設定
    /// </summary>
    public void SetSearchQuery(string query)
    {
        _searchQuery = query.ToLower();
    }

    /// <summary>
    /// パーツを切り替え
    /// </summary>
    public void SetPart(string partKey)
    {
        _currentPart = partKey;
        Console.WriteLine($"[BoneViewer] Part: {partKey}");
        // TODO: パーツ別の表示フィルタリング
    }

    public void ToggleIdleMotion()
    {
        _playIdle = !_playIdle;
    }

    // ===== 更新 =====

    public void Update(float deltaTime)
    {
        if (_character == null) return;

        // アニメーション再生中はボーン位置を更新
        if (_playIdle)
        {
            _character.Update(deltaTime);
        }

        // ボーン位置を更新
        UpdateBonePositions();
    }

    private void UpdateBonePositions()
    {
        if (_character == null) return;
        var boneByName = _character.GetBoneByName();

        for (int i = 0; i < _bones.Count; i++)
        {
            var entry = _bones[i];
            entry.WorldPosition = ExtractPosition(entry.Node.WorldTransform) * 0.01f;

            if (entry.HasParent && entry.Node.ParentName != null &&
                boneByName.TryGetValue(entry.Node.ParentName.ToLower(), out var parent))
            {
                entry.ParentPosition = ExtractPosition(parent.WorldTransform) * 0.01f;
            }
        }
    }

    // ===== 描画 =====

    public void Render(Renderer renderer, Camera camera)
    {
        if (_character == null) return;

        // メッシュを半透明で描画
        // (CharacterModelのRenderをopacity低で呼ぶことで対応)

        // ボーンの描画
        for (int i = 0; i < _bones.Count; i++)
        {
            var bone = _bones[i];

            // 検索フィルター
            if (!string.IsNullOrEmpty(_searchQuery) &&
                !bone.Node.Name.Contains(_searchQuery, StringComparison.CurrentCultureIgnoreCase))
            {
                continue;
            }

            // 色の決定
            Vector4 color;
            float radius = BoneRadius;

            if (i == _selectedBoneIndex)
            {
                color = BoneSelectedColor;
                radius = BoneRadius * 1.5f;
            }
            else if (i == _hoveredBoneIndex)
            {
                color = BoneHoverColor;
                radius = BoneRadius * 1.3f;
            }
            else if (!string.IsNullOrEmpty(_searchQuery))
            {
                color = BoneColor;
                radius = BoneRadius * 1.8f;
            }
            else
            {
                color = BoneColor;
            }

            // ボーン球を描画
            renderer.DrawBoneSphere(bone.WorldPosition, radius, color, camera);

            // 親への接続線
            if (bone.HasParent)
            {
                renderer.DrawBoneLine(bone.WorldPosition, bone.ParentPosition, LineColor, camera);
            }
        }
    }

    // ===== 情報取得 =====

    public string? GetSelectedBoneName()
    {
        if (_selectedBoneIndex < 0 || _selectedBoneIndex >= _bones.Count)
            return null;
        return _bones[_selectedBoneIndex].Node.Name;
    }

    public string? GetSelectedBoneInfo()
    {
        if (_selectedBoneIndex < 0 || _selectedBoneIndex >= _bones.Count)
            return null;

        var bone = _bones[_selectedBoneIndex];
        return $"Position: ({bone.WorldPosition.X:F4}, {bone.WorldPosition.Y:F4}, {bone.WorldPosition.Z:F4})" +
               (bone.Node.ParentName != null ? $" | Parent: {bone.Node.ParentName}" : "");
    }

    public IReadOnlyList<BoneEntry> GetBones() => _bones;

    // ===== ヘルパー =====

    private static Vector3 ExtractPosition(Matrix4x4 matrix)
    {
        return new Vector3(matrix.M41, matrix.M42, matrix.M43);
    }
}

public class BoneEntry
{
    public BoneNode Node { get; set; } = null!;
    public Vector3 WorldPosition { get; set; }
    public Vector3 ParentPosition { get; set; }
    public bool HasParent { get; set; }
}
