using UnityEngine;

[RequireComponent(typeof(EnemyMovement))]
[RequireComponent(typeof(EnemyCombat))]
[RequireComponent(typeof(EnemyPlayerDetector))]
[RequireComponent(typeof(EnemyAttack))]
public class EnemyReaction : MonoBehaviour
{
    private float _reactionRotationSpeed;
    private bool _isInitialized;

    private EnemyMovement _enemyMovement;
    private EnemyCombat _enemyCombat;
    private EnemyPlayerDetector _playerDetector;
    private EnemyAttack _enemyAttack;
    private DamageReceiver _damageReceiver;
    private Companion _companionAttacker;
    private Behaviour _reactionTarget;

    // 반격 또는 기절로 이동을 멈췄는지 기록
    private bool _hasPausedMovement;

    private StatusEffectController _statusEffects;

    private KnockbackController _knockback;

    // 넉백 종료 프레임에는 일반 회전·공격을 생략
    private bool _skipActionsOnce;

    private EnemyReactionMode _reactionMode;
    private bool _pursuePlayerContinuously;
    private int _reactionAttackCount;
    private float _reactionCooldown;
    private float _reactionStartDistance;
    private float _reactionEndDistance;

    private int _usedReactionAttackCount;

    private bool _isReactionLocked;
    private bool _hasLeftReactionRange;
    private double _reactionReadyTime;

    // 대기 조건 충족 후 새로 접근했는지 검사
    private float _previousTargetDistance = float.PositiveInfinity;

    // 최초 접근으로 시작된 반격을 횟수 완료까지 유지
    private bool _isReacting;

    private void Awake()
    {
        _enemyMovement = GetComponent<EnemyMovement>();
        _enemyCombat = GetComponent<EnemyCombat>();
        _playerDetector = GetComponent<EnemyPlayerDetector>();
        _enemyAttack = GetComponent<EnemyAttack>();
        _damageReceiver = GetComponent<DamageReceiver>();
        _statusEffects = GetComponent<StatusEffectController>();
        _knockback = GetComponent<KnockbackController>();
    }

    private void LateUpdate()
    {
        if (!_isInitialized)
        {
            return;
        }

        if (!_enemyCombat.CanReceiveDamage)
        {
            _enemyAttack.SetReactionTarget(null);
            _hasPausedMovement = false;
            _skipActionsOnce = false;
            return;
        }

        if (!_enemyMovement.isActiveAndEnabled)
        {
            _enemyAttack.CancelAttack();
            _enemyAttack.SetReactionTarget(null);
            return;
        }

        if (TryRunAreaSlam())
        {
            return;
        }

        // 방어 목표 도착 후에는 플레이어 반격·추격 대신 목표 앞에 머무름
        // 다음 단계에서 이 분기에 목표 공격 갱신을 연결
        if (_enemyMovement.HasReachedDefenseGoal)
        {
            UpdateDefenseGoalReaction();
            return;
        }

        bool isStunned = _statusEffects.IsStunned;
        bool isKnockback = _knockback.IsKnockback;

        SelectReactionTarget();
        bool hasAliveTarget = EnemyAttack.IsTargetAlive(_reactionTarget);
        bool hasValidTarget = hasAliveTarget &&
            (_enemyAttack.IsAttacking || CanEngage(_reactionTarget));

        if (!hasValidTarget)
        {
            _enemyAttack.SetReactionTarget(null);
        }

        float distance = hasAliveTarget
            ? Vector3.Distance(transform.position, _reactionTarget.transform.position)
            : float.PositiveInfinity;
        bool canReact = UpdateReactionPermission(hasAliveTarget, hasValidTarget, distance);

        // 반격을 시작할 때만 접근 거리 검사
        bool canBeginReaction =
            !_isReacting &&
            canReact &&
            hasValidTarget &&
            !isStunned &&
            !isKnockback &&
            !_skipActionsOnce &&
            (_pursuePlayerContinuously || distance <= _reactionStartDistance);

        if (canBeginReaction)
        {
            _isReacting = true;
            if (_companionAttacker == _reactionTarget)
            {
                _companionAttacker = null;
            }

            Debug.Log($"반격 시작: 대상 {_reactionTarget.name}", this);
        }

        bool hasReactionTarget = _isReacting && hasValidTarget;

        _enemyAttack.SetReactionTarget(hasReactionTarget ? _reactionTarget : null);

        Transform chaseTarget = hasReactionTarget
            ? _reactionTarget.transform
            : null;

        _enemyMovement.SetChaseTarget(chaseTarget);

        bool isInAttackRange =
            hasReactionTarget &&
            distance <= _enemyAttack.AttackRange;

        // 공격 중에는 대상이 멀어져도 동작을 마침
        // 타격 시점의 거리 검사가 빗나감을 결정
        bool shouldPauseMovement =
            isStunned ||
            isKnockback ||
            _skipActionsOnce ||
            (hasReactionTarget &&
             (isInAttackRange || _enemyAttack.IsAttacking));

        if (!TryApplyMovementPause(shouldPauseMovement))
        {
            return;
        }

        if (_skipActionsOnce)
        {
            _skipActionsOnce = false;
            return;
        }

        if (!isStunned &&
            !isKnockback &&
            hasReactionTarget &&
            shouldPauseMovement)
        {
            FaceTarget(_reactionTarget.transform, Time.deltaTime);
        }

        _enemyAttack.UpdateAttack(Time.deltaTime);
    }

