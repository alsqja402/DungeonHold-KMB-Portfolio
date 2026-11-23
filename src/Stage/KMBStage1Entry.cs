using LJPlay;
using UnityEngine;

public class KMBStage1Entry : MonoBehaviour
{
    [SerializeField] private KMBLobbyEnemySource _stageDataSource;
    [SerializeField] private WaveController _waveController;

    [SerializeField] private Stage1Controller _stageController;
    [SerializeField] private BuildPhaseGate _buildPhaseGate;
    [SerializeField] private BuildPreviewController _buildPreview;
    [SerializeField] private BuildPlacementController _buildPlacement;
    [SerializeField] private CompanionPlacementPoint[] _companionPlacementPoints =
        System.Array.Empty<CompanionPlacementPoint>();
    [SerializeField] private Transform _heroStartPoint;
    [SerializeField] private Transform _heroRevivePoint;

    [SerializeField, Min(0)]
    private int _initialStageCurrency = 500;

    private int? _handledRequestId;
    private SceneFlow _endLinkFlow;
    private PlayScene _endLinkOwner;

    public static bool SupportsMap(string mapId, MissionDifficulty difficulty)
    {
        return mapId == "stage_1" &&
            (difficulty == MissionDifficulty.Easy ||
             difficulty == MissionDifficulty.Normal ||
             difficulty == MissionDifficulty.Hard);
    }

    private void Update()
    {
        SceneFlow flow = GameManager.Instance?.Scenes;
        if (flow == null ||
            !flow.TryGetMapPreparation(
                gameObject.scene,
                out PlayScene playScene,
                out StageEntrySelection entry,
                out int requestId))
        {
            return;
        }

        if (_handledRequestId == requestId)
        {
            return;
        }

        _handledRequestId = requestId;

        if (!SupportsMap(entry.MapId, entry.Difficulty))
        {
            flow.ReportMapFailure(
                playScene, requestId,
                "지원하지 않는 Stage 1 입장 요청입니다.");
            return;
        }

        Stage1Data stageData = _stageDataSource != null
            ? _stageDataSource.GetStage1Data(entry.Difficulty)
            : null;
        if (stageData == null || stageData is Stage3Data || !stageData.IsValid)
        {
            flow.ReportMapFailure(
                playScene, requestId,
                $"Stage 1의 선택 난이도 데이터를 확인하세요. 난이도 {entry.Difficulty}");
            return;
        }

        if (!flow.TryBindHeroPoints(
                gameObject.scene, requestId, _heroStartPoint, _heroRevivePoint))
        {
            flow.ReportMapFailure(
                playScene, requestId,
                "Stage 1의 Hero 시작점과 자동 부활점을 연결하지 못했습니다.");
            return;
        }

        if (_waveController == null ||
            !_waveController.TryInitializeForMap(playScene, stageData))
        {
            flow.ReportMapFailure(
                playScene, requestId,
                "Stage 1 웨이브 초기화에 실패했습니다. 스테이지 데이터와 스포너를 확인하세요.");
            return;
        }

        if (_stageController == null ||
            !_stageController.TryInitializeForMap(playScene, stageData))
        {
            flow.ReportMapFailure(
                playScene, requestId,
                "Stage 1 판정 초기화에 실패했습니다. 스테이지 데이터와 점수 설정을 확인하세요.");
            return;
        }

        if (_buildPhaseGate == null ||
            !_buildPhaseGate.TryInitializeForMap(playScene))
        {
            flow.ReportMapFailure(
                playScene, requestId,
                "Stage 1 설치 가능 조건을 초기화하지 못했습니다.");
            return;
        }

        if (_buildPreview == null ||
            !_buildPreview.TryInitializeForMap(playScene) ||
            _buildPlacement == null ||
            !_buildPlacement.TryInitializeForMap(playScene))
        {
            flow.ReportMapFailure(
                playScene, requestId,
                "Stage 1 설치 프리뷰 또는 배치 초기화에 실패했습니다.");
            return;
        }

        if (!flow.TryBindMapReferences(
            gameObject.scene, requestId, _buildPreview, _companionPlacementPoints))
        {
            flow.ReportMapFailure(
                playScene, requestId,
                "Stage 1 설치 프리뷰와 동료 배치점을 연결하지 못했습니다.");
            return;
        }

        if (!flow.TryBindEndLink(playScene, _stageController))
        {
            flow.ReportMapFailure(
                playScene, requestId,
                "Stage 1 종료 링크를 등록하지 못했습니다.");
            return;
        }

        _endLinkFlow = flow;
        _endLinkOwner = playScene;

        flow.TryBeginPreparedStage(
            playScene,
            requestId,
            entry.MapId,
            entry.Difficulty,
            _initialStageCurrency,
            MissionDefenseRule.NoPass);
    }

    private void OnDestroy()
    {
        _endLinkFlow?.UnbindEndLink(_endLinkOwner, _stageController);
    }
}
