#include "RenderTest/EudoriaRenderTestGameMode.h"

#include "GameFramework/HUD.h"
#include "RenderTest/EudoriaRenderTestPlayerController.h"
#include "RenderTest/EudoriaThirdPersonCharacter.h"

#include UE_INLINE_GENERATED_CPP_BY_NAME(EudoriaRenderTestGameMode)

AEudoriaRenderTestGameMode::AEudoriaRenderTestGameMode()
{
	DefaultPawnClass = AEudoriaThirdPersonCharacter::StaticClass();
	PlayerControllerClass = AEudoriaRenderTestPlayerController::StaticClass();
	HUDClass = AHUD::StaticClass(); // empty HUD: no "SpawnActor failed" warning at start
}
