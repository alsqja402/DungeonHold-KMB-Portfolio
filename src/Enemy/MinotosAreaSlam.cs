using UnityEngine;

[RequireComponent(typeof(Animator))]
public sealed class MinotosAreaSlam : MonoBehaviour
{
    private static readonly int StateHash = Animator.StringToHash("Base Layer.AreaSlam");
    private static readonly int AnimationParameter = Animator.StringToHash("animation");
    private static readonly int FillProperty = Shader.PropertyToID("_Fill");

    [SerializeField] private AnimationClip _slamClip;
    [SerializeField] private Shader _telegraphShader;
    [SerializeField, Min(0.1f)] private float _radius = 4f;
    [SerializeField, Min(0.1f)] private float _damage = 35f;
    [SerializeField, Min(0.1f)] private float _verticalTolerance = 1.5f;

    private float _attackMultiplier = 1f;
    private Animator _animator;
    private EnemyCombat _combat;
    private Health _health;
    private GameObject _telegraph;
    private Material _telegraphMaterial;
    private Vector3 _impactCenter;
    private float _impactTime;
    private int _startFrame;
    private int _impactFrame;
    private bool _isQueued;
    private bool _hasStarted;
    private bool _hasHit;

    public bool IsQueued => isActiveAndEnabled && _isQueued;
    public bool IsPlaying { get; private set; }

    private float ScaledDamage => _damage * _attackMultiplier;

    internal bool TrySetAttackMultiplier(float attackMultiplier)
    {
        float damage = _damage * attackMultiplier;
        if (!(damage > 0f) || float.IsInfinity(damage))
        {
            Debug.LogError("Minotos 광역기의 피해와 공격력 배율을 확인하세요.", this);
            return false;
        }

        _attackMultiplier = attackMultiplier;
        return true;
    }

    private void Awake()
    {
        _animator = GetComponent<Animator>();
        _combat = GetComponentInParent<EnemyCombat>();
        _health = _combat != null ? _combat.GetComponent<Health>() : null;

        if (_slamClip != null)
        {
            foreach (AnimationEvent marker in _slamClip.events)
            {
                if (marker.functionName == nameof(OnAreaSlamHit))
                {
                    _impactTime = marker.time;
                    break;
                }
            }
        }

        if (_health == null || _telegraphShader == null || _impactTime <= 0f ||
            !_animator.HasState(0, StateHash))
        {
            Debug.LogError("Minotos 광역기 클립, 타격 이벤트와 전조 설정을 확인하세요.", this);
            enabled = false;
        }
    }

    private void OnEnable()
    {
        _health.OnHpChanged += HandleHpChanged;
    }

    private void OnDisable()
    {
        if (_health != null)
        {
            _health.OnHpChanged -= HandleHpChanged;
        }

        Cancel();
    }

    private void OnDestroy()
    {
        if (_telegraph != null)
        {
            Destroy(_telegraph);
        }

        if (_telegraphMaterial != null)
        {
            Destroy(_telegraphMaterial);
        }
    }

    private void HandleHpChanged(float currentHp, float maxHp)
    {
        if (!_hasStarted && currentHp > 0f && currentHp <= maxHp * 0.5f)
        {
            _isQueued = true;
        }
    }

    public bool TryStart()
    {
        if (!IsQueued || !_combat.CanReceiveDamage || !_health.IsAlive ||
            !_animator.isActiveAndEnabled)
        {
            return false;
        }

        _impactCenter = _combat.transform.position;
        ShowTelegraph();
        _isQueued = false;
        _hasStarted = true;
        _hasHit = false;
        IsPlaying = true;
        _startFrame = Time.frameCount;
        _animator.SetInteger(AnimationParameter, 6);
        _animator.Play(StateHash, 0, 0f);
        Debug.Log($"Minotos 광역기 전조 시작: 반경 {_radius:F1}, 피해 {ScaledDamage:F1}", this);
        return true;
    }

