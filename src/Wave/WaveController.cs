using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static UnityEngine.EventSystems.EventTrigger;

public class WaveController : MonoBehaviour
{
    [SerializeField] private Stage1Data _stageData;

    // Stage 1 데이터와 둘 중 하나만 연결
    [SerializeField] private Stage2Data _stage2Data;

    // 각 스포너가 자신의 스폰 지점과 이동 경로를 사용
    [SerializeField]
    private EnemySpawner[] _enemySpawners =
        new EnemySpawner[0];

    [SerializeField] private EnemyMovement _bossPrefab;
    [SerializeField] private EnemySpawner _bossSpawner;
    [SerializeField] private bool _waitForPhaseTwoAfterFirstBatch;
    [SerializeField, Min(0f)] private float _phaseTwoSpawnDelay = 3f;

    private HashSet<EnemyMovement> _activeEnemies =
        new HashSet<EnemyMovement>();

    private int _passedEnemyCount;


    private int _currentWaveNumber;
    private int _pendingWaveSaveNumber;
    private float _remainingPreparationTime;
    private bool _isPreparing;

    // 이벤트 인자는 1부터 시작하는 웨이브 번호
    public event Action<int> OnPreparationStarted;      // 해당 웨이브의 준비 시작
    public event Action<int> OnWaveStarted;             // 해당 웨이브 시작
    public event Action<int> OnWaveCompleted;           // 생성 완료, 활성 적 0으로 해당 웨이브 완료

    // 모든 웨이브 진행 종료이며, 스테이지 클리어 판정은 별도
    public event Action OnAllWavesCompleted;            // 모든 웨이브 완료

    // 이번 스테이지에서 누적 통과한 적 수 전달
    public event Action<int> OnPassedEnemyCountChanged;

    private int _diedEnemyCount;

    // 점수에 사용하는 플레이어·동료·설치물의 누적 처치 수
    private int _teamKillCount;

    [SerializeField] private StageRewardController _stageRewards;

    [SerializeField] private LJPlay.PlayScene _playerScene;

    [SerializeField] private bool _waitForMapPreparation;

    // 첫 전투 시작은 도전당 한 번만 실행
    private bool _hasBegunCombat;
    private bool _usePhaseTwoExit;
    private bool _spawnBossOnPhaseTwo;
    private bool _hasSpawnedFirstBatch;
    private bool _hasReleasedPhaseTwoSpawns;
    private double _phaseTwoSpawnReadyAt;
    private int _phaseOneSpawnedCount;
    private EnemyMovement _phaseBoss;

    // 맵 번호가 아닌 이번 플레이 회차의 도전 번호
    private int _stageId;

    public int StageId => _stageId;

    // KMB WaveController들이 발급하는 생명 번호를 공유
    // 웨이브가 바뀌어도 초기화하지 않음
    private static long _lastIssuedLifeId;

    public int DiedEnemyCount => _diedEnemyCount;
    public int TeamKillCount => _teamKillCount;

    public int CurrentWaveNumber => _currentWaveNumber;

    /// <summary>
    /// 현재 난이도에 실제 적용된 전체 웨이브 수. 초기화 완료 전에는 0을 반환합니다.
    /// </summary>
    public int TotalWaveCount => HasValidatedSetup ? WaveCount : 0;

    public float RemainingPreparationTime => _remainingPreparationTime;
    public bool IsPreparing => _isPreparing;
    public bool HasCompletedAllWaves { get; private set; }

    private bool _isWaveRunning;
    private bool _hasFinishedSpawning;
    private bool _hasWaveFailed;

    internal bool HasActivePhaseBoss => _phaseBoss != null && _activeEnemies.Contains(_phaseBoss);
    public int ActiveEnemyCount => _activeEnemies.Count - (HasActivePhaseBoss ? 1 : 0);
    public int PassedEnemyCount => _passedEnemyCount;
    public bool IsWaveRunning => _isWaveRunning;
    public bool HasBegunCombat => _hasBegunCombat;
    internal bool HasDefeatedPhaseBoss { get; private set; }
    internal EnemyData PhaseBossData { get; private set; }
    internal Stage3Data Stage3SpawnData => _stageData as Stage3Data;
    internal bool IsPhaseOneTimeoutWaveReached =>
        _currentWaveNumber >= (Stage3SpawnData?.PhaseOneTimeoutStartWave ?? 1);

    private int _enemyDeathDispatchDepth;

    public bool IsDispatchingEnemyDeath => _enemyDeathDispatchDepth > 0;
    public bool HasValidatedSetup { get; private set; }

    // 스테이지 결과에 의해 웨이브 진행이 중단되었는지 여부
    public bool HasStoppedForStageResult { get; private set; }

    // 연결된 스테이지 데이터에서 웨이브 진행 설정 조회
    private int WaveCount =>
        _stage2Data != null
            ? _stage2Data.WaveCount
            : _stageData.WaveCount;

    private float FirstPreparationTime =>
        _stage2Data != null
            ? _stage2Data.FirstPreparationTime
            : _stageData.FirstPreparationTime;

    private float BetweenWavePreparationTime =>
        _stage2Data != null
            ? _stage2Data.BetweenWavePreparationTime
            : _stageData.BetweenWavePreparationTime;

