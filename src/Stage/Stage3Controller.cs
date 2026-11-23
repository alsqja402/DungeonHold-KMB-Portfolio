using LJPlay;
using System.Collections.Generic;
using UnityEngine;

public class Stage3Controller : MonoBehaviour, IStageEndLink
{
    [SerializeField] private WaveController _waveController;
    [SerializeField] private Stage2DefenseGoal _defenseGoal;
    [SerializeField] private PlayScene _playerScene;
    [SerializeField] private bool _waitForMapPreparation;
    private bool _isInitialized;
    [SerializeField] private Stage1Data _stageData;
    [SerializeField] private StageScoreData _scoreData;
    [SerializeField] private EnemyPath _phaseTwoExitPath;
    [SerializeField, Min(1f)] private float _phaseOneTimeLimit = 60f;
    [SerializeField] private Stage3StatueReveal _statueReveal;
    private int? _clearScore;

    public string Error { get; private set; }
    private MissionStageResult? _pendingStageResult;
    private List<EnemyData> _pendingVictoryEnemies;
    private int _endingStageId;
    private int _endRequestFrame;
    private bool _waitingForEndEvents;
    private bool _endEventsRetryRequired;
    private float _elapsedCombatTime;
    private bool _exitPhaseRequested;
    private string _pendingExitPhaseReason;
    private bool _hasEnteredExitPhase;
    private bool _hasTriedExitPhaseVictory;
    private float _phaseOneGoalHp;
    private float _phaseOneGoalMaxHp;
    private bool _phaseOneGoalWasDamaged;
    private Hero _subscribedHero;
    private Health _heroHealth;

    /// <summary>
    /// 시간 초과 패배 판정에 사용하는 남은 전투 시간(초).
    /// 일반 웨이브와 보스 생존 구간에 누적한 전투 시간을 기준으로 계산합니다.
    /// 초기화 완료 전에는 0을 반환합니다.
    /// </summary>
    public float RemainingCombatTime => _isInitialized && _stageData != null
        ? Mathf.Max(0f, _stageData.CombatTimeLimit - _elapsedCombatTime)
        : 0f;

    private void OnEnable()
    {
        if (!_isInitialized)
        {
            if (_waitForMapPreparation)
                return;

            if (!TryInitialize(_playerScene))
            {
                enabled = false;
                return;
            }
        }

        SubscribeStageEvents();
    }

    public bool TryInitializeForMap(PlayScene playScene, Stage3Data stageData = null)
    {
        if (!_waitForMapPreparation || !isActiveAndEnabled ||
            playScene == null || !playScene.IsInitialized)
        {
            return false;
        }

        if (_isInitialized)
            return _playerScene == playScene && (stageData == null || _stageData == stageData);

        if (!TryInitialize(playScene, stageData))
            return false;

        SubscribeStageEvents();
        return true;
    }

    private bool TryInitialize(PlayScene playScene, Stage3Data selectedData = null)
    {
        Stage1Data stageData = selectedData != null ? selectedData : _stageData;
        if (_waveController == null || _defenseGoal == null ||
            playScene == null || playScene.Hero == null ||
            stageData == null || !stageData.IsValid ||
            _scoreData == null || !_scoreData.IsValid)
        {
            Debug.LogError("Stage3Controller의 참조와 스테이지 데이터를 확인하세요.", this);
            return false;
        }

        Stage3Data waveData = _waveController.Stage3SpawnData;
        if ((stageData is Stage3Data || waveData != null) &&
            (stageData != waveData || _phaseTwoExitPath == null ||
             _phaseTwoExitPath.WaypointCount == 0))
        {
            Debug.LogError(
                "WaveController와 Stage3Controller에 같은 Stage 3 데이터와 2차 출구 경로를 연결하세요.",
                this);
            return false;
        }

        if (stageData is Stage3Data stage3Data &&
            (!stage3Data.HasValidPhaseOneGoalHp || !_defenseGoal.UsesStage3Controller(this)))
        {
            Debug.LogError("Stage 3의 상자 최대 HP와 방어 목표의 Stage 3 Controller 참조를 확인하세요.", this);
            return false;
        }

        if (selectedData != null && !_defenseGoal.TryApplyStartingHpForMap(selectedData.PhaseOneGoalMaxHp))
        {
            Debug.LogError("Stage 3 상자에 선택 난이도의 최대 HP를 적용하지 못했습니다.", this);
            return false;
        }

        _stageData = stageData;
        _playerScene = playScene;
        _isInitialized = true;
        return true;
    }