    private void LateUpdate()
    {
        if (!IsPlaying || Time.frameCount == _startFrame)
        {
            return;
        }

        AnimatorStateInfo state = _animator.GetCurrentAnimatorStateInfo(0);
        if (!_animator.isActiveAndEnabled || state.fullPathHash != StateHash ||
            state.normalizedTime >= 1f)
        {
            Cancel();
            return;
        }

        if (_hasHit)
        {
            if (Time.frameCount > _impactFrame)
            {
                _telegraph.SetActive(false);
            }

            return;
        }

        float elapsed = state.normalizedTime * _slamClip.length;
        _telegraphMaterial.SetFloat(FillProperty, Mathf.Clamp01(elapsed / _impactTime));
    }

    public void OnAreaSlamHit(AnimationEvent marker)
    {
        if (!IsPlaying || _hasHit || !_health.IsAlive || !_combat.CanReceiveDamage ||
            marker.animatorClipInfo.clip != _slamClip ||
            marker.animatorStateInfo.fullPathHash != StateHash)
        {
            return;
        }

        _hasHit = true;
        _impactFrame = Time.frameCount;
        _telegraphMaterial.SetFloat(FillProperty, 1f);

        DamageInfo damageInfo = new DamageInfo(
            _combat.gameObject, gameObject, ActorType.Enemy, Faction.Enemy,
            ScaledDamage, AttackType.Skill, _startFrame);

        int targetsHit = 0;
        foreach (Hero hero in FindObjectsByType<Hero>(FindObjectsSortMode.None))
        {
            targetsHit += TryDamage(hero, damageInfo) ? 1 : 0;
        }

        foreach (Companion companion in FindObjectsByType<Companion>(FindObjectsSortMode.None))
        {
            targetsHit += TryDamage(companion, damageInfo) ? 1 : 0;
        }

        Debug.Log($"Minotos 광역기 타격: 피해 적용 {targetsHit}명", this);
    }

    public void OnAreaSlamEnd(AnimationEvent marker)
    {
        if (IsPlaying && marker.animatorClipInfo.clip == _slamClip &&
            marker.animatorStateInfo.fullPathHash == StateHash)
        {
            Cancel();
        }
    }

    public void Cancel()
    {
        IsPlaying = false;
        if (_telegraph != null)
        {
            _telegraph.SetActive(false);
        }
    }

    private bool TryDamage(Behaviour target, DamageInfo damageInfo)
    {
        if (target == null || !target.isActiveAndEnabled)
        {
            return false;
        }

        Vector3 offset = target.transform.position - _impactCenter;
        if (Mathf.Abs(offset.y) > _verticalTolerance ||
            offset.x * offset.x + offset.z * offset.z > _radius * _radius)
        {
            return false;
        }

        DamageReceiver receiver = target.GetComponentInChildren<DamageReceiver>();
        return receiver != null && receiver.isActiveAndEnabled && receiver.IsAlive &&
            receiver.Faction == Faction.Ally &&
            receiver.TakeDamage(damageInfo) > 0f;
    }

    private void ShowTelegraph()
    {
        if (_telegraph == null)
        {
            _telegraph = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _telegraph.name = "Minotos Area Slam Telegraph";
            Collider telegraphCollider = _telegraph.GetComponent<Collider>();
            if (telegraphCollider != null)
            {
                telegraphCollider.enabled = false;
                Destroy(telegraphCollider);
            }

            _telegraphMaterial = new Material(_telegraphShader);
            _telegraph.GetComponent<MeshRenderer>().sharedMaterial = _telegraphMaterial;
        }

        _telegraph.transform.SetPositionAndRotation(
            _impactCenter + Vector3.up * 0.04f, Quaternion.Euler(90f, 0f, 0f));
        _telegraph.transform.localScale = new Vector3(_radius * 2f, _radius * 2f, 1f);
        _telegraphMaterial.SetFloat(FillProperty, 0f);
        _telegraph.SetActive(true);
    }
}
