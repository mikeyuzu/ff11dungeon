using ImGuiNET;
using System.Numerics;

namespace CharacterViewer;

/// <summary>
/// ImGuiベースの武器装備UI
/// </summary>
public class WeaponUI(CharacterModel character)
{
    private readonly CharacterModel _character = character;

    private static readonly string[] CharacterNames = [
        "HumeMale",
        "HumeFemale",
        "ElvaanMale",
        "ElvaanFemale",
        "TarutaruMale",
        "TarutaruFemale",
        "Mithra",
        "Galka"
    ];
    private int _characterIndex = 1;

    private static readonly WeaponInfo[] MainWeapons =
    [
        new() { Category = 1, Id = "01001", Label = "格闘 #001" },
        new() { Category = 2, Id = "02001", Label = "短剣 #001" },
        new() { Category = 3, Id = "03001", Label = "片手剣 #001" },
        new() { Category = 4, Id = "04001", Label = "両手剣 #001" },
        new() { Category = 5, Id = "05001", Label = "片手斧 #001" },
        new() { Category = 6, Id = "06001", Label = "両手斧 #001" },
        new() { Category = 7, Id = "07001", Label = "両手鎌 #001" },
        new() { Category = 8, Id = "08001", Label = "両手槍 #001" },
        new() { Category = 9, Id = "09001", Label = "片手刀 #001" },
        new() { Category = 10, Id = "10001", Label = "両手刀 #001" },
        new() { Category = 11, Id = "11001", Label = "片手棍 #001" },
        new() { Category = 12, Id = "12001", Label = "両手棍 #001" },
    ];

    private int _mainWeaponIndex = -1;
    private bool _dualWield;

    private string[] _boneNames = [];
    private int _rightBoneIndex;
    private int _leftBoneIndex;
    private bool _boneListReady;

    private static readonly string[] BaseMotionNames = ["idle", "walk", "run", "ability", "item", "throw", "bmagic", "wmagic", "ninjutsu", "summoner", "heal", "death", "reflesh"];
    private string[] _currentMotionNames = ["idle", "walk", "run", "ability", "item", "throw", "bmagic", "wmagic", "ninjutsu", "summoner", "heal", "death", "reflesh"];
    private int _motionIndex;

    private static readonly string[] RenderModeNames = ["Textured", "Wireframe", "Normal", "Depth", "Bone"];
    private int _renderModeIndex;

    /// <summary>
    /// ボーンリストの初期化 - キャラクターモデルからボーン名を抽出してコンボボックス用の配列を作成
    /// </summary>
    public void InitBoneList()
    {
        var bones = _character.GetAllBones();
        var boneNames = bones
            .Where(b => b.Name.StartsWith("bone", StringComparison.OrdinalIgnoreCase))
            .Select(b => b.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name =>
            {
                var numStr = new string([.. name.Where(char.IsDigit)]);
                return int.TryParse(numStr, out var n) ? n : 0;
            })
            .ToArray();

        _boneNames = boneNames;
        _boneListReady = true;

        _rightBoneIndex = Array.FindIndex(_boneNames, b => b.Equals("bone0090", StringComparison.OrdinalIgnoreCase));
        _leftBoneIndex = Array.FindIndex(_boneNames, b => b.Equals("bone0075", StringComparison.OrdinalIgnoreCase));
        if (_rightBoneIndex < 0)
        {
            _rightBoneIndex = 0;
        }
        if (_leftBoneIndex < 0)
        {
            _leftBoneIndex = 0;
        }
    }

    /// <summary>
    /// UIの描画 - ImGuiを使ってキャラクター選択、モーション選択、レンダリングモード、武器装備のセクションを作成
    /// </summary>
    public void Draw()
    {
        ImGui.SetNextWindowPos(new Vector2(10, 10), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(300, 600), ImGuiCond.FirstUseEver);

        if (ImGui.Begin("Character Controls"))
        {
            DrawCharacterSection();
            ImGui.Separator();
            DrawMotionSection();
            ImGui.Separator();
            DrawRenderSection();
            ImGui.Separator();
            DrawWeaponSection();
        }
        ImGui.End();
    }

