using UnityEngine;

[RequireComponent(typeof(PlacedInstallation))]
public class ArrowTower : MonoBehaviour, IInstallationUpgradeTarget
{
    private const float ArrowReleaseTime = 1f / 30f;

    [SerializeField] private Transform _turretPivot;
    [SerializeField] private LayerMask _enemyMask;

    // 초당 회전 각도
    [SerializeField, Min(1f)]
    private float _rotationSpeed = 360f;

    [SerializeField] private Transform _firePoint;

    [SerializeField]
    private ArrowTowerAimProvider _aimProvider;

    [SerializeField]
    private ProjectileAttackExecutor _projectileExecutor;

    // 발사부가 목표 방향에 충분히 가까워진 뒤 발사
    [SerializeField, Min(0.1f)]
    private float _fireAngleTolerance = 5f;

    // 벽·바닥 등 화살을 막는 지형만 포함
    [SerializeField] private LayerMask _obstacleMask;

    // 공용 투사체 실행자와 같은 데이터 연결
    // 실제 화살의 충돌 반경으로 차폐 검사
    [SerializeField] private ProjectileData _projectileData;

    [SerializeField] private Transform _launchOrigin;
    [SerializeField] private Transform _modelFirePoint;
    [SerializeField] private Transform _modelLaunchOrigin;
    [SerializeField] private GameObject _modelAnimationRoot;
    [SerializeField] private AnimationClip _modelFireClip;
    [SerializeField] private AnimationClip _modelReloadClip;
    [SerializeField] private SkinnedMeshRenderer _loadedArrow;

    private Transform _modelTurretPivot;
    private Quaternion _modelAimOffset;
    private float _shotAnimationTime = -1f;
    private bool _hasReleasedArrow;

    private float _damage;
    private float _attackInterval;
    private float _remainingAttackTime;
    private int _attackId;

    private BuildPhaseGate _buildPhaseGate;
    private bool _isInitialized;
    private float _attackRange;

    private EnemyCombat _targetEnemy;
    private Collider _targetCollider;

    // 이 설치물은 피해 증가와 공격 주기 단축을 지원
    public InstallationUpgradeSupport SupportedUpgrades =>
        InstallationUpgradeSupport.Damage |
        InstallationUpgradeSupport.Interval;

    public bool TryInitialize(
    TrapData data,
    BuildPhaseGate buildPhaseGate,
    InstallationUpgradeStats upgradeStats)
    {
        PlacedInstallation installation =
            GetComponent<PlacedInstallation>();

        bool hasValidSettings =
            !_isInitialized &&
            isActiveAndEnabled &&
            installation.Area != null &&
            data != null &&
            data.IsValid &&
            data.AttackRange > 0f &&
            buildPhaseGate != null &&
            _turretPivot != null &&
            _turretPivot != transform &&
            _turretPivot.IsChildOf(transform) &&
            _enemyMask.value != 0 &&
            _rotationSpeed > 0f &&
            _firePoint != null &&
            _firePoint.IsChildOf(_turretPivot) &&
            _aimProvider != null &&
            _aimProvider.isActiveAndEnabled &&
            _projectileExecutor != null &&
            _projectileExecutor.isActiveAndEnabled &&
            data.Damage > 0f &&
            data.AttackInterval > 0f &&
            _fireAngleTolerance > 0f &&
            _obstacleMask.value != 0 &&
            _projectileData != null &&
            _projectileData.CollisionRadius > 0f &&
            _launchOrigin != null &&
            _launchOrigin.IsChildOf(_turretPivot);

        if (!hasValidSettings)
        {
            Debug.LogError(
                "화살 포탑의 사거리, 회전부, Enemy Mask와 초기화 순서를 확인하세요.",
                this);

            return false;
        }

        _buildPhaseGate = buildPhaseGate;
        _attackRange = data.AttackRange;

        // 원본 데이터는 유지하고 이번 도전의 강화 결과를 실행 값에 적용
        _damage =
            data.Damage * (1f + upgradeStats.DamageBonus);

        _attackInterval =
            data.AttackInterval * (1f - upgradeStats.IntervalReduction);
        _remainingAttackTime = 0f;

        var gameManager = GameManager.Instance;
        if (gameManager == null || gameManager.Vfx == null)
        {
            Debug.LogError("화살 포탑에 연결할 공용 VFX 서비스를 확인하세요.", this);
            return false;
        }

        if (!TryAlignLauncherModel())
        {
            Debug.LogError("화살 포탑 모델의 회전부와 발사 기준점을 확인하세요.", this);
            return false;
        }

        _projectileExecutor.SetSound(gameManager.Sound);
        _projectileExecutor.SetVfx(gameManager.Vfx);

        _isInitialized = true;

        Debug.Log(
            $"화살 발사기 강화 적용: 데이터 {data.name}, " +
            $"피해 {_damage:F1}, " +
            $"공격 간격 {_attackInterval:F3}초",
            this);

        return true;
    }