    private float SpawnInterval =>
        _stage2Data != null
            ? _stage2Data.SpawnInterval
            : _stageData.SpawnInterval;

    private int MaxActiveEnemies =>
        _stage2Data != null
            ? _stage2Data.MaxActiveEnemies
            : _stageData.MaxActiveEnemies;

    private WaveData GetWave(int index)
    {
        return _stage2Data != null
            ? _stage2Data.GetWave(index)
            : _stageData.GetWave(index);
    }

    private (float Hp, float Attack) GetEnemyCombatMultipliers(bool isBoss)
    {
        if (isBoss)
        {
            Stage3Data bossStage = Stage3SpawnData;
            return (bossStage?.BossHpMultiplier ?? 1f, bossStage?.BossAttackMultiplier ?? 1f);
        }

        return _stage2Data != null
            ? (_stage2Data.EnemyHpMultiplier, _stage2Data.EnemyAttackMultiplier)
            : (_stageData.EnemyHpMultiplier, _stageData.EnemyAttackMultiplier);
    }

    private void Start()
    {
        if (!_waitForMapPreparation)
        {
            InitializeWaveSetup();
        }
    }

    public bool TryInitializeForMap(
        LJPlay.PlayScene playScene, Stage1Data stageData = null, Stage2Data stage2Data = null)
    {
        if (!_waitForMapPreparation ||
            !isActiveAndEnabled ||
            playScene == null ||
            !playScene.isActiveAndEnabled ||
            !playScene.IsInitialized ||
            playScene.Hero == null ||
            playScene.Rewards == null ||
            (stageData != null && stage2Data != null))
        {
            return false;
        }

        if (HasValidatedSetup)
        {
            return _playerScene == playScene &&
                (stageData == null || _stageData == stageData) &&
                (stage2Data == null || _stage2Data == stage2Data);
        }

        if (stageData != null || stage2Data != null)
        {
            _stageData = stageData;
            _stage2Data = stage2Data;
        }

        _playerScene = playScene;
        _stageRewards = playScene.Rewards;
        _hasWaveFailed = false;

        InitializeWaveSetup();
        return HasValidatedSetup;
    }

    private void InitializeWaveSetup()
    {
        bool hasStage1Data = _stageData != null;
        bool hasStage2Data = _stage2Data != null;

        // Stage 3 데이터는 Stage Data 칸을 사용합니다.
        if (hasStage1Data == hasStage2Data)
        {
            StopWaveWithError(
                "Stage Data와 Stage 2 Data 중 하나만 연결하세요.");

            return;
        }

        bool hasValidStageData = hasStage2Data
            ? _stage2Data.IsValid
            : _stageData.IsValid;
        Stage3Data stage3Data = Stage3SpawnData;
        if (stage3Data != null && !stage3Data.HasValidPhaseSpawnSettings)
        {
            StopWaveWithError("Stage 3의 1차 생성 수와 2차 생성 대기 시간을 확인하세요.");
            return;
        }

        bool isValidSetting =
            _playerScene != null &&
            _playerScene.isActiveAndEnabled &&
            _enemySpawners != null &&
            _enemySpawners.Length > 0 &&
            _stageRewards != null &&
            _stageRewards == _playerScene.Rewards &&
            hasValidStageData;

        if (!isValidSetting)
        {
            StopWaveWithError(
                "TestScene, 스포너 목록, 동일한 보상 시스템과 " +
                "스테이지 데이터 설정을 확인하세요.");

            return;
        }

        // 같은 스포너를 여러 칸에 연결하면
        // 한 묶음에서 같은 지점을 선택할 수 있으므로 검사
        HashSet<EnemySpawner> registeredSpawners =
            new HashSet<EnemySpawner>();

        foreach (EnemySpawner spawner in _enemySpawners)
        {
            if (spawner == null ||
                !spawner.isActiveAndEnabled ||
                !registeredSpawners.Add(spawner))
            {
                StopWaveWithError(
                    "스포너의 누락, 비활성 또는 중복 연결을 확인하세요.");

                return;
            }
        }

        bool hasBossSetting = stage3Data != null || _bossPrefab != null || _bossSpawner != null;

        if (hasBossSetting &&
            (_bossPrefab == null ||
             _bossSpawner == null ||
             !registeredSpawners.Contains(_bossSpawner)))
        {
            StopWaveWithError(
                "Boss Prefab과 Enemy Spawners 목록에 등록된 Boss Spawner를 연결하세요.");
            return;
        }

        int spawnersPerBatch = stage3Data != null ? _enemySpawners.Length - 1 : _enemySpawners.Length;
        for (int waveIndex = 0;
            waveIndex < WaveCount;
            waveIndex++)
        {
            WaveData wave = GetWave(waveIndex);

            int directionCount = _stage2Data != null
                ? _stage2Data.GetSpawnDirectionCount(waveIndex)
                : 0;
            if (directionCount > 0 &&
                (_enemySpawners.Length % 2 != 0 ||
                 directionCount > _enemySpawners.Length / 2 || hasBossSetting))
            {
                StopWaveWithError(
                    $"{waveIndex + 1}웨이브의 방향 수와 스포너 연결을 확인하세요. " +
                    "Stage 2 방향별 스포너는 A/B, C/D, E/F, G/H 순서의 쌍으로 연결해야 합니다.");
                return;
            }

            if (_bossSpawner != null && _enemySpawners.Length == 1)
            {
                for (int groupIndex = 0; groupIndex < wave.SpawnGroupCount; groupIndex++)
                {
                    if (wave.GetSpawnGroup(groupIndex).EnemyPrefab != _bossPrefab)
                    {
                        StopWaveWithError(
                            $"{waveIndex + 1}웨이브의 일반 적을 생성할 스포너가 없습니다.");
                        return;
                    }
                }
            }

            int availableSpawners = directionCount > 0 ? directionCount * 2 : spawnersPerBatch;
            if (wave.SpawnBatchSize > availableSpawners)
            {
                StopWaveWithError(
                    $"{waveIndex + 1}웨이브의 동시 생성 수가 " +
                    "해당 웨이브에서 사용할 스포너 수보다 많습니다.");

                return;
            }
        }

        if (_waitForMapPreparation)
        {
            foreach (EnemySpawner spawner in _enemySpawners)
            {
                spawner.BindPlayer(_playerScene.Hero);
            }
        }

        HasValidatedSetup = true;

        StartCoroutine(RunWavesWhenStageReady());
    }

