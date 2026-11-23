using System;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(EnemyCombat))]
[RequireComponent(typeof(EnemyMovement))]
[RequireComponent(typeof(EnemyPlayerDetector))]
[RequireComponent(typeof(HitDetector))]
public class EnemyAttack : MonoBehaviour
{
    [SerializeField] private LayerMask _playerMask;

    [SerializeField] private EnemyAnimationEvents _animationEvents;

    private bool _isAttacking;
    private bool _hasCommittedHit;
    private int _currentAttackId;
    private Behaviour _attackTarget;
    private Behaviour _reactionTarget;
    private readonly List<IDamageable> _hitTargets = new List<IDamageable>();

    // 현재 공격이 방어 목표 공격인지 구분
    private bool _isDefenseGoalAttack;

    // 공격 시작 시 확정한 방어 목표
    private Stage2DefenseGoal _attackDefenseGoal;

    public bool IsAttacking => _isAttacking ||
        (AreaSlam != null && AreaSlam.IsPlaying);

    internal MinotosAreaSlam AreaSlam { get; private set; }

    private EnemyCombat _enemyCombat;
    private EnemyMovement _enemyMovement;
    private EnemyPlayerDetector _playerDetector;
    private HitDetector _hitDetector;
    private StatusEffectController _statusEffects;
    private KnockbackController _knockback;

    private float _damage;
    private float _attackInterval;
    private float _attackRange;
    private float _attackAngle;
    private float _remainingAttackTime;

    private bool _isInitialized;

    // 피해 성공 여부와 관계없이 타격 기회를 사용했을 때 알림
    public event Action OnAttackOpportunityUsed;

    public float AttackRange => _attackRange;

    internal bool HasTargetInAttackRange(Behaviour target) =>
        isActiveAndEnabled &&
        _isInitialized &&
        TryGetAttackTarget(target, out _);

    // 이동 중에도 조회할 수 있도록 정지 상태와 별도로 거리 검사
    public bool HasDefenseGoalInAttackRange
    {
        get
        {
            if (!_isInitialized)
            {
                return false;
            }

            Stage2DefenseGoal goal = _enemyMovement.DefenseGoal;

            return goal != null &&
                goal.CanReceiveDamage &&
                Vector3.Distance(
                    transform.position,
                    goal.transform.position) <= _attackRange;
        }
    }

    [ContextMenu("테스트/공격 정지 원인 확인")]
    private void PrintAttackStateForTest()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        Behaviour target = _reactionTarget != null ? _reactionTarget : _playerDetector.Player;
        bool hasAttackTarget = TryGetAttackTarget(target, out _);

        bool canStartAnimation =
            _animationEvents != null &&
            _animationEvents.CanStartAttack;

