# Snow Panic - Script Migration Map (Planning Only)

## [MIGRATION MAP]

### Core
- current_path=Assets/Scripts/AvalanchePhysicsSystem.cs
  proposed_path=Assets/Scripts/Core/AvalanchePhysicsSystem.cs
  category=Core
  owner_system=SnowPhysics
  risk_level=high

- current_path=Assets/Scripts/SnowLayerType.cs
  proposed_path=Assets/Scripts/Core/SnowLayerType.cs
  category=Core
  owner_system=SnowPhysics
  risk_level=low

- current_path=Assets/Scripts/SnowPieceAutoSettle.cs
  proposed_path=Assets/Scripts/Core/SnowPieceAutoSettle.cs
  category=Core
  owner_system=SnowPhysics
  risk_level=medium

- current_path=Assets/Scripts/SnowPackFallingPiece.cs
  proposed_path=Assets/Scripts/Core/SnowPackFallingPiece.cs
  category=Core
  owner_system=SnowPhysics
  risk_level=high

- current_path=Assets/Scripts/SnowPackSpawner.cs
  proposed_path=Assets/Scripts/Core/SnowPackSpawner.cs
  category=Core
  owner_system=SnowPhysics
  risk_level=high

- current_path=Assets/Scripts/SnowClump.cs
  proposed_path=Assets/Scripts/Core/SnowClump.cs
  category=Core
  owner_system=SnowPhysics
  risk_level=high

- current_path=Assets/Scripts/SnowCluster.cs
  proposed_path=Assets/Scripts/Core/SnowCluster.cs
  category=Core
  owner_system=SnowPhysics
  risk_level=medium

- current_path=Assets/Scripts/SnowMvpBootstrap.cs
  proposed_path=Assets/Scripts/Core/SnowMvpBootstrap.cs
  category=Core
  owner_system=SnowPhysics
  risk_level=high

- current_path=Assets/Scripts/SnowModule.cs
  proposed_path=Assets/Scripts/Core/SnowModule.cs
  category=Core
  owner_system=SnowPhysics
  risk_level=medium

- current_path=Assets/Scripts/MvpSnowChunkMotion.cs
  proposed_path=Assets/Scripts/Core/MvpSnowChunkMotion.cs
  category=Core
  owner_system=SnowPhysics
  risk_level=high

- current_path=Assets/Scripts/SnowFallSystem.cs
  proposed_path=Assets/Scripts/Core/SnowFallSystem.cs
  category=Core
  owner_system=SnowPhysics
  risk_level=high

- current_path=Assets/Scripts/GroundSnowSystem.cs
  proposed_path=Assets/Scripts/Core/GroundSnowSystem.cs
  category=Core
  owner_system=SnowPhysics
  risk_level=high

- current_path=Assets/Scripts/GroundSnowPile.cs
  proposed_path=Assets/Scripts/Core/GroundSnowPile.cs
  category=Core
  owner_system=SnowPhysics
  risk_level=medium

- current_path=Assets/Scripts/GroundSnowAccumulator.cs
  proposed_path=Assets/Scripts/Core/GroundSnowAccumulator.cs
  category=Core
  owner_system=SnowPhysics
  risk_level=medium

- current_path=Assets/Scripts/SnowfallEventBurst.cs
  proposed_path=Assets/Scripts/Core/SnowfallEventBurst.cs
  category=Core
  owner_system=SnowPhysics
  risk_level=medium

- current_path=Assets/Scripts/DetachedSnowRegistry.cs
  proposed_path=Assets/Scripts/Core/DetachedSnowRegistry.cs
  category=Core
  owner_system=SnowPhysics
  risk_level=medium

- current_path=Assets/Scripts/SnowVisual.cs
  proposed_path=Assets/Scripts/Core/SnowVisual.cs
  category=Core
  owner_system=SnowPhysics
  risk_level=medium

- current_path=Assets/Scripts/EavesCatchZone.cs
  proposed_path=Assets/Scripts/Core/EavesCatchZone.cs
  category=Core
  owner_system=SnowPhysics
  risk_level=medium