    // 준비 중인 웨이브를 시작하며, 전투 중 요청은 거절
    public bool TryStartWave()
    {
        bool canStart =
            isActiveAndEnabled &&
            _isPreparing &&
            _pendingWaveSaveNumber == 0 &&
            !_isWaveRunning &&
            !_hasWaveFailed &&
            !HasStoppedForStageResult &&
            _playerScene.isActiveAndEnabled &&
            _playerScene.IsStageReady &&
            !_playerScene.IsEndingStage &&
            _stageId > 0 &&
            _stageRewards.CurrentStageId == _stageId;

        if (!canStart)
        {
            return false;
        }

        // 첫 적을 생성하기 전에 LJ 동료 전투 준비부터 완료
        // 이후 웨이브에서는 반복 호출하지 않음
        if (!_hasBegunCombat)
        {
            bool wasStarted = _playerScene.BeginCombat(
                out CompanionHireResult result);

            if (!wasStarted)
            {
                StopWaveWithError(
                    $"첫 전투 준비에 실패했습니다: {result}");

                return false;
            }

            _hasBegunCombat = true;

            Debug.Log(
                $"첫 전투 연결 완료: 도전 {_stageId}",
                this);
        }
        else
        {
            // 첫 전투 이후에는 동료 시작을 반복하지 않고
            // 현재 웨이브가 전투 구간임을 전달
            if (!_playerScene.SetPlayPhase(_stageId, true))
            {
                StopWaveWithError(
                    $"전투 구간 전환 실패: " +
                    $"도전 {_stageId}, 웨이브 {_currentWaveNumber}");

                return false;
            }

            Debug.Log(
                $"LJ 전투 구간 전환: " +
                $"도전 {_stageId}, 웨이브 {_currentWaveNumber}",
                this);
        }

        _isPreparing = false;
        _remainingPreparationTime = 0f;
        _isWaveRunning = true;
        _hasFinishedSpawning = false;

        Debug.Log($"{_currentWaveNumber}웨이브 시작", this);

        OnWaveStarted?.Invoke(_currentWaveNumber);
        return true;
    }

