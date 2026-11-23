# 생략한 의존성과 실행 조건

[README](../README.md) · [출처·범위](../NOTICE.md)

## 왜 단독 컴파일되지 않나요?

이 저장소는 완성 게임 배포판이 아닌 담당 코드의 열람용 스냅샷입니다. Unity 프로젝트 설정·패키지·씬·GUID 연결과 다른 담당자의 공통 클래스가 빠져 있습니다. 일반 C# 프로젝트에서 열면 Unity 타입 자체도 해석되지 않습니다.

형식적으로 빌드를 통과시키는 가짜 구현이나 원본과 다른 실행 규칙을 추가하지 않았습니다.

## 주요 공통 의존성

아래는 대표 의존성입니다. 전체 타입·함수 서명을 재배포하는 명세는 아닙니다.

| 영역 | 대표 타입·API | KMB 코드의 사용 목적 |
| --- | --- | --- |
| 피해·생존 | Health, DamageReceiver, DamageInfo | 유효 피해·생존·사망 연결 |
| 효과·넉백 | StatusEffectController, KnockbackController | 이동·공격 제한과 효과 적용 |
| 공격 실행 | AttackExecutor 계열 | 포탑 발사·투사체·타격 연결 |
| 플레이 수명 | LJPlay.PlayScene, SceneFlow, IStageEndLink | 준비·도전·맵 참조·종료 조율 |
| 경제·보상 | Currency, StageRewardController | 설치 비용·환불·원래 도전의 보상 사건 |
| 강화 | InstallationUpgradeStats 및 지원 계약 | 입장 시 확정된 설치물 강화 반영 |
| 플레이어·동료 | Hero, Companion, CompanionPlacementPoint | 추격·피해 대상과 맵 배치점 연결 |
| 미션·도감 | MissionStageResult, LobbyEnemySource 등 | 방어·처치·결과·등장 적 데이터 전달 |
| 표현 | SoundPlayer, SoundLoopPlayer, CharacterVfx 등 | 기존 공용 사운드·효과 호출 |

## 원본 Unity 환경

- Unity 6000.0.68f1
- Universal Render Pipeline 17.0.4
- AI Navigation 2.0.10
- Input System 1.18.0
- Cinemachine 3.1.7
- uGUI 2.0.0 및 TextMeshPro

이 목록은 원본 환경의 설명이며 자동 설치나 버전 업그레이드를 요구하지 않습니다. 모든 패키지가 발췌 코드 전체에서 직접 참조되는 것은 아닙니다.

## 외부 리소스

Linework, DungeonTraps, Minotos 모델·애니메이션, 맵 모듈·소품 등의 원본은 포함하지 않았습니다. 프리팹·Animator·재질 참조를 포함한 실제 실행 검증은 해당 리소스와 팀 공통 코드를 갖춘 원본 프로젝트에서만 가능합니다.