    internal bool TryGetPhaseOneGoalMaxHp(Stage2DefenseGoal goal, out float maxHp)
    {
        maxHp = 0f;
        if (goal != _defenseGoal || !(_stageData is Stage3Data data) || !data.HasValidPhaseOneGoalHp)
        {
            return false;
        }

        maxHp = data.PhaseOneGoalMaxHp;
        return true;
    }

    private void SubscribeStageEvents()
    {
        _waveController.OnPassedEnemyCountChanged += HandlePassedCountChanged;
        _defenseGoal.OnDepleted += HandleGoalDepleted;
        _waveController.OnAllWavesCompleted += HandleAllWavesCompleted;

        _subscribedHero = _playerScene.Hero;
        _subscribedHero.OnReviveExhausted += HandlePlayerReviveExhausted;

        if (_phaseTwoExitPath != null)
        {
            _heroHealth = _subscribedHero.GetComponent<Health>();
            _waveController.UsePhaseTwoBossSpawn();
        }
    }

    private void OnDisable()
    {
        CancelPendingExitPhase();

        if (_waveController != null)
        {
            _waveController.OnPassedEnemyCountChanged -= HandlePassedCountChanged;
            _waveController.OnAllWavesCompleted -= HandleAllWavesCompleted;

        }

        if (_defenseGoal != null)
            _defenseGoal.OnDepleted -= HandleGoalDepleted;

        if (_subscribedHero != null)
            _subscribedHero.OnReviveExhausted -= HandlePlayerReviveExhausted;

        _subscribedHero = null;
        _heroHealth = null;
    }

    private void Update()
    {
        if (!_isInitialized)
            return;

        bool usesExitPhase = _phaseTwoExitPath != null;
        if (_endingStageId != 0 || _pendingStageResult.HasValue ||
            (usesExitPhase ? !_waveController.HasBegunCombat : !_waveController.IsWaveRunning) ||
            _waveController.StageId <= 0 ||
            _playerScene.CurrentStageId != _waveController.StageId ||
            !_playerScene.IsStageReady ||
            _playerScene.IsEndingStage)
        {
            return;
        }

        bool isWaveRunning = _waveController.IsWaveRunning;
        if (isWaveRunning || _waveController.HasActivePhaseBoss)
        {
            _elapsedCombatTime += Time.deltaTime;

            if (RemainingCombatTime <= 0f)
            {
                RequestDefeat("combat_time_expired", "제한시간 초과");
                return;
            }
        }

        if (_pendingExitPhaseReason != null && (_statueReveal == null || _statueReveal.IsReady))
        {
            string reason = _pendingExitPhaseReason;
            _pendingExitPhaseReason = null;
            CompleteExitPhase(reason);
        }
        else if (usesExitPhase && !_exitPhaseRequested &&
            (!_defenseGoal.IsAlive ||
             (isWaveRunning && _waveController.IsPhaseOneTimeoutWaveReached &&
              _elapsedCombatTime >= _phaseOneTimeLimit)))
        {
            TryBeginExitPhase(!_defenseGoal.IsAlive);
        }
    }

    private void TryBeginExitPhase(bool goalDestroyed)
    {
        _exitPhaseRequested = true;
        string reason = goalDestroyed
            ? (_statueReveal != null ? "방어 목표 파괴" : "상자 파괴")
            : "1차 제한 시간 경과";
        if (_phaseTwoExitPath.WaypointCount > 0 &&
            _statueReveal != null && _statueReveal.TryBegin())
        {
            _pendingExitPhaseReason = reason;
            return;
        }

        CompleteExitPhase(reason);
    }