    private bool TryRunAreaSlam()
    {
        MinotosAreaSlam areaSlam = _enemyAttack.AreaSlam;
        if (areaSlam == null)
        {
            return false;
        }

        if (areaSlam.IsPlaying)
        {
            return true;
        }

        if (!areaSlam.IsQueued || _statusEffects.IsStunned || _knockback.IsKnockback ||
            _skipActionsOnce || !_enemyMovement.TrySetMovementPaused(true))
        {
            return false;
        }

        _hasPausedMovement = true;
        _enemyAttack.CancelAttack();
        return areaSlam.TryStart();
    }

    private void OnDisable()
    {
        _damageReceiver.OnDamaged -= HandleDamaged;
        _companionAttacker = null;
        _reactionTarget = null;
        if (_statusEffects != null)
        {
            _statusEffects.OnEffectChanged -= HandleEffectChanged;
        }

        if (_knockback != null)
        {
            _knockback.OnKnockbackChanged -= HandleKnockbackChanged;
            _knockback.CancelKnockback();
        }

        _skipActionsOnce = false;

        if (_enemyMovement != null)
        {
            _enemyMovement.SetChaseTarget(null);
        }

        _isReacting = false;

        if (_enemyAttack != null)
        {
            _enemyAttack.OnAttackOpportunityUsed -=
                HandleAttackOpportunityUsed;

            _enemyAttack.CancelAttack();
            _enemyAttack.SetReactionTarget(null);
        }

        bool canResume =
            _hasPausedMovement &&
            _enemyMovement != null &&
            _enemyCombat != null &&
            _statusEffects != null &&
            _knockback != null &&
            !_knockback.IsKnockback &&
            !_statusEffects.IsStunned &&
            _enemyCombat.CanReceiveDamage &&
            _enemyMovement.isActiveAndEnabled;

        if (canResume &&
            _enemyMovement.TrySetMovementPaused(false))
        {
            _hasPausedMovement = false;
        }
    }

    public bool TryInitialize(EnemyData data, bool pursuePlayerContinuously = false)
    {
        if (!isActiveAndEnabled ||
            _isInitialized ||
            data == null ||
            !data.IsValid)
        {
            return false;
        }

        _reactionRotationSpeed = data.ReactionRotationSpeed;
        _pursuePlayerContinuously = pursuePlayerContinuously;
        _reactionMode = pursuePlayerContinuously
            ? EnemyReactionMode.Continuous
            : data.ReactionMode;
        _reactionAttackCount = data.ReactionAttackCount;
        _reactionCooldown = data.ReactionCooldown;
        _reactionStartDistance = data.ReactionStartDistance;
        _reactionEndDistance = data.ReactionEndDistance;
        _isInitialized = true;

        return true;
    }