- current_path=Assets/Scripts/EavesDropTrigger.cs
  proposed_path=Assets/Scripts/Core/EavesDropTrigger.cs
  category=Core
  owner_system=SnowPhysics
  risk_level=medium

- current_path=Assets/Scripts/RoofSnowSystem.cs
  proposed_path=Assets/Scripts/Core/RoofSnowSystem.cs
  category=Core
  owner_system=RoofSnow
  risk_level=high

- current_path=Assets/Scripts/RoofSnow.cs
  proposed_path=Assets/Scripts/Core/RoofSnow.cs
  category=Core
  owner_system=RoofSnow
  risk_level=high

- current_path=Assets/Scripts/RoofSnowPlaceholder.cs
  proposed_path=Assets/Scripts/Core/RoofSnowPlaceholder.cs
  category=Core
  owner_system=RoofSnow
  risk_level=low

- current_path=Assets/Scripts/RoofSnowMaskController.cs
  proposed_path=Assets/Scripts/Core/RoofSnowMaskController.cs
  category=Core
  owner_system=RoofSnow
  risk_level=medium

- current_path=Assets/Scripts/RoofSnowCleanup.cs
  proposed_path=Assets/Scripts/Core/RoofSnowCleanup.cs
  category=Core
  owner_system=RoofSnow
  risk_level=low

- current_path=Assets/Scripts/CorniceRuntimeSnowSetup.cs
  proposed_path=Assets/Scripts/Core/CorniceRuntimeSnowSetup.cs
  category=Core
  owner_system=Cornice
  risk_level=high

### Game
- current_path=Assets/Scripts/CoreGameplayManager.cs
  proposed_path=Assets/Scripts/Game/CoreGameplayManager.cs
  category=Game
  owner_system=Run
  risk_level=high

- current_path=Assets/Scripts/RunStructureManager.cs
  proposed_path=Assets/Scripts/Game/RunStructureManager.cs
  category=Game
  owner_system=Run
  risk_level=high

- current_path=Assets/Scripts/SnowPhysicsScoreManager.cs
  proposed_path=Assets/Scripts/Game/SnowPhysicsScoreManager.cs
  category=Game
  owner_system=HUD
  risk_level=medium

- current_path=Assets/Scripts/ToolCooldownManager.cs
  proposed_path=Assets/Scripts/Game/ToolCooldownManager.cs
  category=Game
  owner_system=Tool
  risk_level=low

- current_path=Assets/Scripts/UnifiedHUD.cs
  proposed_path=Assets/Scripts/Game/UnifiedHUD.cs
  category=Game
  owner_system=HUD
  risk_level=high

- current_path=Assets/Scripts/RunHUDUI.cs
  proposed_path=Assets/Scripts/Game/RunHUDUI.cs
  category=Game
  owner_system=HUD
  risk_level=medium

- current_path=Assets/Scripts/RunResultUI.cs
  proposed_path=Assets/Scripts/Game/RunResultUI.cs
  category=Game
  owner_system=HUD
  risk_level=medium

- current_path=Assets/Scripts/SnowScoreDisplayUI.cs
  proposed_path=Assets/Scripts/Game/SnowScoreDisplayUI.cs
  category=Game
  owner_system=HUD
  risk_level=medium

- current_path=Assets/Scripts/TapToSlideOnRoof.cs
  proposed_path=Assets/Scripts/Game/TapToSlideOnRoof.cs
  category=Game
  owner_system=SnowPhysics
  risk_level=high

- current_path=Assets/Scripts/SnowTestSlideAssist.cs
  proposed_path=Assets/Scripts/Game/SnowTestSlideAssist.cs
  category=Game
  owner_system=SnowPhysics
  risk_level=high

- current_path=Assets/Scripts/AvalancheFeedback.cs
  proposed_path=Assets/Scripts/Game/AvalancheFeedback.cs
  category=Game
  owner_system=SnowPhysics
  risk_level=medium

- current_path=Assets/Scripts/SnowTestCube.cs
  proposed_path=Assets/Scripts/Game/SnowTestCube.cs
  category=Game
  owner_system=SnowPhysics
  risk_level=medium