    private void CompleteExitPhase(string reason)
    {
        if (_phaseTwoExitPath.WaypointCount == 0 || !_waveController.TryStartPhaseTwo())
        {
            if (_statueReveal != null)
                _statueReveal.Cancel();
            Debug.LogError("Stage 3의 2차 출구 경로와 일반·보스 스포너 설정을 확인하세요.", this);
            return;
        }

        _phaseOneGoalHp = _defenseGoal.CurrentHp;
        _phaseOneGoalMaxHp = _defenseGoal.MaxHp;
        _phaseOneGoalWasDamaged = _defenseGoal.HasTakenDamage || _phaseOneGoalHp <= 0f;
        _hasEnteredExitPhase = true;

        if (_statueReveal != null)
            _statueReveal.Break();
        int redirectedCount = _waveController.RedirectEnemiesToExit(_defenseGoal, _phaseTwoExitPath);
        Debug.Log($"Stage 3 2차 전환: {reason}, 생존 일반 적 {redirectedCount}마리 출구로 전환", this);
    }

#if UNITY_EDITOR
    [ContextMenu("테스트/석상 파괴 및 보스 등장")]
    private void TestStatueDestruction()
    {
        if (!Application.isPlaying || gameObject.scene.name != "KMBStage3Test" ||
            !isActiveAndEnabled || !_isInitialized || _statueReveal == null)
        {
            Debug.LogWarning("석상 파괴 테스트는 KMBStage3Test의 Play 모드에서만 사용할 수 있습니다.", this);
            return;
        }

        if (!_playerScene.IsStageReady || _playerScene.IsEndingStage || _endingStageId != 0 ||
            _pendingStageResult.HasValue || _exitPhaseRequested || !_defenseGoal.IsAlive)
        {
            Debug.LogWarning("Stage 3 도전을 시작하고 석상이 살아 있는 1차 구간에서 실행하세요.", this);
            return;
        }

        if (!_waveController.HasBegunCombat && !_waveController.TryStartWave())
        {
            Debug.LogWarning("첫 웨이브를 시작한 뒤 석상 파괴 테스트를 다시 실행하세요.", this);
            return;
        }

        // 실제 공격과 같은 피해 경로에서 HP를 0으로 만들어 기존 연출·보스 스폰을 검증합니다.
        var damage = new DamageInfo(gameObject, gameObject, ActorType.Enemy, Faction.Enemy,
            _defenseGoal.MaxHp, AttackType.BasicAttack, 0);
        if (!_defenseGoal.TryGetComponent(out DamageReceiver receiver) ||
            receiver.TakeDamage(damage) <= 0f)
        {
            Debug.LogWarning("석상 피해가 적용되지 않았습니다. 방어 목표와 무적 상태를 확인하세요.", this);
        }
    }
#endif

    private void CancelPendingExitPhase()
    {
        if (_pendingExitPhaseReason == null)
            return;

        _pendingExitPhaseReason = null;
        _exitPhaseRequested = false;
        if (_statueReveal != null)
            _statueReveal.Cancel();
    }

    private void HandlePassedCountChanged(int passedCount)
    {
        if (passedCount >= _stageData.PassLimit)
            RequestDefeat("pass_limit_reached", "적 통과 한도 도달");
    }

    private void HandleGoalDepleted()
    {
        if (_phaseTwoExitPath == null)
        {
            RequestDefeat("defense_goal_destroyed", "방어 목표 파괴");
        }
    }

    private void HandlePlayerReviveExhausted(DamageInfo damageInfo)
    {
        RequestDefeat("player_revive_exhausted", "플레이어 부활 횟수 소진");
    }

    private void HandleAllWavesCompleted()
    {
        if (_phaseTwoExitPath == null)
        {
            TryRequestVictory();
        }
    }