    private bool TryAlignLauncherModel()
    {
        if (_modelFirePoint == null && _modelLaunchOrigin == null)
        {
            return true;
        }

        if (_modelFirePoint == null || _modelLaunchOrigin == null)
        {
            return false;
        }

        Transform launcher = _modelFirePoint.parent;
        Transform modelPivot = launcher != null ? launcher.parent : null;

        RestoreLoadedPose();

        Vector3 firePosition = _modelFirePoint.position;
        Vector3 launchPosition = _modelLaunchOrigin.position;
        Vector3 direction = firePosition - launchPosition;

        if (modelPivot == null || _modelLaunchOrigin.parent != launcher ||
            direction.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        // 모델의 로컬 축과 +Z를 기준으로 조준하는 전투 피벗을 맞춤
        Quaternion aimRotation = Quaternion.LookRotation(direction, transform.up);
        _modelTurretPivot = modelPivot;
        _modelAimOffset = Quaternion.Inverse(aimRotation) * modelPivot.rotation;
        _turretPivot.SetPositionAndRotation(modelPivot.position, aimRotation);
        _launchOrigin.SetPositionAndRotation(launchPosition, aimRotation);
        _firePoint.SetPositionAndRotation(firePosition, aimRotation);
        return true;
    }

    private void Update()
    {
        if (!_isInitialized)
        {
            return;
        }

        if (!_buildPhaseGate.CanActivateTraps)
        {
            CancelShotAnimation();
            _targetEnemy = null;
            _targetCollider = null;
            _remainingAttackTime = 0f;

            // 웨이브가 끝나면 남은 화살의 공격 실행도 중단
            _projectileExecutor.CancelExecutions();
            return;
        }

        _remainingAttackTime = Mathf.Max(
            0f,
            _remainingAttackTime - Time.deltaTime);

        if (_shotAnimationTime < 0f || _hasReleasedArrow)
        {
            FindNearestEnemy();
        }

        RotateTowardTarget();

        if (_shotAnimationTime >= 0f)
        {
            UpdateShotAnimation(Time.deltaTime);
            if (_shotAnimationTime >= 0f)
            {
                return;
            }
        }

        TryStartShot();
    }

    private void FindNearestEnemy()
    {
        // 회전하는 발사점 대신 타워의 회전 중심을 탐색 기준으로 사용
        Vector3 origin = _turretPivot.position;

        Collider[] colliders = Physics.OverlapSphere(
            origin,
            _attackRange,
            _enemyMask,
            QueryTriggerInteraction.Collide);

        EnemyCombat nearestEnemy = null;
        Collider nearestCollider = null;
        float nearestSqrDistance = float.PositiveInfinity;

        foreach (Collider collider in colliders)
        {
            EnemyCombat enemy =
                collider.GetComponentInParent<EnemyCombat>();

            if (enemy == null || !enemy.CanReceiveDamage)
            {
                continue;
            }

            float sqrDistance =
                (enemy.transform.position - origin).sqrMagnitude;

            if (sqrDistance > _attackRange * _attackRange ||
                sqrDistance >= nearestSqrDistance)
            {
                continue;
            }

            Vector3 targetPoint = collider.bounds.center;

            // 가려진 후보는 선택하지 않고 다른 적을 계속 검사
            if (IsShotSegmentBlocked(origin, targetPoint))
            {
                continue;
            }

            nearestSqrDistance = sqrDistance;
            nearestEnemy = enemy;
            nearestCollider = collider;
        }

        _targetEnemy = nearestEnemy;
        _targetCollider = nearestCollider;
    }

    // 지정한 구간에 화살을 막는 지형이 있는지 검사
    private bool IsShotSegmentBlocked(
        Vector3 origin,
        Vector3 targetPoint)
    {
        float radius = _projectileData.CollisionRadius;

        // 검사 시작점부터 지형과 겹쳐 있는 경우도 차단
        if (Physics.CheckSphere(
            origin,
            radius,
            _obstacleMask,
            QueryTriggerInteraction.Ignore))
        {
            return true;
        }

        Vector3 displacement = targetPoint - origin;
        float distance = displacement.magnitude;

        if (distance <= 0.0001f)
        {
            return false;
        }

        // 실제 화살 두께를 반영해 벽 모서리도 검사
        return Physics.SphereCast(
            origin,
            radius,
            displacement / distance,
            out _,
            distance,
            _obstacleMask,
            QueryTriggerInteraction.Ignore);
    }

    private void RotateTowardTarget()
    {
        if (_targetEnemy == null || _targetCollider == null)
        {
            return;
        }

        Vector3 targetPosition = _targetCollider.bounds.center;

        Vector3 direction =
            targetPosition - _turretPivot.position;

        if (direction.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        Quaternion targetRotation = Quaternion.LookRotation(
            direction,
            transform.up);

        // 지상 적과 비행형을 모두 조준하도록 상하 회전도 허용
        _turretPivot.rotation = Quaternion.RotateTowards(
            _turretPivot.rotation,
            targetRotation,
            _rotationSpeed * Time.deltaTime);

        if (_modelTurretPivot != null)
        {
            _modelTurretPivot.rotation = _turretPivot.rotation * _modelAimOffset;
        }
    }

    private void TryStartShot()
    {
        bool hasAnimation = _modelAnimationRoot != null &&
            _modelFireClip != null && _modelFireClip.length >= ArrowReleaseTime &&
            _modelReloadClip != null && _loadedArrow != null;

        // 다음 발사의 준비 동작은 남은 공격 간격 안에서 시작
        float windupTime = hasAnimation ? ArrowReleaseTime : 0f;
        if (_remainingAttackTime > windupTime || !CanFireArrow(out Vector3 targetPoint))
        {
            return;
        }

        if (!hasAnimation)
        {
            FireArrow(targetPoint);
            return;
        }

        _shotAnimationTime = 0f;
        _hasReleasedArrow = false;
        _modelFireClip.SampleAnimation(_modelAnimationRoot, 0f);
        _loadedArrow.enabled = true;
    }

    private void UpdateShotAnimation(float deltaTime)
    {
        if (!_hasReleasedArrow)
        {
            _shotAnimationTime = Mathf.Min(ArrowReleaseTime, _shotAnimationTime + deltaTime);
            _modelFireClip.SampleAnimation(_modelAnimationRoot, _shotAnimationTime);

            if (_shotAnimationTime < ArrowReleaseTime || _remainingAttackTime > 0f)
            {
                return;
            }

            if (!CanFireArrow(out Vector3 targetPoint))
            {
                CancelShotAnimation();
                return;
            }

            _hasReleasedArrow = true;
            _loadedArrow.enabled = false;
            FireArrow(targetPoint);
            return;
        }

        // 강화로 공격 간격이 짧아져도 재장전 동작 때문에 발사가 늦어지지 않게 조정
        float fireDuration = _modelFireClip.length;
        float sequenceDuration = fireDuration + _modelReloadClip.length;
        float recoveryTime = Mathf.Max(0.001f, _attackInterval - ArrowReleaseTime);
        float playbackSpeed = Mathf.Max(1f, (sequenceDuration - ArrowReleaseTime) / recoveryTime);
        _shotAnimationTime += deltaTime * playbackSpeed;

        if (_shotAnimationTime >= sequenceDuration)
        {
            CancelShotAnimation();
        }
        else if (_shotAnimationTime < fireDuration)
        {
            _modelFireClip.SampleAnimation(_modelAnimationRoot, _shotAnimationTime);
            _loadedArrow.enabled = false;
        }
        else
        {
            _modelReloadClip.SampleAnimation(_modelAnimationRoot, _shotAnimationTime - fireDuration);
            _loadedArrow.enabled = true;
        }
    }

    private void CancelShotAnimation()
    {
        if (_shotAnimationTime < 0f)
        {
            return;
        }

        _shotAnimationTime = -1f;
        _hasReleasedArrow = false;
        RestoreLoadedPose();
    }

    private void RestoreLoadedPose()
    {
        if (_modelAnimationRoot != null && _modelReloadClip != null)
        {
            _modelReloadClip.SampleAnimation(_modelAnimationRoot, _modelReloadClip.length);
        }

        if (_loadedArrow != null)
        {
            _loadedArrow.enabled = true;
        }
    }

    private bool CanFireArrow(out Vector3 targetPoint)
    {
        targetPoint = default;
        if (_targetEnemy == null ||
            !_targetEnemy.CanReceiveDamage ||
            _targetCollider == null ||
            !_targetCollider.enabled ||
            !_targetCollider.gameObject.activeInHierarchy)
        {
            return false;
        }

        targetPoint = _targetCollider.bounds.center;

        Vector3 aimDirection =
            targetPoint - _turretPivot.position;

        if (aimDirection.sqrMagnitude <= 0.0001f ||
            aimDirection.sqrMagnitude > _attackRange * _attackRange)
        {
            return false;
        }

        float aimAngle = Vector3.Angle(
            _turretPivot.forward,
            aimDirection);

        if (aimAngle > _fireAngleTolerance)
        {
            return false;
        }

        Vector3 launchDirection =
            targetPoint - _firePoint.position;

        // 발사 지점 뒤쪽의 목표로 역방향 발사하지 않음
        if (Vector3.Dot(_firePoint.forward, launchDirection) <= 0f)
        {
            return false;
        }

        // 회전으로 발사점이 이동하거나 지형 가까이에 놓인 경우를 확인
        // 공용 투사체가 검사하는 실제 발사 구간과 맞춤
        return !IsShotSegmentBlocked(
                _launchOrigin.position,
                _firePoint.position) &&
            !IsShotSegmentBlocked(_firePoint.position, targetPoint);
    }

    private void FireArrow(Vector3 targetPoint)
    {
        _aimProvider.SetTargetPoint(targetPoint);

        _attackId++;

        AttackContext context = new AttackContext(
            gameObject,
            gameObject,
            ActorType.Installation,
            Faction.Ally,
            AttackType.BasicAttack,
            _attackId,
            _damage,
            _attackRange,
            360f);

        // 실행 전에 간격을 반영해 중복 발사 방지
        _remainingAttackTime = _attackInterval;

        // 실제 생성·이동·충돌·피해는 공용 투사체 기능 사용
        _projectileExecutor.Execute(context);
    }

    private void OnDisable()
    {
        CancelShotAnimation();
        if (_projectileExecutor != null)
        {
            _projectileExecutor.CancelExecutions();
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (_turretPivot == null)
        {
            return;
        }

        Color previousColor = Gizmos.color;

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(
            _turretPivot.position,
            _isInitialized ? _attackRange : 10f);

        Gizmos.color = previousColor;
    }
}