- current_path=Assets/Scripts/CorniceSnowSegment.cs
  proposed_path=Assets/Scripts/Game/CorniceSnowSegment.cs
  category=Game
  owner_system=Cornice
  risk_level=medium

- current_path=Assets/Scripts/CorniceSnowManager.cs
  proposed_path=Assets/Scripts/Game/CorniceSnowManager.cs
  category=Game
  owner_system=Cornice
  risk_level=medium

- current_path=Assets/Scripts/CorniceHitter.cs
  proposed_path=Assets/Scripts/Game/CorniceHitter.cs
  category=Game
  owner_system=Cornice
  risk_level=medium

- current_path=Assets/Scripts/SnowPanicMinimalSample.cs
  proposed_path=Assets/Scripts/Game/SnowPanicMinimalSample.cs
  category=Game
  owner_system=SnowPhysics
  risk_level=medium

- current_path=Assets/Scripts/CameraOrbit.cs
  proposed_path=Assets/Scripts/Game/CameraOrbit.cs
  category=Game
  owner_system=Camera
  risk_level=low

### Debug
- current_path=Assets/Scripts/AssiDebugUI.cs
  proposed_path=Assets/Scripts/Debug/AssiDebugUI.cs
  category=Debug
  owner_system=ASSI
  risk_level=low

- current_path=Assets/Scripts/DebugSnowVisibility.cs
  proposed_path=Assets/Scripts/Debug/DebugSnowVisibility.cs
  category=Debug
  owner_system=SnowPhysics
  risk_level=low

- current_path=Assets/Scripts/DebugDiagnostics.cs
  proposed_path=Assets/Scripts/Debug/DebugDiagnostics.cs
  category=Debug
  owner_system=ASSI
  risk_level=low

- current_path=Assets/Scripts/DebugScreenshotCapture.cs
  proposed_path=Assets/Scripts/Debug/DebugScreenshotCapture.cs
  category=Debug
  owner_system=ASSI
  risk_level=low

- current_path=Assets/Scripts/GridVisualWatchdog.cs
  proposed_path=Assets/Scripts/Debug/GridVisualWatchdog.cs
  category=Debug
  owner_system=SnowPhysics
  risk_level=medium

- current_path=Assets/Scripts/BugOriginTracker.cs
  proposed_path=Assets/Scripts/Debug/BugOriginTracker.cs
  category=Debug
  owner_system=ASSI
  risk_level=low

- current_path=Assets/Scripts/RollbackVerification.cs
  proposed_path=Assets/Scripts/Debug/RollbackVerification.cs
  category=Debug
  owner_system=ASSI
  risk_level=low

- current_path=Assets/Scripts/SnowMinReproBootstrap.cs
  proposed_path=Assets/Scripts/Debug/SnowMinReproBootstrap.cs
  category=Debug
  owner_system=SnowPhysics
  risk_level=low

- current_path=Assets/Scripts/SnowVerifyB2Debug.cs
  proposed_path=Assets/Scripts/Debug/SnowVerifyB2Debug.cs
  category=Debug
  owner_system=SnowPhysics
  risk_level=low

- current_path=Assets/Scripts/RoofSnowAngleProbe.cs
  proposed_path=Assets/Scripts/Debug/RoofSnowAngleProbe.cs
  category=Debug
  owner_system=RoofSnow
  risk_level=low

- current_path=Assets/Scripts/SnowVerifyPhaseA.cs
  proposed_path=Assets/Scripts/Debug/SnowVerifyPhaseA.cs
  category=Debug
  owner_system=SnowPhysics
  risk_level=low

- current_path=Assets/Scripts/SnowVerifyPhaseB.cs
  proposed_path=Assets/Scripts/Debug/SnowVerifyPhaseB.cs
  category=Debug
  owner_system=SnowPhysics
  risk_level=low

- current_path=Assets/Scripts/RoofAlignToSnow.cs
  proposed_path=Assets/Scripts/Debug/RoofAlignToSnow.cs
  category=Debug
  owner_system=RoofSnow
  risk_level=medium

