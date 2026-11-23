using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(
    fileName = "KMB_LobbyEnemySource",
    menuName = "KMB/Lobby Enemy Source")]
public class KMBLobbyEnemySource : LobbyEnemySource
{
    [SerializeField] private Stage1Data _stage1EasyData;
    [SerializeField] private Stage1Data _stage1NormalData;
    [SerializeField] private Stage1Data _stage1HardData;
    [SerializeField] private Stage2Data _stage2EasyData;
    [SerializeField] private Stage2Data _stage2NormalData;
    [SerializeField] private Stage2Data _stage2HardData;
    [SerializeField] private Stage3Data _stage3EasyData;
    [SerializeField] private Stage3Data _stage3NormalData;
    [SerializeField] private Stage3Data _stage3HardData;
    [SerializeField] private EnemyData _stage3BossData;

    public Stage1Data GetStage1Data(MissionDifficulty difficulty)
    {
        return difficulty switch
        {
            MissionDifficulty.Easy => _stage1EasyData,
            MissionDifficulty.Normal => _stage1NormalData,
            MissionDifficulty.Hard => _stage1HardData,
            _ => null
        };
    }

    public Stage2Data GetStage2Data(MissionDifficulty difficulty)
    {
        return difficulty switch
        {
            MissionDifficulty.Easy => _stage2EasyData,
            MissionDifficulty.Normal => _stage2NormalData,
            MissionDifficulty.Hard => _stage2HardData,
            _ => null
        };
    }

    public Stage3Data GetStage3Data(MissionDifficulty difficulty)
    {
        return difficulty switch
        {
            MissionDifficulty.Easy => _stage3EasyData,
            MissionDifficulty.Normal => _stage3NormalData,
            MissionDifficulty.Hard => _stage3HardData,
            _ => null
        };
    }

    public override bool TryGetEnemies(
        string mapId,
        MissionDifficulty difficulty,
        out IReadOnlyList<EnemyData> enemies,
        out string reason)
    {
        enemies = Array.Empty<EnemyData>();

        if (mapId != "stage_1" && mapId != "stage_2" && mapId != "stage_3")
        {
            reason = $"등장 적 조회를 지원하지 않는 구성입니다. 맵 {mapId}, 난이도 {difficulty}";
            return false;
        }

        bool isStage2 = mapId == "stage_2";
        bool isStage3 = mapId == "stage_3";

        Stage2Data stage2Data = isStage2 ? GetStage2Data(difficulty) : null;
        Stage1Data stageData = isStage3 ? GetStage3Data(difficulty) : GetStage1Data(difficulty);
        bool hasValidData = isStage2
            ? stage2Data != null && stage2Data.IsValid
            : stageData != null && stageData.IsValid;

        if (!hasValidData)
        {
            reason = $"{mapId}의 선택 난이도 웨이브 데이터를 확인하세요. 난이도 {difficulty}";
            return false;
        }

        if (isStage3 && _stage3BossData == null)
        {
            reason = "Stage 3 보스의 EnemyData를 연결하세요.";
            return false;
        }

        var result = new List<EnemyData>();
        var seen = new HashSet<EnemyData>();

        int waveCount = isStage2 ? stage2Data.WaveCount : stageData.WaveCount;
        for (int waveIndex = 0; waveIndex < waveCount; waveIndex++)
        {
            WaveData wave = isStage2
                ? stage2Data.GetWave(waveIndex)
                : stageData.GetWave(waveIndex);

            for (int groupIndex = 0; groupIndex < wave.SpawnGroupCount; groupIndex++)
            {
                EnemyMovement prefab = wave.GetSpawnGroup(groupIndex).EnemyPrefab;
                if (!prefab.TryGetComponent(out EnemyCombat combat) || combat.Data == null)
                {
                    reason = $"웨이브 {waveIndex + 1}, 스폰 그룹 {groupIndex + 1}의 EnemyData가 누락됐습니다.";
                    return false;
                }

                if (seen.Add(combat.Data))
                    result.Add(combat.Data);
            }
        }

        if (isStage3 && seen.Add(_stage3BossData))
            result.Add(_stage3BossData);

        enemies = result;
        reason = null;
        return true;
    }
}