    private void SelectReactionTarget()
    {
        if (_enemyAttack.IsAttacking)
        {
            return;
        }

        bool hasCompanionTarget = _reactionTarget is Companion &&
            EnemyAttack.IsTargetAlive(_reactionTarget);
        if (hasCompanionTarget && (_isReacting || _isReactionLocked))
        {
            return;
        }

        // 사망한 대상은 기존 반격의 종료를 처리한 뒤 교체
        if (_isReacting && !EnemyAttack.IsTargetAlive(_reactionTarget))
        {
            return;
        }

        Behaviour target = _playerDetector.isActiveAndEnabled ? _playerDetector.Player : null;
        bool canSwitchTarget = !_isReacting ||
            _reactionMode == EnemyReactionMode.Continuous ||
            _usedReactionAttackCount < _reactionAttackCount;
        if (!_isReactionLocked && canSwitchTarget &&
            EnemyAttack.IsTargetAlive(_companionAttacker) &&
            Vector3.Distance(transform.position, _companionAttacker.transform.position) <=
                _reactionStartDistance &&
            CanEngage(_companionAttacker))
        {
            target = _companionAttacker;
            if (_isReacting)
            {
                _companionAttacker = null;
            }
        }

        if (_reactionTarget != target)
        {
            _reactionTarget = target;
            _previousTargetDistance = float.PositiveInfinity;
        }
    }

    private bool CanEngage(Behaviour target) =>
        _enemyMovement.IsFlying ||
        _enemyMovement.CanChaseTarget(target.transform) ||
        _enemyAttack.HasTargetInAttackRange(target);

    private void FaceTarget(Transform target, float deltaTime)
    {
        if (_reactionRotationSpeed <= 0f || float.IsInfinity(_reactionRotationSpeed))
        {
            return;
        }

        Vector3 direction = target.position - transform.position;

        // 높이 차이로 몸이 기울지 않도록 수평 방향만 사용
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.0001f)
        {
            return;
        }