- current_path=Assets/Scripts/SnowVerifyMinimalScene.cs
  proposed_path=Assets/Scripts/Debug/SnowVerifyMinimalScene.cs
  category=Debug
  owner_system=SnowPhysics
  risk_level=low

- current_path=Assets/Scripts/RendererWatch.cs
  proposed_path=Assets/Scripts/Debug/RendererWatch.cs
  category=Debug
  owner_system=ASSI
  risk_level=low

- current_path=Assets/Scripts/RoofSlideTestAutoSetup.cs
  proposed_path=Assets/Scripts/Debug/RoofSlideTestAutoSetup.cs
  category=Debug
  owner_system=RoofSnow
  risk_level=medium

- current_path=Assets/Scripts/RoofSnowReportWriter.cs
  proposed_path=Assets/Scripts/Debug/RoofSnowReportWriter.cs
  category=Debug
  owner_system=RoofSnow
  risk_level=low

- current_path=Assets/Scripts/DetachedSnowDiagnostics.cs
  proposed_path=Assets/Scripts/Debug/DetachedSnowDiagnostics.cs
  category=Debug
  owner_system=SnowPhysics
  risk_level=low

- current_path=Assets/Scripts/CabinRoofForceHide.cs
  proposed_path=Assets/Scripts/Debug/CabinRoofForceHide.cs
  category=Debug
  owner_system=RoofSnow
  risk_level=medium

- current_path=Assets/Scripts/SnowVerifyPhaseB1.cs
  proposed_path=Assets/Scripts/Debug/SnowVerifyPhaseB1.cs
  category=Debug
  owner_system=SnowPhysics
  risk_level=low

- current_path=Assets/Scripts/RoofDebugAutoSetup.cs
  proposed_path=Assets/Scripts/Debug/RoofDebugAutoSetup.cs
  category=Debug
  owner_system=RoofSnow
  risk_level=low

- current_path=Assets/Scripts/SnowSizeDiagnostics.cs
  proposed_path=Assets/Scripts/Debug/SnowSizeDiagnostics.cs
  category=Debug
  owner_system=SnowPhysics
  risk_level=low

- current_path=Assets/Scripts/RoofDebugGizmo.cs
  proposed_path=Assets/Scripts/Debug/RoofDebugGizmo.cs
  category=Debug
  owner_system=RoofSnow
  risk_level=low

- current_path=Assets/Scripts/SnowVerifyFixedScene.cs
  proposed_path=Assets/Scripts/Debug/SnowVerifyFixedScene.cs
  category=Debug
  owner_system=SnowPhysics
  risk_level=low

- current_path=Assets/Scripts/SnowVerifyPhaseB2.cs
  proposed_path=Assets/Scripts/Debug/SnowVerifyPhaseB2.cs
  category=Debug
  owner_system=SnowPhysics
  risk_level=low

### Editor (unchanged path)
- current_path=Assets/Scripts/Editor/AssiReportWindow.cs
  proposed_path=Assets/Scripts/Editor/AssiReportWindow.cs
  category=Editor
  owner_system=ASSI
  risk_level=low

- current_path=Assets/Scripts/Editor/SnowNoaReplyWindow.cs
  proposed_path=Assets/Scripts/Editor/SnowNoaReplyWindow.cs
  category=Editor
  owner_system=ASSI
  risk_level=low

- current_path=Assets/Scripts/Editor/SnowMinReproSceneBuilder.cs
  proposed_path=Assets/Scripts/Editor/SnowMinReproSceneBuilder.cs
  category=Editor
  owner_system=SnowPhysics
  risk_level=low

- current_path=Assets/Scripts/Editor/DioramaRoofSetup.cs
  proposed_path=Assets/Scripts/Editor/DioramaRoofSetup.cs
  category=Editor
  owner_system=Cornice
  risk_level=low

- current_path=Assets/Scripts/Editor/CorniceSceneSetup.cs
  proposed_path=Assets/Scripts/Editor/CorniceSceneSetup.cs
  category=Editor
  owner_system=Cornice
  risk_level=medium

- current_path=Assets/Scripts/Editor/SnowLoopNoaReportAutoCopy.cs
  proposed_path=Assets/Scripts/Editor/SnowLoopNoaReportAutoCopy.cs
  category=Editor
  owner_system=ASSI
  risk_level=medium

