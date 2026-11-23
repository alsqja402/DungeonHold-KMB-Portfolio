using System;
using UnityEngine;

[RequireComponent(typeof(Health))]
[RequireComponent(typeof(DamageReceiver))]
public class Stage2DefenseGoal : MonoBehaviour
{
    [SerializeField] private Stage2Data _stageData;
    [SerializeField] private Stage3Controller _stage3Controller;

    private Health _health;
    private DamageReceiver _damageReceiver;

    private bool _isInitialized;

    public float CurrentHp => _health.CurrentHp;
    public float MaxHp => _health.MaxHp;

    // 회복해도 실제 피해를 받은 기록은 유지
    public bool HasTakenDamage { get; private set; }

    public bool IsAlive =>
        _isInitialized &&
        _health.IsAlive;

    // 이후 적 공격 코드에서 대상의 유효성을 확인할 때 사용
    public bool CanReceiveDamage =>
        isActiveAndEnabled &&
        IsAlive &&
        _health.isActiveAndEnabled &&
        _damageReceiver.isActiveAndEnabled;

    // 실제 공격 피해는 공통 피해 수신부로 전달
    public DamageReceiver Receiver => _damageReceiver;

    internal bool UsesStage3Controller(Stage3Controller controller) =>
        _stage3Controller == controller;

    internal bool TryApplyStartingHpForMap(float maxHp)
    {
        if (!CanReceiveDamage || HasTakenDamage || !(maxHp > 0f) || float.IsInfinity(maxHp))
        {
            return false;
        }

        if (_health.MaxHp != maxHp)
            _health.Init(maxHp);

        return true;
    }

    // 이후 Stage2Controller에서 실패 처리에 연결
    public event Action OnDepleted;

    private void Awake()
    {
        _health = GetComponent<Health>();
        _damageReceiver = GetComponent<DamageReceiver>();

        if (_health == null || _damageReceiver == null || !TryGetMaxHp(out float maxHp))
        {
            Debug.LogError(
                "방어 목표의 Health, DamageReceiver와 스테이지 HP 설정을 확인하세요.",
                this);

            enabled = false;
            return;
        }

        // 이번 씬 실행의 방어 목표 체력 초기화
        _health.Init(maxHp);

        // 방어 목표는 아군 진영이며 현재 피해 감소율은 0
        bool wasInitialized = _damageReceiver.Init(
            gameObject,
            _health,
            Faction.Ally,
            0f);

        if (!wasInitialized)
        {
            Debug.LogError(
                "방어 목표의 피해 수신 초기화에 실패했습니다.",
                this);

            enabled = false;
            return;
        }

        _isInitialized = true;
    }

    private bool TryGetMaxHp(out float maxHp)
    {
        if (_stage3Controller != null)
        {
            return _stage3Controller.TryGetPhaseOneGoalMaxHp(this, out maxHp);
        }

        maxHp = 0f;
        if (_stageData == null || !_stageData.IsValid)
        {
            return false;
        }

        maxHp = _stageData.DefenseGoalMaxHp;
        return true;
    }

    private void OnEnable()
    {
        if (!_isInitialized)
        {
            return;
        }

        _damageReceiver.OnDamaged += HandleDamaged;
        _damageReceiver.OnDied += HandleDied;
    }

    private void OnDisable()
    {
        if (_damageReceiver != null)
        {
            _damageReceiver.OnDamaged -= HandleDamaged;
            _damageReceiver.OnDied -= HandleDied;
        }
    }

    private void HandleDamaged(
    DamageInfo damageInfo,
    float appliedDamage)
    {
        if (appliedDamage > 0f)
        {
            HasTakenDamage = true;

            SoundManager sound = GameManager.Instance?.Sound;

            if (sound != null)
            {
                sound.PlayAt(SoundId.ObjectiveHurt, transform.position);
            }
        }

        Debug.Log(
            $"방어 목표 피격: 피해 {appliedDamage:F1}, " +
            $"HP {_health.CurrentHp:F1}/{_health.MaxHp:F1}",
            this);
    }

    private void HandleDied(DamageInfo damageInfo)
    {
        Debug.Log("방어 목표 HP가 소진되었습니다.", this);

        OnDepleted?.Invoke();
    }
}
