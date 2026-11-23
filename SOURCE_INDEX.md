# 소스 색인

[README](README.md) · [공통 의존성](docs/DEPENDENCIES.md) · [출처·범위](NOTICE.md)

KMB 담당 런타임 소스 50개와 커스텀 셰이더 3개입니다. 각 파일은 원본 그대로 복사했습니다. 원본 전체 커밋 이력은 포함하지 않으며, source-manifest.json에서 원본 경로·기준 커밋·파일 해시를 확인할 수 있습니다.

## Enemy

- [EnemyAnimation.cs](src/Enemy/EnemyAnimation/EnemyAnimation.cs)
- [EnemyAnimationEvents.cs](src/Enemy/EnemyAnimation/EnemyAnimationEvents.cs)
- [EnemyAttack.cs](src/Enemy/EnemyAttack.cs)
- [EnemyCombat.cs](src/Enemy/EnemyCombat.cs)
- [EnemyData.cs](src/Enemy/EnemyData.cs)
- [EnemyMovement.cs](src/Enemy/EnemyMovement.cs)
- [EnemyMovementType.cs](src/Enemy/EnemyMovementType.cs)
- [EnemyPath.cs](src/Enemy/EnemyPath.cs)
- [EnemyPlayerDetector.cs](src/Enemy/EnemyPlayerDetector.cs)
- [EnemyReaction.cs](src/Enemy/EnemyReaction.cs)
- [EnemyReactionMode.cs](src/Enemy/EnemyReactionMode.cs)
- [EnemySpawner.cs](src/Enemy/EnemySpawner.cs)
- [MinotosAreaSlam.cs](src/Enemy/MinotosAreaSlam.cs)

## Installation

- [ArrowTower.cs](src/Installation/ArrowTower.cs)
- [ArrowTowerAimProvider.cs](src/Installation/ArrowTowerAimProvider.cs)
- [BuildArea.cs](src/Installation/BuildArea.cs)
- [BuildPhaseGate.cs](src/Installation/BuildPhaseGate.cs)
- [BuildPlacementBlocker.cs](src/Installation/BuildPlacementBlocker.cs)
- [BuildPlacementController.cs](src/Installation/BuildPlacementController.cs)
- [BuildPlacementValidator.cs](src/Installation/BuildPlacementValidator.cs)
- [BuildPreviewController.cs](src/Installation/BuildPreviewController.cs)
- [FireTrap.cs](src/Installation/FireTrap.cs)
- [FrostTrap.cs](src/Installation/FrostTrap.cs)
- [InstallationEconomyData.cs](src/Installation/InstallationEconomyData.cs)
- [InstallationModelPreview.cs](src/Installation/InstallationModelPreview.cs)
- [PlacedInstallation.cs](src/Installation/PlacedInstallation.cs)
- [ShockTrap.cs](src/Installation/ShockTrap.cs)
- [SpikeTrap.cs](src/Installation/SpikeTrap.cs)
- [TrapData.cs](src/Installation/TrapData.cs)
- [TrapDataMap.cs](src/Installation/TrapDataMap.cs)

## Minimap

- [EnemyMinimapTarget.cs](src/Minimap/EnemyMinimapTarget.cs)
- [MinimapTarget.cs](src/Minimap/MinimapTarget.cs)
- [MinimapTargetType.cs](src/Minimap/MinimapTargetType.cs)

## Shaders

- [InstallationPreviewOutline.shader](src/Shaders/InstallationPreviewOutline.shader)
- [MinotosAreaSlamTelegraph.shader](src/Shaders/MinotosAreaSlamTelegraph.shader)
- [Stage3StatueCracks.shader](src/Shaders/Stage3StatueCracks.shader)

## Stage

- [KMBLobbyEnemySource.cs](src/Stage/KMBLobbyEnemySource.cs)
- [KMBStage1Entry.cs](src/Stage/KMBStage1Entry.cs)
- [KMBStage2Entry.cs](src/Stage/KMBStage2Entry.cs)
- [KMBStage3Entry.cs](src/Stage/KMBStage3Entry.cs)
- [Stage1Controller.cs](src/Stage/Stage1Controller.cs)
- [Stage1Data.cs](src/Stage/Stage1Data.cs)
- [Stage2Controller.cs](src/Stage/Stage2Controller.cs)
- [Stage2Data.cs](src/Stage/Stage2Data.cs)
- [Stage2DefenseGoal.cs](src/Stage/Stage2DefenseGoal.cs)
- [Stage3Controller.cs](src/Stage/Stage3Controller.cs)
- [Stage3Data.cs](src/Stage/Stage3Data.cs)
- [Stage3StatueReveal.cs](src/Stage/Stage3StatueReveal.cs)
- [StageScoreData.cs](src/Stage/StageScoreData.cs)

## Wave

- [EnemySpawnData.cs](src/Wave/EnemySpawnData.cs)
- [WaveController.cs](src/Wave/WaveController.cs)
- [WaveData.cs](src/Wave/WaveData.cs)
- [WaveStartInput.cs](src/Wave/WaveStartInput.cs)
