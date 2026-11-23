using UnityEngine;

public class BuildPhaseGate : MonoBehaviour
{
    [SerializeField] private WaveController _waveController;
    [SerializeField] private LJPlay.PlayScene _playerScene;
    [SerializeField] private bool _waitForMapPreparation;

    // LJ의 플레이어·재화 도전 초기화가 완료된 상태
    // 종료 예약은 정산 실패 시 취소될 수 있으므로 여기서는 구분
    public bool HasReadyPlayerStage =>
        isActiveAndEnabled &&
        _playerScene != null &&
        _playerScene.isActiveAndEnabled &&
        _playerScene.IsStageReady;

    // 선택·설치·철거가 같은 진행 조건을 사용
    public bool CanBuild =>
        HasReadyPlayerStage &&
        !_playerScene.IsEndingStage &&
        _waveController != null &&
        _waveController.isActiveAndEnabled &&
        _waveController.IsPreparing &&
        !_waveController.IsWaveRunning &&
        !_waveController.HasCompletedAllWaves;

    // 일반 웨이브 종료 후에도 Stage 3 보스가 살아 있으면 함정이 작동합니다.
    public bool CanActivateTraps =>
        isActiveAndEnabled &&
        _waveController != null &&
        _waveController.isActiveAndEnabled &&
        !_waveController.HasStoppedForStageResult &&
        ((_waveController.IsWaveRunning && !_waveController.HasCompletedAllWaves) ||
         _waveController.HasActivePhaseBoss);

    private void Awake()
    {
        if (_waveController == null ||
            (!_waitForMapPreparation && _playerScene == null))
        {
            Debug.LogError(
                "BuildPhaseGate의 WaveController와 PlayScene 설정을 확인하세요.",
                this);

            enabled = false;
            return;
        }

        if (_waitForMapPreparation)
        {
            _playerScene = null;
        }
    }

    public bool TryInitializeForMap(LJPlay.PlayScene playScene)
    {
        if (!_waitForMapPreparation ||
            !isActiveAndEnabled ||
            playScene == null ||
            !playScene.isActiveAndEnabled ||
            !playScene.IsInitialized ||
            _waveController == null ||
            !_waveController.HasValidatedSetup)
        {
            return false;
        }

        _playerScene = playScene;
        return true;
    }
}