    private IEnumerator SpawnEnemies(WaveData wave)
    {
        WaitForSeconds spawnWait =
            new WaitForSeconds(SpawnInterval);

        int spawnedCount = 0;
        int groupIndex = GetNextWaveSpawnGroup(wave, 0);
        int spawnedInGroup = 0;
        Stage3Data stage3Data = Stage3SpawnData;
        bool usePhaseSpawnSchedule = _spawnBossOnPhaseTwo &&
            stage3Data != null && stage3Data.WaitForPhaseTwoSpawns;
        // 전환 가능 웨이브 이전에는 스폰 상한으로 다음 웨이브 진입을 막지 않습니다.
        bool limitPhaseOneSpawns = usePhaseSpawnSchedule && IsPhaseOneTimeoutWaveReached;
        double nextBatchAt = 0d;

        // 방향은 웨이브 시작 시 한 번 선택하고 모든 생성 묶음에서 유지합니다.
        int[] waveSpawnerIndices = SelectWaveSpawnerIndices();
        int[] spawnerIndices = new int[waveSpawnerIndices.Length];
        int waveDirectionCount = _stage2Data != null
            ? _stage2Data.GetSpawnDirectionCount(_currentWaveNumber - 1)
            : 0;

        while (groupIndex < wave.SpawnGroupCount)
        {
            if (_hasWaveFailed || HasStoppedForStageResult)
            {
                yield break;
            }

            // 기존 테스트 씬의 설정을 사용합니다.
            if (stage3Data == null && _spawnBossOnPhaseTwo && _waitForPhaseTwoAfterFirstBatch &&
                _hasSpawnedFirstBatch && !_hasReleasedPhaseTwoSpawns)
            {
                while (!_usePhaseTwoExit || Time.timeAsDouble < _phaseTwoSpawnReadyAt)
                {
                    if (_hasWaveFailed || HasStoppedForStageResult)
                    {
                        yield break;
                    }

                    yield return null;
                }

                _hasReleasedPhaseTwoSpawns = true;
            }
            else if (spawnedCount > 0 && (!usePhaseSpawnSchedule || _hasReleasedPhaseTwoSpawns))
            {
                yield return spawnWait;
            }

            // 빈자리를 기다리는 동안에도 2차 전환과 생성 시간을 확인합니다.
            while (ActiveEnemyCount >= MaxActiveEnemies ||
                (usePhaseSpawnSchedule && !_hasReleasedPhaseTwoSpawns &&
                 ShouldWaitForPhaseSpawn(stage3Data, nextBatchAt, limitPhaseOneSpawns)))
            {
                if (_hasWaveFailed || HasStoppedForStageResult)
                {
                    yield break;
                }

                yield return null;
            }

            if (_hasWaveFailed || HasStoppedForStageResult)
            {
                yield break;
            }

            // 현재 여유 자리 안에서 이번 묶음의 생성 수 결정
            int availableSlots =
                MaxActiveEnemies - ActiveEnemyCount;

            int batchSize =
                Mathf.Min(wave.SpawnBatchSize, availableSlots);

            if (usePhaseSpawnSchedule)
            {
                if (_usePhaseTwoExit)
                {
                    _hasReleasedPhaseTwoSpawns = true;
                }
                else if (limitPhaseOneSpawns)
                {
                    batchSize = Mathf.Min(batchSize, stage3Data.PhaseOneSpawnCount - _phaseOneSpawnedCount);
                }
            }

            // 새 묶음에서는 이번 웨이브에 선택된 방향만 다시 사용
            for (int index = 0;
                 index < spawnerIndices.Length;
                 index++)
            {
                spawnerIndices[index] = waveSpawnerIndices[index];
            }

            for (int batchIndex = 0;
                 batchIndex < batchSize &&
                 groupIndex < wave.SpawnGroupCount;
                 batchIndex++)
            {
                if (_hasWaveFailed || HasStoppedForStageResult)
                {
                    yield break;
                }

                EnemySpawnData spawnGroup = wave.GetSpawnGroup(groupIndex);
                int bossIndex = _bossSpawner == null
                    ? -1
                    : Array.FindIndex(
                        spawnerIndices,
                        batchIndex,
                        index => _enemySpawners[index] == _bossSpawner);

                bool isBoss = _bossPrefab != null &&
                    spawnGroup.EnemyPrefab == _bossPrefab;
                int selectedSlot;

                if (isBoss)
                {
                    // 이번 묶음에서 보스 스포너를 이미 사용했으면 다음 묶음에서 생성
                    if (bossIndex < 0)
                    {
                        break;
                    }

                    selectedSlot = bossIndex;
                }
                else
                {
                    int endIndex = spawnerIndices.Length - (bossIndex >= 0 ? 1 : 0);

                    if (batchIndex >= endIndex)
                    {
                        break;
                    }

                    // 선택된 모든 방향에서 최소 한 마리씩 먼저 출현시킵니다.
                    selectedSlot = spawnedCount < waveDirectionCount
                        ? Array.IndexOf(spawnerIndices, waveSpawnerIndices[spawnedCount], batchIndex)
                        : UnityEngine.Random.Range(batchIndex, endIndex);

                    // 일반 적의 선택 범위에서 보스 스포너를 제외
                    if (bossIndex >= 0 && selectedSlot >= bossIndex)
                    {
                        selectedSlot++;
                    }
                }

                // 선택한 스포너를 앞으로 옮겨 이번 묶음의 재선택 방지
                int selectedIndex = spawnerIndices[selectedSlot];
                spawnerIndices[selectedSlot] = spawnerIndices[batchIndex];
                spawnerIndices[batchIndex] = selectedIndex;

                EnemySpawner spawner =
                    _enemySpawners[selectedIndex];

                if (spawner == null || !spawner.isActiveAndEnabled)
                {
                    StopWaveWithError(
                        "생성 중 스포너가 없어졌거나 비활성화되었습니다.");

                    yield break;
                }

                int stageId = _stageId;

                bool canSpawnInStage =
                    _stageRewards.isActiveAndEnabled &&
                    stageId > 0 &&
                    _stageRewards.CurrentStageId == stageId &&
                    _playerScene.isActiveAndEnabled &&
                    _playerScene.IsStageReady &&
                    !_playerScene.IsEndingStage;

                if (!canSpawnInStage)
                {
                    StopWaveWithError(
                        "적을 생성할 원래 도전이 종료되었거나 변경되었습니다.");

                    yield break;
                }

                if (!spawner.TryCreateEnemy(
                    spawnGroup.EnemyPrefab,
                    out EnemyMovement enemy))
                {
                    StopWaveWithError("적 생성에 실패했습니다.");
                    yield break;
                }

                // 이동 전에 통과·사망 알림 등록
                if (!TryRegisterEnemy(enemy))
                {
                    Destroy(enemy.gameObject);

                    StopWaveWithError("적 등록에 실패했습니다.");
                    yield break;
                }

                long lifeId = ++_lastIssuedLifeId;

                // 생성에 사용한 스포너로 초기화해야
                // 해당 스포너에 연결된 이동 경로를 전달받음
                var multipliers = GetEnemyCombatMultipliers(isBoss);
                if (!spawner.TryInitializeEnemy(
                    enemy, stageId, lifeId, _usePhaseTwoExit && !isBoss,
                    hpMultiplier: multipliers.Hp, attackMultiplier: multipliers.Attack))
                {
                    TryUnregisterEnemy(enemy);
                    Destroy(enemy.gameObject);

                    StopWaveWithError("적 초기화에 실패했습니다.");
                    yield break;
                }

                spawnedCount++;
                spawnedInGroup++;
                _hasSpawnedFirstBatch = true;
                if (usePhaseSpawnSchedule && !_usePhaseTwoExit)
                {
                    _phaseOneSpawnedCount++;
                }

                // 현재 종류의 수량을 채웠으면 다음 그룹으로 이동
                // 그룹이 바뀌어도 이번 생성 묶음은 계속 진행
                if (spawnedInGroup >= spawnGroup.Count)
                {
                    groupIndex = GetNextWaveSpawnGroup(wave, groupIndex + 1);
                    spawnedInGroup = 0;
                }
            }

            nextBatchAt = Time.timeAsDouble + SpawnInterval;
        }

        _hasFinishedSpawning = true;

        Debug.Log(
            $"{_currentWaveNumber}웨이브 예정된 적 생성 완료: " +
            $"{spawnedCount}마리",
            this);

        CheckWaveCompleted();
    }

