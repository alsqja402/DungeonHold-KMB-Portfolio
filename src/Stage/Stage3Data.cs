using UnityEngine;

[CreateAssetMenu(fileName = "Stage3Data", menuName = "KMB/Stage/Stage3 Data")]
public class Stage3Data : Stage1Data
{
    [Header("Stage 3 방어 목표")]
    [SerializeField, Min(1f)] private float _phaseOneGoalMaxHp = 50f;

    [Header("Stage 3 스폰 전환")]
    [SerializeField] private bool _waitForPhaseTwoSpawns = true;
    [SerializeField, Min(1)] private int _phaseOneSpawnCount = 8;
    [SerializeField, Min(1)] private int _phaseOneTimeoutStartWave = 1;
    [SerializeField, Min(0f)] private float _phaseTwoSpawnDelay = 3f;

    [Header("보스 전투 배율")]
    [SerializeField, Min(0.01f)] private float _bossHpMultiplier = 1f;
    [SerializeField, Min(0.01f)] private float _bossAttackMultiplier = 1f;

    public float PhaseOneGoalMaxHp => _phaseOneGoalMaxHp;
    public float BossHpMultiplier => _bossHpMultiplier;
    public float BossAttackMultiplier => _bossAttackMultiplier;
    public bool WaitForPhaseTwoSpawns => _waitForPhaseTwoSpawns;
    public int PhaseOneSpawnCount => _phaseOneSpawnCount;
    public int PhaseOneTimeoutStartWave => Mathf.Clamp(_phaseOneTimeoutStartWave, 1, WaveCount);
    public float PhaseTwoSpawnDelay => _phaseTwoSpawnDelay;

    internal bool HasValidPhaseOneGoalHp =>
        _phaseOneGoalMaxHp >= 1f && !float.IsInfinity(_phaseOneGoalMaxHp);

    internal bool HasValidPhaseSpawnSettings =>
        !_waitForPhaseTwoSpawns ||
        (_phaseOneSpawnCount > 0 && _phaseTwoSpawnDelay >= 0f &&
         !float.IsInfinity(_phaseTwoSpawnDelay));
}