    /// <summary>
    /// キャラクター選択セクション - コンボボックスでキャラクターモデルを選択、選択が変わったらモデルをロードしてモーションリストを更新
    /// </summary>
    private void DrawCharacterSection()
    {
        if (ImGui.CollapsingHeader("Character", ImGuiTreeNodeFlags.DefaultOpen))
        {
            if (ImGui.Combo("Model", ref _characterIndex, CharacterNames, CharacterNames.Length))
            {
                _character.LoadCharacter(CharacterNames[_characterIndex]);
                UpdateMotionList(null);
            }
        }
    }

    /// <summary>
    /// モーション選択セクション - コンボボックスでモーションを選択、スライダーで速度調整、再生/一時停止/フレーム送りのボタン、フレーム位置のスライダーと表示
    /// </summary>
    private void DrawMotionSection()
    {
        if (ImGui.CollapsingHeader("Motion", ImGuiTreeNodeFlags.DefaultOpen))
        {
            if (ImGui.Combo("Animation", ref _motionIndex, _currentMotionNames, _currentMotionNames.Length))
            {
                if (_motionIndex >= 0 && _motionIndex < _currentMotionNames.Length)
                {
                    _character.PlayMotion(_currentMotionNames[_motionIndex]);
                }
            }

            float speed = _character.MotionSpeed;
            if (ImGui.SliderFloat("Speed", ref speed, 0.1f, 3.0f, "%.1fx"))
            {
                _character.MotionSpeed = speed;
            }

            ImGui.Spacing();

            bool paused = _character.IsPaused;
            if (ImGui.Button(paused ? "Play" : "Pause"))
            {
                _character.TogglePause();
            }
            ImGui.SameLine();
            if (ImGui.Button("<"))
            {
                _character.StepBackward();
            }
            ImGui.SameLine();
            if (ImGui.Button(">"))
            {
                _character.StepForward();
            }

            int totalFrames = _character.TotalFrames;
            if (totalFrames > 0)
            {
                int frame = _character.CurrentFrame;
                if (ImGui.SliderInt("Frame", ref frame, 0, totalFrames - 1))
                {
                    _character.SeekToFrame(frame);
                }
                ImGui.Text($"{frame} / {totalFrames - 1}  ({_character.AnimTime:F2}s / {_character.AnimDuration:F2}s)");
            }
        }
    }

    /// <summary>
    /// レンダリングモードセクション - コンボボックスでレンダリングモードを選択、選択が変わったらモデルの描画モードを更新
    /// </summary>
    private void DrawRenderSection()
    {
        if (ImGui.CollapsingHeader("Render", ImGuiTreeNodeFlags.DefaultOpen))
        {
            if (ImGui.Combo("Mode", ref _renderModeIndex, RenderModeNames, RenderModeNames.Length))
            {
                _character.SetRenderMode((RenderMode)_renderModeIndex);
            }
        }
    }