    private int[] SelectWaveSpawnerIndices()
    {
        int directionCount = _stage2Data != null
            ? _stage2Data.GetSpawnDirectionCount(_currentWaveNumber - 1)
            : 0;
        if (directionCount == 0)
        {
            int[] allIndices = new int[_enemySpawners.Length];
            for (int index = 0; index < allIndices.Length; index++)
                allIndices[index] = index;
            return allIndices;
        }

        int[] directions = new int[_enemySpawners.Length / 2];
        for (int index = 0; index < directions.Length; index++)
            directions[index] = index;

        int[] selectedIndices = new int[directionCount * 2];
        for (int index = 0; index < directionCount; index++)
        {
            int slot = UnityEngine.Random.Range(index, directions.Length);
            int direction = directions[slot];
            directions[slot] = directions[index];
            directions[index] = direction;
            int firstSide = UnityEngine.Random.Range(0, 2);
            selectedIndices[index] = direction * 2 + firstSide;
            selectedIndices[index + directionCount] = direction * 2 + (1 - firstSide);
        }

        Debug.Log(
            $"Stage 2 {_currentWaveNumber}웨이브: {directionCount}방향 선택 / " +
            string.Join(", ", Array.ConvertAll(selectedIndices, index => _enemySpawners[index].name)),
            this);
        return selectedIndices;
    }

    private bool ShouldWaitForPhaseSpawn(Stage3Data data, double nextBatchAt, bool limitPhaseOneSpawns)
    {
        return _usePhaseTwoExit
            ? Time.timeAsDouble < _phaseTwoSpawnReadyAt
            : Time.timeAsDouble < nextBatchAt ||
              (limitPhaseOneSpawns && _phaseOneSpawnedCount >= data.PhaseOneSpawnCount);
    }

    private int GetNextWaveSpawnGroup(WaveData wave, int index)
    {
        while (_spawnBossOnPhaseTwo && index < wave.SpawnGroupCount &&
               wave.GetSpawnGroup(index).EnemyPrefab == _bossPrefab)
        {
            index++;
        }

        return index;
    }

    // TestScene과 WaveController의 Start 실행 순서에 의존하지 않음
    private IEnumerator RunWavesWhenStageReady()
    {
        while (!_playerScene.IsStageReady)
        {
            if (!_playerScene.isActiveAndEnabled)
            {
                StopWaveWithError(
                    "플레이어 도전 준비 전에 TestScene이 비활성화되었습니다.");

                yield break;
            }

            yield return null;
        }

        _stageId = _stageRewards.CurrentStageId;

        if (_stageId <= 0)
        {
            StopWaveWithError(
                "플레이어 준비 후 유효한 도전 번호를 확인할 수 없습니다.");

            yield break;
        }

        // 도전 준비가 끝난 다음 첫 웨이브 준비 시간 시작
        yield return RunWaves();
    }

