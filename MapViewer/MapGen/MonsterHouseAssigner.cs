namespace MapViewer.MapGen;

public sealed class MonsterHouseAssigner
{
    /// <summary>
    /// MonsterHouseEnabled=true の場合、非隠しRoomに対してモンスターハウス判定を行い、
    /// 1～3部屋にMonsterHouseフラグと密度乗数を設定する。
    /// </summary>
    public static void AssignMonsterHouses(
        IReadOnlyList<RoomMetadata> metadata,
        GenerationConfig config,
        Random rng)
    {
        if (!config.MonsterHouseEnabled) return;

        var candidates = metadata.Where(m => !m.IsHiddenRoom).ToList();
        if (candidates.Count == 0) return;

        int assigned = 0;
        const int maxMonsterHouses = 3;

        foreach (var room in candidates)
        {
            if (assigned >= maxMonsterHouses) break;

            if (rng.NextDouble() < config.MonsterHouseChance)
            {
                room.IsMonsterHouse = true;
                room.ItemDensityMultiplier = 3.0f;
                room.MonsterDensityMultiplier = 3.0f;
                assigned++;
            }
        }

        // Guarantee at least 1 monster house when enabled
        if (assigned == 0 && candidates.Count > 0)
        {
            var target = candidates[rng.Next(candidates.Count)];
            target.IsMonsterHouse = true;
            target.ItemDensityMultiplier = 3.0f;
            target.MonsterDensityMultiplier = 3.0f;
        }
    }
}