    /// <summary>
    /// 武器装備セクション - コンボボックスでメイン武器を選択、チェックボックスで二刀流の切り替え、コンボボックスで右手と左手の装備ボーンを選択（オプション）
    /// </summary>
    private void DrawWeaponSection()
    {
        if (ImGui.CollapsingHeader("Weapon", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Text("Main Hand:");
            var mainPreview = _mainWeaponIndex >= 0 ? MainWeapons[_mainWeaponIndex].Label : "(none)";
            if (ImGui.BeginCombo("##main-weapon", mainPreview))
            {
                if (ImGui.Selectable("(none)", _mainWeaponIndex == -1))
                {
                    _mainWeaponIndex = -1;
                    _character.EquipMainWeapon(null);
                    UpdateMotionList(null);
                }
                for (int i = 0; i < MainWeapons.Length; i++)
                {
                    bool selected = i == _mainWeaponIndex;
                    if (ImGui.Selectable(MainWeapons[i].Label, selected))
                    {
                        _mainWeaponIndex = i;
                        _character.EquipMainWeapon(MainWeapons[i]);
                        UpdateMotionList(MainWeapons[i]);
                    }
                }
                ImGui.EndCombo();
            }

            ImGui.Spacing();
            if (ImGui.Checkbox("Dual Wield (二刀流)", ref _dualWield))
            {
                _character.SetDualWield(_dualWield);
            }

            if (_boneListReady && _boneNames.Length > 0)
            {
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Text("Attach Bones (Override):");

                ImGui.Text("Right:");
                ImGui.SameLine();
                if (ImGui.Combo("##bone-right", ref _rightBoneIndex, _boneNames, _boneNames.Length))
                {
                    _character.SetAttachBone("right", _boneNames[_rightBoneIndex]);
                }

                ImGui.Text("Left:");
                ImGui.SameLine();
                if (ImGui.Combo("##bone-left", ref _leftBoneIndex, _boneNames, _boneNames.Length))
                {
                    _character.SetAttachBone("left", _boneNames[_leftBoneIndex]);
                }
            }
        }
    }

    /// <summary>
    /// モーションリストの更新 - 武器のカテゴリに応じたモーションを追加、武器なしの場合は基本モーションのみ、武器装備時は対応するモーションを追加してリストを更新
    /// </summary>
    /// <param name="weapon"></param>
    private void UpdateMotionList(WeaponInfo? weapon)
    {
        _motionIndex = 0;
        UpdateDefaultBoneSelection(weapon);

        if (weapon == null)
        {
            _currentMotionNames = [.. BaseMotionNames];
            return;
        }

        var folder = CharacterModel.GetWeaponMotionFolder(weapon.Category);
        if (folder == null)
        {
            _currentMotionNames = [.. BaseMotionNames];
            return;
        }

        var motions = new List<string>(BaseMotionNames)
        {
            $"btl_idl_{folder}",
            $"btl_attack0_{folder}",
            $"btl_attack1_{folder}"
        };
        _currentMotionNames = [.. motions];
    }

    /// <summary>
    /// デフォルトの装備ボーンの選択 - 武器のカテゴリに応じたデフォルトの右手と左手の装備ボーンを選択、武器なしの場合は一般的な骨を選択、武器装備時はカテゴリに対応する骨を選択
    /// </summary>
    /// <param name="weapon"></param>
    private void UpdateDefaultBoneSelection(WeaponInfo? weapon)
    {
        if (!_boneListReady || _boneNames.Length == 0)
        {
            return;
        }

        if (weapon == null)
        {
            _rightBoneIndex = FindBoneIndex("bone0090");
            _leftBoneIndex = FindBoneIndex("bone0075");
            return;
        }

        var mainBones = _character.GetMainAttachBones(weapon.Category);
        if (mainBones != null && mainBones.Length > 0)
        {
            _rightBoneIndex = FindBoneIndex(mainBones[0]);
        }

        var subBones = _character.GetSubAttachBones(weapon.Category, _character.DualWield);
        if (subBones != null && subBones.Length > 0)
        {
            _leftBoneIndex = FindBoneIndex(subBones[0]);
        }
    }

    /// <summary>
    /// ボーン名からインデックスを検索 - ボーン名が一致するインデックスを返す、見つからない場合は0を返す
    /// </summary>
    /// <param name="boneName"></param>
    /// <returns></returns>
    private int FindBoneIndex(string boneName)
    {
        var idx = Array.FindIndex(_boneNames, b => b.Equals(boneName, StringComparison.OrdinalIgnoreCase));
        return idx >= 0 ? idx : 0;
    }
}