        Quaternion targetRotation =
            Quaternion.LookRotation(direction, Vector3.up);

        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            targetRotation,
            _reactionRotationSpeed * deltaTime);
    }

    private bool TryApplyMovementPause(bool shouldPause)
    {
        if (shouldPause == _hasPausedMovement)
        {
            return true;
        }

        if (!_enemyMovement.TrySetMovementPaused(shouldPause))
        {
            Debug.LogError(
                "반격·기절 상태에 따른 이동 정지·재개에 실패했습니다.",
                this);

            enabled = false;
            return false;
        }

        _hasPausedMovement = shouldPause;
        return true;
    }

    private void OnEnable()
    {
        _damageReceiver.OnDamaged += HandleDamaged;
        _statusEffects.OnEffectChanged += HandleEffectChanged;
        _knockback.OnKnockbackChanged += HandleKnockbackChanged;
        _enemyAttack.OnAttackOpportunityUsed += HandleAttackOpportunityUsed;
    }

    private void HandleDamaged(DamageInfo damageInfo, float appliedDamage)
    {
        if (_isInitialized &&
            _enemyCombat.CanReceiveDamage &&
            !_pursuePlayerContinuously &&
            _reactionMode != EnemyReactionMode.IgnorePlayer &&
            !_enemyMovement.HasReachedDefenseGoal &&
            appliedDamage > 0f &&
            damageInfo.ActorType == ActorType.Companion &&
            damageInfo.Faction == Faction.Ally &&
            damageInfo.Attacker != null &&
            damageInfo.Attacker.TryGetComponent(out Companion companion) &&
            companion != _reactionTarget)
        {
            _companionAttacker = companion;
        }
    }

    private void HandleEffectChanged(StatusEffectType type)
    {
        if (type != StatusEffectType.Stun ||
            !_statusEffects.IsStunned ||
            !_isInitialized ||
            !_enemyCombat.CanReceiveDamage)
        {
            return;
        }

        // 기절 적용 즉시 진행 공격 취소
        // 같은 프레임의 뒤늦은 타격 이벤트도 무효화
        _enemyAttack.CancelAttack();

        if (_enemyMovement.isActiveAndEnabled)
        {
            TryApplyMovementPause(true);
        }
    }

    private void HandleKnockbackChanged(bool isKnockback)
    {
        if (!_isInitialized || !_enemyCombat.CanReceiveDamage)
        {
            return;
        }

        if (isKnockback)
        {
            _enemyAttack.CancelAttack();

            if (_enemyMovement.isActiveAndEnabled)
            {
                TryApplyMovementPause(true);
            }

            return;
        }

        // 바로 이동을 풀지 않고 다음 행동 판정에서 결정
        _skipActionsOnce = true;
    }

    private void HandleAttackOpportunityUsed()
    {
        if (!_isInitialized ||
            _reactionMode != EnemyReactionMode.Limited)
        {
            return;
        }

        _usedReactionAttackCount++;

        Debug.Log(
            $"반격 타격 기회 사용: " +
            $"{_usedReactionAttackCount}/{_reactionAttackCount}",
            this);
    }

    private bool UpdateReactionPermission(bool hasAliveTarget, bool hasValidTarget, float distance)
    {
        bool hasEnteredStartRange =
            hasAliveTarget &&
            _previousTargetDistance > _reactionStartDistance &&
            distance <= _reactionStartDistance;

        _previousTargetDistance = distance;

        if (_reactionMode == EnemyReactionMode.IgnorePlayer)
        {
            _isReacting = false;
            return false;
        }

        if (_reactionMode == EnemyReactionMode.Continuous)
        {
            if (!hasValidTarget)
            {
                _isReacting = false;
            }

            return true;
        }

        // 횟수 완료 또는 대상 무효화로 반격 종료
        // 마지막 공격은 종료·취소된 이후에 복귀
        bool hasFinishedReaction =
            _isReacting &&
            !_isReactionLocked &&
            !_enemyAttack.IsAttacking &&
            (_usedReactionAttackCount >= _reactionAttackCount ||
             !hasValidTarget);

        if (hasFinishedReaction)
        {
            _isReacting = false;
            _isReactionLocked = true;
            _hasLeftReactionRange = _reactionTarget is Companion && !hasAliveTarget;

            _reactionReadyTime =
                Time.timeAsDouble + _reactionCooldown;

            Debug.Log(
                $"반격 종료: {_reactionCooldown}초 대기 및 " +
                $"{_reactionEndDistance}m 이탈 후 재접근 필요",
                this);
        }

        if (!_isReactionLocked)
        {
            return true;
        }

        if ((hasAliveTarget && distance >= _reactionEndDistance) ||
            (_reactionTarget is Companion && !hasAliveTarget))
        {
            _hasLeftReactionRange = true;
        }

        bool canRestart =
            Time.timeAsDouble >= _reactionReadyTime &&
            _hasLeftReactionRange &&
            hasEnteredStartRange;

        if (!canRestart)
        {
            return false;
        }

        _isReactionLocked = false;
        _usedReactionAttackCount = 0;

        Debug.Log("재반격 조건 충족", this);
        return true;
    }

    private void UpdateDefenseGoalReaction()
    {
        _isReacting = false;
        _reactionTarget = null;
        _companionAttacker = null;
        _enemyMovement.SetChaseTarget(null);

        Stage2DefenseGoal goal = _enemyMovement.DefenseGoal;

        bool hasValidGoal =
            goal != null &&
            goal.CanReceiveDamage;

        bool isStunned = _statusEffects.IsStunned;
        bool isKnockback = _knockback.IsKnockback;

        bool shouldPauseMovement =
            !hasValidGoal ||
            isStunned ||
            isKnockback ||
            _skipActionsOnce ||
            _enemyAttack.HasDefenseGoalInAttackRange ||
            _enemyAttack.IsAttacking;

        // 도착 시 이동부가 직접 정지했을 수 있으므로
        // 실제 이동 상태에 요청하고 반격 쪽 기록도 맞춤
        if (!_enemyMovement.TrySetMovementPaused(shouldPauseMovement))
        {
            _enemyAttack.CancelAttack();
            return;
        }

        _hasPausedMovement = shouldPauseMovement;

        if (_skipActionsOnce)
        {
            _skipActionsOnce = false;
            return;
        }

        if (hasValidGoal &&
            !isStunned &&
            !isKnockback &&
            shouldPauseMovement)
        {
            Vector3 direction =
                goal.transform.position - transform.position;

            direction.y = 0f;

            if (direction.sqrMagnitude > 0.0001f)
            {
                Quaternion targetRotation =
                    Quaternion.LookRotation(direction);

                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation,
                    targetRotation,
                    _reactionRotationSpeed * Time.deltaTime);
            }
        }

        _enemyAttack.UpdateAttack(Time.deltaTime);
    }
}