    private void TryRequestVictory()
    {
        int stageId = _waveController.StageId;
        if (_endingStageId != 0 || _pendingStageResult.HasValue || stageId <= 0 ||
            _playerScene.CurrentStageId != stageId ||
            !_playerScene.IsStageReady ||
            _playerScene.IsEndingStage)
        {
            return;
        }

        if (!_hasEnteredExitPhase && !_defenseGoal.IsAlive)
        {
            HandleGoalDepleted();
            return;
        }

        int passedCount = _waveController.PassedEnemyCount;
        if (passedCount >= _stageData.PassLimit)
        {
            HandlePassedCountChanged(passedCount);
            return;
        }

        int killCount = _waveController.TeamKillCount;
        float currentHp = _hasEnteredExitPhase ? _phaseOneGoalHp : _defenseGoal.CurrentHp;
        float maxHp = _hasEnteredExitPhase ? _phaseOneGoalMaxHp : _defenseGoal.MaxHp;
        bool targetWasDamaged = _hasEnteredExitPhase ? _phaseOneGoalWasDamaged : _defenseGoal.HasTakenDamage;

        if (!_scoreData.TryCalculateStage3ClearScore(
                killCount, passedCount, currentHp, maxHp, out int score))
        {
            Debug.LogError("Stage3 클리어 점수 계산에 실패했습니다.", this);
            return;
        }

        var enemies = new HashSet<EnemyData>();
        for (int waveIndex = 0; waveIndex < _stageData.WaveCount; waveIndex++)
        {
            WaveData wave = _stageData.GetWave(waveIndex);
            for (int groupIndex = 0; groupIndex < wave.SpawnGroupCount; groupIndex++)
            {
                EnemyCombat combat = wave.GetSpawnGroup(groupIndex)
                    .EnemyPrefab.GetComponent<EnemyCombat>();

                if (combat == null || combat.Data == null)
                {
                    Debug.LogError(
                        $"Stage3 도감 데이터 누락: 웨이브 {waveIndex + 1}, 그룹 {groupIndex + 1}",
                        this);
                    return;
                }

                enemies.Add(combat.Data);
            }
        }

        if (_hasEnteredExitPhase)
        {
            enemies.Add(_waveController.PhaseBossData);
        }

        int stars;
        if (_hasEnteredExitPhase && (passedCount > 0 || currentHp <= 0f))
            stars = 1;
        else if (passedCount == 0 && !targetWasDamaged)
            stars = 3;
        else if (_hasEnteredExitPhase || (double)currentHp * 2 >= maxHp)
            stars = 2;
        else
            stars = 1;

        var result = new MissionStageResult(
            stageId,
            MissionOutcome.Victory,
            passedCount,
            targetWasDamaged: targetWasDamaged,
            defenseRecordValid: true,
            reasonId: "stage_clear",
            starCount: stars);

        _pendingStageResult = result;
        _pendingVictoryEnemies = new List<EnemyData>(enemies);
        _clearScore = score;
        if (!TryReserveStageEnd(result, _pendingVictoryEnemies))
        {
            return;
        }

        Debug.Log(
            $"Stage3 승리 접수: 처치 {killCount}, 통과 {passedCount}, " +
            $"HP {currentHp:F1}/{maxHp:F1}, 별 {stars}개, {score}점",
            this);
    }

    private void RequestDefeat(string reasonId, string reason)
    {
        int stageId = _waveController.StageId;
        if (_endingStageId != 0 || _pendingStageResult.HasValue || stageId <= 0 ||
            _playerScene.CurrentStageId != stageId ||
            !_playerScene.IsStageReady ||
            _playerScene.IsEndingStage)
        {
            return;
        }

        var result = new MissionStageResult(
            stageId,
            MissionOutcome.Defeat,
            _waveController.PassedEnemyCount,
            targetWasDamaged: _hasEnteredExitPhase ? _phaseOneGoalWasDamaged : _defenseGoal.HasTakenDamage,
            defenseRecordValid: true,
            reasonId: reasonId,
            starCount: 0);

        _pendingStageResult = result;
        if (TryReserveStageEnd(result))
        {
            Debug.Log($"Stage3 실패: {reason}", this);
        }
    }

    public bool TryEndStage(int stageId)
    {
        Error = null;
        if (!_isInitialized || !isActiveAndEnabled || stageId <= 0 ||
            stageId != _waveController.StageId ||
            stageId != _playerScene.CurrentStageId ||
            !_playerScene.IsStageReady || _playerScene.IsEndingStage ||
            _endingStageId != 0)
        {
            Error = "현재 진행 중인 Stage 3 도전인지 확인하세요.";
            return false;
        }

        if (_pendingStageResult.HasValue)
        {
            return TryReserveStageEnd(_pendingStageResult.Value, _pendingVictoryEnemies);
        }

        var result = new MissionStageResult(
            stageId,
            MissionOutcome.Abandoned,
            _waveController.PassedEnemyCount,
            targetWasDamaged: _hasEnteredExitPhase ? _phaseOneGoalWasDamaged : _defenseGoal.HasTakenDamage,
            defenseRecordValid: true,
            reasonId: "stage_abandoned",
            starCount: 0);

        return TryReserveStageEnd(result);
    }