    // 준비, 생성, 남은 적 대기, 다음 웨이브를 순서대로 진행
    private IEnumerator RunWaves(int startWaveIndex = 0)
    {
        for (int waveIndex = startWaveIndex; waveIndex < WaveCount; waveIndex++)
        {
            while (_pendingWaveSaveNumber > 0 &&
            !_hasWaveFailed &&
            !HasStoppedForStageResult)
            {
                yield return null;
            }

            if (_hasWaveFailed || HasStoppedForStageResult)
            {
                yield break;
            }

            _currentWaveNumber = waveIndex + 1;
            // 첫 웨이브만 긴 준비 시간 적용
            _remainingPreparationTime = waveIndex == 0
            ? FirstPreparationTime
            : BetweenWavePreparationTime;
            _hasFinishedSpawning = false;
            _isPreparing = true;

            Debug.Log(
            $"{_currentWaveNumber}웨이브 준비 시작: " +
            $"{_remainingPreparationTime:F1}초",
            this);

            OnPreparationStarted?.Invoke(_currentWaveNumber);

            // 시간 만료 또는 외부 조기 시작 요청까지 대기
            while (_isPreparing && !_hasWaveFailed)
            {
                _remainingPreparationTime = Mathf.Max(
                    0f,
                    _remainingPreparationTime - Time.deltaTime);

                if (_remainingPreparationTime <= 0f)
                {
                    TryStartWave();
                }

                yield return null;
            }

            if (_hasWaveFailed)
            {
                yield break;
            }

            // 시작 요청이 여러 번 와도 생성은 이곳에서 한 번만 실행
            WaveData wave = GetWave(waveIndex);
            yield return SpawnEnemies(wave);

            if (_hasWaveFailed)
            {
                yield break;
            }

            // 생성이 끝나도 활성 적이 남아 있으면 완료까지 대기
            while (_isWaveRunning && !_hasWaveFailed)
            {
                yield return null;
            }

            if (_hasWaveFailed)
            {
                yield break;
            }
        }

        HasCompletedAllWaves = true;

        Debug.Log("전체 웨이브 종료", this);

        OnAllWavesCompleted?.Invoke();
    }

    internal void UsePhaseTwoBossSpawn() => _spawnBossOnPhaseTwo = true;

    internal bool TryStartPhaseTwo()
    {
        if (!_spawnBossOnPhaseTwo || _usePhaseTwoExit || !HasValidatedSetup ||
            _hasWaveFailed || HasStoppedForStageResult || _bossPrefab == null || _bossSpawner == null)
        {
            return false;
        }

        foreach (EnemySpawner spawner in _enemySpawners)
        {
            if (spawner == null || (spawner != _bossSpawner && !spawner.HasPhaseTwoExitPath))
            {
                Debug.LogError("일반 스포너의 2차 출구 경로를 확인하세요.", spawner);
                return false;
            }
        }

        if (!_bossSpawner.TryCreateEnemy(_bossPrefab, out EnemyMovement boss, pursuePlayerOnly: true))
        {
            return false;
        }

        _phaseBoss = boss;
        var multipliers = GetEnemyCombatMultipliers(isBoss: true);
        if (!TryRegisterEnemy(boss) ||
            !_bossSpawner.TryInitializeEnemy(
                boss, _stageId, ++_lastIssuedLifeId, pursuePlayerOnly: true,
                hpMultiplier: multipliers.Hp, attackMultiplier: multipliers.Attack) ||
            (!_playerScene.Missions.IsPlaying && !_playerScene.SetPlayPhase(_stageId, true)))
        {
            TryUnregisterEnemy(boss);
            _phaseBoss = null;
            Destroy(boss.gameObject);
            return false;
        }

        PhaseBossData = boss.GetComponent<EnemyCombat>().Data;
        _usePhaseTwoExit = true;
        float spawnDelay = _stageData is Stage3Data stage3Data
            ? stage3Data.PhaseTwoSpawnDelay
            : _phaseTwoSpawnDelay;
        _phaseTwoSpawnReadyAt = Time.timeAsDouble + spawnDelay;
        Debug.Log($"Stage 3 2차 보스 소환: {boss.name}", boss);
        return true;
    }

    public int RedirectEnemiesToExit(
        Stage2DefenseGoal phaseOneGoal, EnemyPath exitPath)
    {
        if (phaseOneGoal == null || exitPath == null || exitPath.WaypointCount == 0)
        {
            return 0;
        }

        int redirectedCount = 0;
        foreach (EnemyMovement enemy in new List<EnemyMovement>(_activeEnemies))
        {
            if (enemy == null ||
                enemy.DefenseGoal != phaseOneGoal ||
                !enemy.TryGetComponent(out EnemyCombat combat) ||
                !combat.CanReceiveDamage)
            {
                continue;
            }

            if (enemy.TrySwitchToExitPath(exitPath))
            {
                redirectedCount++;
            }
            else
            {
                Debug.LogWarning($"출구 경로 전환 실패: {enemy.name}", enemy);
            }
        }

        return redirectedCount;
    }

    // 통과와 사망 결과를 모두 구독
    private bool TryRegisterEnemy(EnemyMovement enemy)
    {
        if (enemy == null)
        {
            return false;
        }

        EnemyCombat enemyCombat = enemy.GetComponent<EnemyCombat>();

        if (enemyCombat == null || !_activeEnemies.Add(enemy))
        {
            return false;
        }

        enemy.OnPassed += HandleEnemyPassed;
        enemyCombat.OnDied += HandleEnemyDied;

        Debug.Log($"현재 활성 적: {ActiveEnemyCount}", this);
        return true;
    }

    private bool TryUnregisterEnemy(EnemyMovement enemy)
    {
        if (enemy == null || !_activeEnemies.Remove(enemy))
        {
            return false;
        }

        enemy.OnPassed -= HandleEnemyPassed;

        EnemyCombat enemyCombat = enemy.GetComponent<EnemyCombat>();

        if (enemyCombat != null)
        {
            enemyCombat.OnDied -= HandleEnemyDied;
        }

        Debug.Log($"현재 활성 적: {ActiveEnemyCount}", this);
        return true;
    }

