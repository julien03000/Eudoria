#include "RenderTest/EudoriaRenderTestPlayerController.h"

#include "EnhancedInputSubsystems.h"
#include "Engine/LocalPlayer.h"
#include "RenderTest/EudoriaRenderTestInput.h"

#include UE_INLINE_GENERATED_CPP_BY_NAME(EudoriaRenderTestPlayerController)

UEudoriaRenderTestInput* AEudoriaRenderTestPlayerController::GetRenderTestInput()
{
	if (!RenderTestInput)
	{
		RenderTestInput = UEudoriaRenderTestInput::Create(this);
	}
	return RenderTestInput;
}

void AEudoriaRenderTestPlayerController::BeginPlay()
{
	Super::BeginPlay();

	if (!IsLocalController())
	{
		return;
	}
	if (UEnhancedInputLocalPlayerSubsystem* Subsystem = ULocalPlayer::GetSubsystem<UEnhancedInputLocalPlayerSubsystem>(GetLocalPlayer()))
	{
		Subsystem->AddMappingContext(GetRenderTestInput()->Context, 0);
	}
	SetShowMouseCursor(false);
	SetInputMode(FInputModeGameOnly());
}