    private bool TryReserveStageEnd(MissionStageResult result, List<EnemyData> victoryEnemies = null)
    {
        bool accepted = result.Outcome == MissionOutcome.Victory
            ? _playerScene.RequestEndStage(result, false, victoryEnemies)
            : _playerScene.RequestEndStage(result, false);

        if (!accepted)
        {
            Error = $"Stage 3 종료 예약이 거절됐습니다. 사유 {result.ReasonId}";
            Debug.LogError(Error, this);
            return false;
        }

        Error = null;
        _pendingStageResult = null;
        _pendingVictoryEnemies = null;
        _endingStageId = result.StageId;
        _endRequestFrame = Time.frameCount;
        _waitingForEndEvents = true;

        CancelPendingExitPhase();
        _waveController.StopForStageResult();
        return true;
    }

    private void LateUpdate()
    {
        if (!_isInitialized)
            return;

        // 마지막 적과 Hero가 함께 쓰러진 경우 부활 소진 판정을 기다립니다.
        if (_endingStageId == 0 && !_pendingStageResult.HasValue &&
            !_hasTriedExitPhaseVictory && _hasEnteredExitPhase &&
            _playerScene.IsStageReady && !_playerScene.IsEndingStage &&
            _playerScene.CurrentStageId == _waveController.StageId &&
            _waveController.HasCompletedAllWaves && _waveController.ActiveEnemyCount == 0 &&
            _waveController.HasDefeatedPhaseBoss &&
            _heroHealth != null && (_heroHealth.IsAlive || _subscribedHero.IsWaitingRevive))
        {
            _hasTriedExitPhaseVictory = true;
            TryRequestVictory();
        }

        if (!_endEventsRetryRequired && CanCompleteEndEvents())
        {
            TryCompleteEndEvents(_endingStageId);
        }
    }

    private bool CanCompleteEndEvents()
    {
        return _waitingForEndEvents &&
            isActiveAndEnabled &&
            _playerScene != null &&
            _playerScene.isActiveAndEnabled &&
            _endingStageId > 0 &&
            _playerScene.CurrentStageId == _endingStageId &&
            _playerScene.IsAwaitingEndEvents &&
            Time.frameCount > _endRequestFrame &&
            _waveController != null &&
            _waveController.HasStoppedForStageResult &&
            !_waveController.IsDispatchingEnemyDeath &&
            _playerScene.Missions != null &&
            !_playerScene.Missions.IsProcessing;
    }

    public bool TryCompleteEndEvents(int stageId)
    {
        Error = null;
        if (stageId <= 0 || stageId != _endingStageId || !CanCompleteEndEvents())
        {
            Error = "Stage 3 마지막 사건을 전달할 수 있는 상태인지 확인하세요.";
            return false;
        }

        bool accepted = _clearScore.HasValue
            ? _playerScene.CompleteEndEvents(stageId, _clearScore.Value)
            : _playerScene.CompleteEndEvents(stageId);

        if (!accepted)
        {
            _endEventsRetryRequired = true;
            Error = $"Stage 3 마지막 사건 전달이 거절됐습니다. 도전 {stageId}";
            Debug.LogError(Error, this);
            return false;
        }

        _waitingForEndEvents = false;
        _endEventsRetryRequired = false;
        Debug.Log(
            $"Stage 3 마지막 사건 전달 완료 접수. 도전 {stageId}",
            this);
        return true;
    }

    [ContextMenu("테스트/Stage 3 마지막 사건 전달 재시도")]
    private void RetryEndEventsForTest()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (!_endEventsRetryRequired || !CanCompleteEndEvents())
        {
            Debug.LogWarning(
                "Stage 3 마지막 사건 전달을 재시도할 수 있는 상태인지 확인하세요.",
                this);
            return;
        }

        TryCompleteEndEvents(_endingStageId);
    }
}