    private void HandleEnemyDied(
    EnemyCombat enemyCombat,
    DamageInfo damageInfo)
    {
        EnemyMovement enemy =
            enemyCombat.GetComponent<EnemyMovement>();
        bool isPhaseBoss = _phaseBoss != null && enemy == _phaseBoss;

        // 등록 해제에 성공한 첫 사망 알림만 처리
        if (!TryUnregisterEnemy(enemy))
        {
            return;
        }

        // 외부 알림 전에 원래 생명의 정보를 보관
        int stageId = enemyCombat.RewardStageId;
        long lifeId = enemyCombat.RewardLifeId;
        int killReward = enemyCombat.KillReward;
        EnemyData enemyData = enemyCombat.Data;

        if (!isPhaseBoss)
        {
            _diedEnemyCount++;
            Debug.Log($"현재 사망한 적: {DiedEnemyCount}", this);
        }

        // 재화 보상과 별개로 실제 적 사망을 미션에 전달
        // 통과·단순 제거·생성 실패는 이 사망 경로를 사용하지 않음
        MissionKillContext kill = new MissionKillContext(
            stageId,
            lifeId,
            enemyData);

        MissionKillResult missionResult =
            _playerScene.RegisterMissionKill(kill);

        Debug.Log(
            $"미션 처치 접수: 도전 {stageId}, " +
            $"생명 {lifeId}, 적 {enemyData.name}, " +
            $"원인 {damageInfo.ActorType}/{damageInfo.Faction}, " +
            $"결과 {missionResult}",
            this);

        if (missionResult == MissionKillResult.AcceptedSavePending)
        {
            // 이미 집계된 처치이므로 같은 생명을 다시 전달하지 않음
            Debug.LogWarning(
                $"미션 처치는 접수됐지만 저장 대기 중입니다: " +
                $"도전 {stageId}, 생명 {lifeId}. " +
                "처치 재전달이 아닌 미션 저장 재시도가 필요합니다.",
                this);
        }

        // 기존 팀 처치 재화 지급 기준 유지
        // 화상도 원래 공격 주체와 진영 정보로 판정
        bool isTeamKill =
            damageInfo.Faction == Faction.Ally &&
            (damageInfo.ActorType == ActorType.Player ||
             damageInfo.ActorType == ActorType.Companion ||
             damageInfo.ActorType == ActorType.Installation);

        bool wasRewardAccepted = true;

        if (isTeamKill)
        {
            // 실제 처치는 보상 지급 성공 여부와 별도로 집계
            _teamKillCount++;

            wasRewardAccepted = _stageRewards.RegisterKill(
                stageId,
                lifeId,
                killReward,
                Time.timeAsDouble);

            if (wasRewardAccepted)
            {
                Debug.Log(
                    $"적 처치 접수 완료: " +
                    $"생명 {lifeId}, 기본 보상 {killReward}, " +
                    $"현재 재화 {_stageRewards.StageCurrencyBalance}, " +
                    $"미지급 요청 {_stageRewards.PendingRewardCount}",
                    this);

                if (_stageRewards.PendingRewardCount > 0)
                {
                    Debug.LogWarning(
                        "접수된 재화 보상 중 미지급 요청이 남아 있습니다.",
                        this);
                }
            }
            else
            {
                Debug.LogError(
                    $"적 처치 보상 접수 실패: " +
                    $"도전 {stageId}, 생명 {lifeId}",
                    this);
            }
        }

        // 한쪽 요청이 실패해도 다른 요청까지 생략하지 않음
        // 두 요청을 마친 뒤 진행 여부 판단
        if (missionResult == MissionKillResult.Rejected ||
            !wasRewardAccepted)
        {
            StopWaveWithError(
                $"처치 전달 상태를 확인하세요: " +
                $"미션 {missionResult}, 재화 접수 {wasRewardAccepted}");

            return;
        }

        if (isPhaseBoss)
        {
            HasDefeatedPhaseBoss = true;
            Debug.Log("Stage 3 2차 보스 처치", this);

            if (!_isWaveRunning && _isPreparing && !_playerScene.SetPlayPhase(_stageId, false))
            {
                StopWaveWithError("보스 처치 후 준비 구간 전환에 실패했습니다.");
                return;
            }
        }

        // 마지막 적의 미션·재화 요청을 마친 뒤에만
        // 준비 구간 전환과 웨이브 완료 판정
        CheckWaveCompleted();
    }

    private void HandleEnemyPassed(EnemyMovement enemy)
    {
        if (!TryUnregisterEnemy(enemy))
        {
            return;
        }

        _passedEnemyCount++;

        Debug.Log($"현재 통과한 적: {PassedEnemyCount}", this);

        // 스테이지가 실패 여부를 먼저 판정할 수 있도록 알림
        OnPassedEnemyCountChanged?.Invoke(_passedEnemyCount);

        CheckWaveCompleted();
    }