        Debug.Log(
            $"공격 상태 확인: 대상 {name}\n" +
            $"컴포넌트 활성: {isActiveAndEnabled}, " +
            $"초기화: {_isInitialized}\n" +
            $"반격 대상: {_reactionTarget?.name}, " +
            $"공격 조건 충족: {CanReact()}\n" +
            $"공격 진행 중: {_isAttacking}, " +
            $"타격 기회 사용: {_hasCommittedHit}\n" +
            $"공격 애니메이션 시작 가능: {canStartAnimation}\n" +
            $"공격 대상 탐색 성공: {hasAttackTarget}\n" +
            $"플레이어 거리: {_playerDetector.CurrentDistance:F3}, " +
            $"공격 거리: {_attackRange:F3}\n" +
            $"남은 공격 간격: {_remainingAttackTime:F3}, " +
            $"Player Mask: {_playerMask.value}",
            this);
    }

    private void Awake()
    {
        _enemyCombat = GetComponent<EnemyCombat>();
        _enemyMovement = GetComponent<EnemyMovement>();
        _playerDetector = GetComponent<EnemyPlayerDetector>();
        _hitDetector = GetComponent<HitDetector>();
        AreaSlam = GetComponentInChildren<MinotosAreaSlam>();
        _statusEffects = GetComponent<StatusEffectController>();
        _knockback = GetComponent<KnockbackController>();

        if (_playerMask.value == 0 || _animationEvents == null)
        {
            Debug.LogError(
                "Player Mask와 Animation Events를 연결하세요.",
                this);

            enabled = false;
        }
    }

    public bool TryInitialize(AttackData data, float attackMultiplier = 1f)
    {
        if (!isActiveAndEnabled || _isInitialized || data == null)
        {
            return false;
        }

        float damage = data.RawDamage * attackMultiplier;
        bool hasValidValues =
            IsPositiveFinite(attackMultiplier) &&
            IsPositiveFinite(damage) &&
            IsPositiveFinite(data.AttackInterval) &&
            IsPositiveFinite(data.Radius) &&
            data.Angle >= 0f &&
            data.Angle <= 360f;

        if (!hasValidValues)
        {
            Debug.LogError("적의 공격 설정값을 확인하세요.", this);
            return false;
        }

        if (AreaSlam != null && !AreaSlam.TrySetAttackMultiplier(attackMultiplier))
        {
            return false;
        }

        _damage = damage;
        _attackInterval = data.AttackInterval;
        _attackRange = data.Radius;
        _attackAngle = data.Angle;

        _isInitialized = true;
        return true;
    }

    // EnemyReaction이 이동 상태를 적용한 뒤 한 번 호출
    public void UpdateAttack(float deltaTime)
    {
        if (!isActiveAndEnabled || !_isInitialized)
        {
            return;
        }

        if (!_enemyCombat.CanReceiveDamage)
        {
            CancelAttack();
            return;
        }

        if (!IsPositiveFinite(deltaTime))
        {
            return;
        }

        _remainingAttackTime = Mathf.Max(
            0f,
            _remainingAttackTime - deltaTime);

        // 방어 목표에 도착한 적은 플레이어 반격 대신 목표 공격
        if (_enemyMovement.HasReachedDefenseGoal)
        {
            UpdateDefenseGoalAttack();
            return;
        }

        if (!CanReact())
        {
            CancelAttack();
            return;
        }

        if (_isAttacking)
        {
            if (!IsTargetAlive(_attackTarget) || _reactionTarget != _attackTarget)
            {
                CancelAttack();
            }

            return;
        }

        if (_remainingAttackTime > 0f ||
            !_animationEvents.CanStartAttack)
        {
            return;
        }

        if (!TryGetAttackTarget(_reactionTarget, out _))
        {
            return;
        }

        _currentAttackId++;
        _attackTarget = _reactionTarget;
        _hasCommittedHit = false;
        _isAttacking = true;

        if (!_animationEvents.TryPlayAttack(_currentAttackId))
        {
            CancelAttack();
            return;
        }

        // 취소되어도 공격 간격은 유지
        _remainingAttackTime = _attackInterval;

        Debug.Log($"적 공격 시작: 회차 {_currentAttackId}, 대상 {_attackTarget.name}", this);
    }

    private bool IsPositiveFinite(float value)
    {
        return value > 0f &&
            !float.IsInfinity(value);
    }

    private bool CanReact()
    {
        return IsTargetAlive(_reactionTarget) &&
            !_statusEffects.IsStunned &&
            !_knockback.IsKnockback &&
            _enemyMovement.isActiveAndEnabled &&
            _enemyMovement.IsMovementPaused;
    }

    internal static bool IsTargetAlive(Behaviour target)
    {
        return target != null &&
            target.isActiveAndEnabled &&
            target.TryGetComponent(out DamageReceiver receiver) &&
            receiver.isActiveAndEnabled &&
            receiver.Faction == Faction.Ally &&
            receiver.IsAlive;
    }

    private bool TryGetAttackTarget(
        Behaviour target,
        out DamageReceiver receiver)
    {
        receiver = null;

        if (!IsTargetAlive(target))
        {
            return false;
        }

        float distance = Vector3.Distance(
            transform.position,
            target.transform.position);

        if (distance > _attackRange)
        {
            return false;
        }

        receiver = target.GetComponent<DamageReceiver>();
        // 동료의 몸통 콜라이더는 루트와 같은 레이어를 사용
        LayerMask targetMask = target is Hero ? _playerMask : (LayerMask)(1 << target.gameObject.layer);

        _hitDetector.Detect(
            transform.position,
            _attackRange,
            transform.forward,
            _attackAngle,
            targetMask,
            _hitTargets);

        return _hitTargets.Contains(receiver);
    }

    // 애니메이션이 시작할 때 기록한 회차로 요청
    public bool TryCommitHit(int attackId)
    {
        bool canCommit =
            isActiveAndEnabled &&
            _isAttacking &&
            attackId == _currentAttackId &&
            !_hasCommittedHit &&
            _enemyCombat.CanReceiveDamage;

        if (!canCommit)
        {
            return false;
        }

        // 빗나가도 이번 타격 기회는 소비
        _hasCommittedHit = true;

        // 목표 공격은 플레이어 반격 횟수를 소비하지 않음
        if (_isDefenseGoalAttack)
        {
            return TryCommitDefenseGoalHit(attackId);
        }

        OnAttackOpportunityUsed?.Invoke();

        if (!CanReact() ||
            !TryGetAttackTarget(
                _attackTarget, out DamageReceiver receiver))
        {
            Debug.Log($"적 공격 빗나감: 회차 {attackId}", this);
            return false;
        }

        DamageInfo damageInfo = new DamageInfo(
            gameObject,
            gameObject,
            ActorType.Enemy,
            Faction.Enemy,
            _damage,
            AttackType.BasicAttack,
            attackId);

        string targetName = receiver.name;
        float appliedDamage = receiver.TakeDamage(damageInfo);

        Debug.Log(
            $"적 타격: 회차 {attackId}, 대상 {targetName}, 실제 피해 {appliedDamage}",
            this);

        return appliedDamage > 0f;
    }

    public bool CompleteAttack(int attackId)
    {
        if (!_isAttacking || attackId != _currentAttackId)
        {
            return false;
        }

        CancelAttack();
        Debug.Log($"적 공격 종료: 회차 {attackId}", this);
        return true;
    }

    public void CancelAttack()
    {
        if (AreaSlam != null)
        {
            AreaSlam.Cancel();
        }

        if (_animationEvents != null)
        {
            _animationEvents.CancelPlayback(_currentAttackId);
        }

        _isAttacking = false;
        _hasCommittedHit = false;
        _attackTarget = null;
        _isDefenseGoalAttack = false;
        _attackDefenseGoal = null;
    }

    private void OnDisable()
    {
        CancelAttack();
    }

    // 반격 상태를 소유하는 EnemyReaction에서 호출
    internal void SetReactionTarget(Behaviour target)
    {
        _reactionTarget = target;

        if (target == null && _isAttacking)
        {
            CancelAttack();
        }
    }

    private bool CanAttackDefenseGoal()
    {
        Stage2DefenseGoal goal = _enemyMovement.DefenseGoal;

        return isActiveAndEnabled &&
            _isInitialized &&
            _enemyCombat.CanReceiveDamage &&
            _enemyMovement.isActiveAndEnabled &&
            _enemyMovement.HasReachedDefenseGoal &&
            _enemyMovement.IsMovementPaused &&
            !_statusEffects.IsStunned &&
            !_knockback.IsKnockback &&
            goal != null &&
            goal.CanReceiveDamage;
    }

    private bool TryGetDefenseGoalTarget(
        Stage2DefenseGoal goal,
        out DamageReceiver receiver)
    {
        receiver = null;

        if (goal == null || !goal.CanReceiveDamage)
        {
            return false;
        }

        Vector3 offset =
            goal.transform.position - transform.position;

        if (offset.magnitude > _attackRange)
        {
            return false;
        }

        // 목표를 향하는 수평 공격 각도 검사
        offset.y = 0f;

        if (offset.sqrMagnitude > 0.0001f &&
            Vector3.Angle(transform.forward, offset) > _attackAngle * 0.5f)
        {
            return false;
        }

        receiver = goal.Receiver;
        return receiver != null && receiver.isActiveAndEnabled;
    }

    private void UpdateDefenseGoalAttack()
    {
        if (!CanAttackDefenseGoal())
        {
            CancelAttack();
            return;
        }

        Stage2DefenseGoal goal = _enemyMovement.DefenseGoal;

        if (_isAttacking)
        {
            // 다른 공격 또는 다른 목표로 시작한 공격은 취소
            if (!_isDefenseGoalAttack || _attackDefenseGoal != goal)
            {
                CancelAttack();
            }

            return;
        }

        if (_remainingAttackTime > 0f ||
            !_animationEvents.CanStartAttack)
        {
            return;
        }

        if (!TryGetDefenseGoalTarget(goal, out _))
        {
            return;
        }

        _currentAttackId++;
        _attackTarget = null;
        _attackDefenseGoal = goal;
        _isDefenseGoalAttack = true;
        _hasCommittedHit = false;
        _isAttacking = true;

        if (!_animationEvents.TryPlayAttack(_currentAttackId))
        {
            CancelAttack();
            return;
        }

        // 공격이 취소돼도 사용한 공격 간격은 유지
        _remainingAttackTime = _attackInterval;

        Debug.Log(
            $"방어 목표 공격 시작: 회차 {_currentAttackId}",
            this);
    }

    private bool TryCommitDefenseGoalHit(int attackId)
    {
        bool hasValidTarget =
            CanAttackDefenseGoal() &&
            _attackDefenseGoal == _enemyMovement.DefenseGoal;

        if (!hasValidTarget ||
            !TryGetDefenseGoalTarget(
                _attackDefenseGoal,
                out DamageReceiver receiver))
        {
            Debug.Log(
                $"방어 목표 공격 빗나감: 회차 {attackId}",
                this);

            return false;
        }

        DamageInfo damageInfo = new DamageInfo(
            gameObject,
            gameObject,
            ActorType.Enemy,
            Faction.Enemy,
            _damage,
            AttackType.BasicAttack,
            attackId);

        float appliedDamage = receiver.TakeDamage(damageInfo);

        Debug.Log(
            $"방어 목표 타격: 회차 {attackId}, " +
            $"실제 피해 {appliedDamage:F1}",
            this);

        return appliedDamage > 0f;
    }
}
