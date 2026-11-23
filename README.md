# Dungeon Hold · KMB Client Portfolio

**3인칭 액션 디펜스의 설치 시스템·적 AI·웨이브·스테이지 구현을 정리한 코드 포트폴리오입니다.**

| 항목 | 내용 |
| --- | --- |
| 개발 기간 | 2026.10.12–2026.11.19 |
| 팀 구성 | 클라이언트 개발 3명 · UI/UX 2명 |
| 개인 역할 | KMB — 설치·설치물, 적 AI·보스, 웨이브·스폰, 승패·별·점수 판정과 맵 연계 |
| 기술 | Unity 6000.0.68f1 · C# · URP · AI Navigation · ScriptableObject |
| 형태 | PC 싱글 플레이 / 팀 프로젝트 담당 소스 발췌 |

[Notion 상세 포트폴리오](https://app.notion.com/p/4217c752df0a80658d51e1465a7dfb86) · [소스 색인](SOURCE_INDEX.md) · [공통 의존성](docs/DEPENDENCIES.md)

> **이 저장소는 단독 실행되는 Unity 프로젝트가 아닙니다.**
> 팀 비공개 프로젝트에서 KMB 담당 소스만 분리했습니다. 플레이어·동료·공통 전투·저장·UI 및 외부 에셋은 포함하지 않습니다. 코드를 빌드하거나 실행하기 위한 필수 의존성이 생략되어 있습니다.

## 먼저 볼 구현 세 가지

| 대표 구현 | 해결한 문제 | 코드 · 설명 |
| --- | --- | --- |
| 설치 검증과 실패 복구 | 프리뷰 이후 상태가 바뀌거나 생성에 실패했을 때 셀·재화가 어긋나는 문제 | [BuildPlacementController](src/Installation/BuildPlacementController.cs) · [기술 설명](docs/02-installation.md) |
| Stage 3의 2단계 전투 | 이미 살아 있는 적의 목적지를 바꾸고 일반 적·보스를 별도로 집계 | [Stage3Controller](src/Stage/Stage3Controller.cs) · [기술 설명](docs/04-wave-stage.md) |
| 보스 광역기의 타이밍 | 전조·애니메이션·범위 피해를 같은 타격 시점에 연결 | [MinotosAreaSlam](src/Enemy/MinotosAreaSlam.cs) · [기술 설명](docs/05-boss.md) |

## 게임 흐름

로비에서 무기·난이도·강화를 준비하고, 스테이지에서 설치물과 동료를 운용하며 직접 전투합니다. 웨이브 사이에 재정비하고, 스테이지별 목표에 따라 승패·별·점수를 판정합니다.

```mermaid
flowchart LR
    Lobby[로비 준비] --> Map[맵 입장·준비]
    Map --> Build[설치·동료 배치]
    Build --> Combat[웨이브 전투]
    Combat -->|다음 웨이브| Build
    Combat --> Result[승패·정산]
    Result --> Lobby
```

## 담당 범위

- **KMB:** 설치 검증·프리뷰·설치물 5종, 일반 적 4종·보스, 웨이브·스폰, 스테이지별 판정, 맵 구성과 공통 기능 연결.
- **LJ:** 플레이어·동료, 공통 피해·상태이상, 재화·강화·미션·도감·저장·씬 이동 등의 기반.
- **LHJ:** 로비·HUD·상점·강화·도감·결과 등의 UI 구현과 화면·연출 연계.
- **UI/UX 2명:** UI/UX 관련 제작.

공통 API를 호출하는 코드는 포함하지만, 호출 대상인 팀원 구현을 제 단독 구현으로 소개하지 않습니다. 리소스 활용과 공개 범위는 [출처·범위](NOTICE.md)에 정리했습니다.

## 읽는 순서

1. [구조와 책임 분리](docs/01-architecture.md)
2. [설치 시스템·프리뷰·강화](docs/02-installation.md)
3. [적 이동·반격·상태이상](docs/03-enemy-ai.md)
4. [웨이브·스테이지·2단계 전투](docs/04-wave-stage.md)
5. [보스 광역기와 석상 등장 연출](docs/05-boss.md)
6. [Profiler 분석과 반영한 최적화](docs/06-performance.md)
7. [문제 해결 사례와 현재 한계](docs/07-troubleshooting.md)
8. [PlayBase·저장·보상 연계](docs/08-integration.md)

## 소스 구성

```text
src/
  Installation/  셀·프리뷰·배치·설치물 5종
  Enemy/         이동·반격·공격·피격 연결·보스
  Wave/          웨이브·스폰 데이터와 진행
  Stage/         맵 진입·승패·별·점수·석상 연출
  Minimap/       적·목표의 미니맵 식별 연결
  Shaders/       KMB의 프리뷰·보스 전조·석상 균열 셰이더
docs/            코드 설명·구조도·측정 결과
```

원본 소스 50개와 커스텀 셰이더 3개를 리팩터링 없이 복사했습니다. 원본 위치와 SHA-256은 [source-manifest.json](source-manifest.json)에 기록했습니다. Unity의 GUID 참조를 보존하는 에셋 패키지는 아닙니다.

## 실행·검증 범위

이 저장소만으로 빌드할 수 없으므로 실행·통합 테스트를 제공하지 않습니다. 가짜 공통 구현이나 동작을 보장하지 않는 실행용 스텁도 추가하지 않았습니다.

원본 프로젝트에서 수행한 기능 확인과 코드 보완, 아직 남은 재확인은 각 문서에서 구분했습니다. Profiler 수치는 에디터 기록이며, 빌드 성능이나 안정적인 60FPS 달성을 의미하지 않습니다.

## 데모와 시각 자료

플레이 영상·스크린샷은 추후 추가합니다. 현재는 문서의 Mermaid 구조도로 구현 흐름을 확인할 수 있습니다. 필요한 촬영 목록은 [미디어 가이드](docs/09-media.md)에 정리했습니다.

## 작성 기준

GitHub 공식 안내의 **짧은 프로젝트 소개, 대표 구현으로의 빠른 이동, 실행·검증 조건과 의존성 설명**을 참고했습니다.

- [Using your GitHub profile to enhance your resume](https://docs.github.com/en/account-and-profile/tutorials/using-your-github-profile-to-enhance-your-resume)
- 원본 코드 기준: `87e6582baab5b97f0d00f114b00b64d21fdaa10f`