    // 생성 완료와 활성 적 0을 함께 만족해야 웨이브 완료
    private void CheckWaveCompleted()
    {
        bool canComplete =
            _isWaveRunning &&
            !_hasWaveFailed &&
            !HasStoppedForStageResult &&
            _hasFinishedSpawning &&
            ActiveEnemyCount == 0;

        if (!canComplete)
        {
            return;
        }

        // 마지막 적의 사망·통과 처리를 마친 뒤 구간 전환
        if (!HasActivePhaseBoss && !_playerScene.SetPlayPhase(_stageId, false))
        {
            StopWaveWithError(
                $"준비 구간 전환 실패: " +
                $"도전 {_stageId}, 웨이브 {_currentWaveNumber}");

            return;
        }

        // 진행 상태를 먼저 종료해 중복 완료 방지
        _isWaveRunning = false;

        if (_currentWaveNumber < WaveCount)
        {
            _pendingWaveSaveNumber = _currentWaveNumber;
            TrySaveCompletedWave();
        }

        if (!HasActivePhaseBoss)
        {
            Debug.Log($"LJ 준비 구간 전환: 도전 {_stageId}, 완료 웨이브 {_currentWaveNumber}", this);
        }

        Debug.Log(
            $"{_currentWaveNumber}웨이브 완료",
            this);

        OnWaveCompleted?.Invoke(_currentWaveNumber);
    }

    private void TrySaveCompletedWave()
    {
        if (_pendingWaveSaveNumber == 0 ||
            !isActiveAndEnabled ||
            _hasWaveFailed ||
            HasStoppedForStageResult ||
            _playerScene == null ||
            !_playerScene.IsStageReady ||
            _playerScene.IsEndingStage ||
            _stageRewards.CurrentStageId != _stageId)
        {
            return;
        }

        int waveNumber = _pendingWaveSaveNumber;

        // 보스가 교전 중이면 저장 호출 동안만 준비 구간으로 바꿉니다.
        bool bossIsActive = HasActivePhaseBoss;
        if (bossIsActive && !_playerScene.SetPlayPhase(_stageId, false))
        {
            StopWaveWithError("보스 전투 중 웨이브 저장 준비에 실패했습니다.");
            return;
        }

        bool wasSaved = _playerScene.TrySaveWave(_stageId, waveNumber);
        if (bossIsActive && !_playerScene.SetPlayPhase(_stageId, true))
        {
            StopWaveWithError("웨이브 저장 후 보스 전투 구간 복원에 실패했습니다.");
            return;
        }

        if (wasSaved)
        {
            _pendingWaveSaveNumber = 0;
            Debug.Log($"웨이브 저장 완료: 도전 {_stageId}, 웨이브 {waveNumber}", this);
            return;
        }

        Debug.LogWarning(
            $"웨이브 저장 대기: 도전 {_stageId}, 웨이브 {waveNumber}",
            this);
    }

    [ContextMenu("테스트/웨이브 저장 재시도")]
    private void RetryWaveSaveForTest()
    {
        if (Application.isPlaying)
        {
            TrySaveCompletedWave();
        }
    }

    private void StopWaveWithError(string message)
    {
        _hasWaveFailed = true;
        _isPreparing = false;
        _isWaveRunning = false;
        _remainingPreparationTime = 0f;

        Debug.Log($"웨이브 진행 중단: {message}", this);
    }

    private void OnEnable()
    {
        if (_pendingWaveSaveNumber > 0 &&
            !_hasWaveFailed &&
            !HasStoppedForStageResult)
        {
            StartCoroutine(RunWaves(_pendingWaveSaveNumber));
        }
    }

    private void OnDisable()
    {
        StopAllCoroutines();

        if (_isPreparing || _isWaveRunning)
        {
            StopWaveWithError("WaveController가 비활성화되었습니다.");
        }
    }

    private void OnDestroy()
    {
        foreach (EnemyMovement enemy in _activeEnemies)
        {
            if (enemy == null)
            {
                continue;
            }

            enemy.OnPassed -= HandleEnemyPassed;

            EnemyCombat enemyCombat = enemy.GetComponent<EnemyCombat>();

            if (enemyCombat != null)
            {
                enemyCombat.OnDied -= HandleEnemyDied;
            }
        }

        _activeEnemies.Clear();
    }

    // 스테이지 결과에 따라 생성과 남은 적의 활동 중단
    public void StopForStageResult()
    {
        if (HasStoppedForStageResult)
        {
            return;
        }

        // 외부 알림이나 비활성화 처리 전에 진행 상태 확정
        HasStoppedForStageResult = true;
        _isPreparing = false;
        _isWaveRunning = false;
        _remainingPreparationTime = 0f;

        StopAllCoroutines();

        // 등록 해제로 원본 집합이 바뀌므로 복사본을 순회
        EnemyMovement[] remainingEnemies =
            new EnemyMovement[_activeEnemies.Count];

        _activeEnemies.CopyTo(remainingEnemies);

        foreach (EnemyMovement enemy in remainingEnemies)
        {
            if (enemy == null)
            {
                continue;
            }

            // 결과 정리는 사망이나 통과로 집계하지 않음
            if (TryUnregisterEnemy(enemy))
            {
                enemy.gameObject.SetActive(false);
            }
        }

        Debug.Log("스테이지 결과에 따라 웨이브 진행을 중단했습니다.", this);
    }
}
