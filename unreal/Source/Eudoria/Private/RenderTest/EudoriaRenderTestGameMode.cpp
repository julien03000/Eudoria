#include "RenderTest/EudoriaRenderTestGameMode.h"

#include "RenderTest/EudoriaRenderTestPlayerController.h"
#include "RenderTest/EudoriaThirdPersonCharacter.h"

#include UE_INLINE_GENERATED_CPP_BY_NAME(EudoriaRenderTestGameMode)

AEudoriaRenderTestGameMode::AEudoriaRenderTestGameMode()
{
	DefaultPawnClass = AEudoriaThirdPersonCharacter::StaticClass();
	PlayerControllerClass = AEudoriaRenderTestPlayerController::StaticClass();
	HUDClass = nullptr;
}
