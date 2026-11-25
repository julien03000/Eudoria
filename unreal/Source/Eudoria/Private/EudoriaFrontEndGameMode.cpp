#include "EudoriaFrontEndGameMode.h"

#include "EudoriaFrontEndPlayerController.h"

#include UE_INLINE_GENERATED_CPP_BY_NAME(EudoriaFrontEndGameMode)

AEudoriaFrontEndGameMode::AEudoriaFrontEndGameMode()
{
	PlayerControllerClass = AEudoriaFrontEndPlayerController::StaticClass();
	DefaultPawnClass = nullptr;
	HUDClass = nullptr;
}