### Integration
- current_path=Assets/Scripts/SnowLoopLogCapture.cs
  proposed_path=Assets/Scripts/Integration/SnowLoopLogCapture.cs
  category=Integration
  owner_system=ASSI
  risk_level=high

- current_path=Assets/Scripts/ModifyTargetDeclaration.cs
  proposed_path=Assets/Scripts/Integration/ModifyTargetDeclaration.cs
  category=Integration
  owner_system=ASSI
  risk_level=low

- current_path=Assets/Scripts/DevelopmentStepTracker.cs
  proposed_path=Assets/Scripts/Integration/DevelopmentStepTracker.cs
  category=Integration
  owner_system=ASSI
  risk_level=low

- current_path=Assets/Scripts/AIPipelineTestCollector.cs
  proposed_path=Assets/Scripts/Integration/AIPipelineTestCollector.cs
  category=Integration
  owner_system=AIPipeline
  risk_level=low

- current_path=Assets/Scripts/VideoPipelineStopRequestor.cs
  proposed_path=Assets/Scripts/Integration/VideoPipelineStopRequestor.cs
  category=Integration
  owner_system=VideoPipeline
  risk_level=low

- current_path=Assets/Scripts/VideoPipelineSelfTestOverlay.cs
  proposed_path=Assets/Scripts/Integration/VideoPipelineSelfTestOverlay.cs
  category=Integration
  owner_system=VideoPipeline
  risk_level=low

- current_path=Assets/Scripts/VideoPipelineStopRequestorBehaviour.cs
  proposed_path=Assets/Scripts/Integration/VideoPipelineStopRequestorBehaviour.cs
  category=Integration
  owner_system=VideoPipeline
  risk_level=low

- current_path=Assets/Scripts/VideoPipelineSelfTestMode.cs
  proposed_path=Assets/Scripts/Integration/VideoPipelineSelfTestMode.cs
  category=Integration
  owner_system=VideoPipeline
  risk_level=low

- current_path=Assets/Scripts/CameraMatchAndSnowConfig.cs
  proposed_path=Assets/Scripts/Integration/CameraMatchAndSnowConfig.cs
  category=Integration
  owner_system=Camera
  risk_level=medium

- current_path=Assets/Scripts/UIBootstrap.cs
  proposed_path=Assets/Scripts/Integration/UIBootstrap.cs
  category=Integration
  owner_system=HUD
  risk_level=high

- current_path=Assets/Scripts/ScoreUiCheckEmitter.cs
  proposed_path=Assets/Scripts/Integration/ScoreUiCheckEmitter.cs
  category=Integration
  owner_system=ASSI
  risk_level=low

- current_path=Assets/Scripts/SnowDespawnLogger.cs
  proposed_path=Assets/Scripts/Integration/SnowDespawnLogger.cs
  category=Integration
  owner_system=ASSI
  risk_level=low

---

## [SAFE LAYER]

Do not touch unless explicitly requested. Compile-critical, report, and core bootstrap.

- SnowLoopLogCapture.cs
- ModifyTargetDeclaration.cs
- SnowMvpBootstrap.cs
- CorniceRuntimeSnowSetup.cs
- AvalanchePhysicsSystem.cs
- SnowPackSpawner.cs
- RoofSnowSystem.cs
- RunStructureManager.cs
- CoreGameplayManager.cs
- Editor/SnowLoopNoaReportAutoCopy.cs
- VideoPipelineSelfTestMode.cs
- VideoPipelineStopRequestor.cs
- VideoPipelineStopRequestorBehaviour.cs
- SnowLayerType.cs

---

## [WORK LAYER]

Actively modified during HUD/ASSI development. Higher change frequency.

- ScoreUiCheckEmitter.cs
- UnifiedHUD.cs
- UIBootstrap.cs
- AssiDebugUI.cs
- RunHUDUI.cs
- RunResultUI.cs
- SnowScoreDisplayUI.cs
- SnowPhysicsScoreManager.cs

---

*This is a planning document. No files have been moved.*
