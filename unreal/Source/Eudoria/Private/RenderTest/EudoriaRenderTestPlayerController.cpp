#include "RenderTest/EudoriaRenderTestPlayerController.h"

#include "Engine/LocalPlayer.h"
#include "EnhancedInputComponent.h"
#include "EnhancedInputSubsystems.h"
#include "Eudoria.h"
#include "EudoriaSession.h"
#include "EudoriaSessionSubsystem.h"
#include "GameMapsSettings.h"
#include "Kismet/GameplayStatics.h"
#include "RenderTest/EudoriaRenderTestInput.h"
#include "UI/EudoriaPauseMenu.h"

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
	UseGameInput();
}

void AEudoriaRenderTestPlayerController::SetupInputComponent()
{
	Super::SetupInputComponent();

	if (UEnhancedInputComponent* Enhanced = Cast<UEnhancedInputComponent>(InputComponent))
	{
		Enhanced->BindAction(GetRenderTestInput()->Pause, ETriggerEvent::Started, this, &ThisClass::OpenPauseMenu);
	}
}

void AEudoriaRenderTestPlayerController::OpenPauseMenu()
{
	if (IsPauseMenuOpen())
	{
		return;
	}
	PauseMenu = CreateWidget<UEudoriaPauseMenu>(this, UEudoriaPauseMenu::StaticClass());
	if (!PauseMenu)
	{
		return;
	}
	PauseMenu->AddToViewport();

	// The world keeps running (a shared world will not pause for one player); only the input changes.
	FInputModeUIOnly InputMode;
	if (UWidget* Focus = PauseMenu->GetInitialFocus())
	{
		InputMode.SetWidgetToFocus(Focus->TakeWidget());
	}
	InputMode.SetLockMouseToViewportBehavior(EMouseLockMode::DoNotLock);
	SetInputMode(InputMode);
	SetShowMouseCursor(true);
}

void AEudoriaRenderTestPlayerController::ClosePauseMenu()
{
	if (PauseMenu)
	{
		PauseMenu->RemoveFromParent();
		PauseMenu = nullptr;
	}
	UseGameInput();
}

bool AEudoriaRenderTestPlayerController::IsPauseMenuOpen() const
{
	return PauseMenu && PauseMenu->IsInViewport();
}

void AEudoriaRenderTestPlayerController::ReturnToFrontEnd()
{
	UGameplayStatics::OpenLevel(this, FName(*UGameMapsSettings::GetGameDefaultMap()));
}

void AEudoriaRenderTestPlayerController::LogoutAndReturnToFrontEnd()
{
	const UGameInstance* GameInstance = GetGameInstance();
	if (UEudoriaSessionSubsystem* Sessions = GameInstance ? GameInstance->GetSubsystem<UEudoriaSessionSubsystem>() : nullptr)
	{
		// Same as the welcome screen: the tokens are forgotten at once (plan 1.3).
		Sessions->GetSession().Logout([](const FEudoriaApiStatus& Result)
		{
			if (Result.IsSet())
			{
				UE_LOG(LogEudoria, Warning, TEXT("The core did not confirm the logout (status %d)."), Result.GetValue().StatusCode);
			}
		});
	}
	ReturnToFrontEnd();
}

void AEudoriaRenderTestPlayerController::UseGameInput()
{
	SetShowMouseCursor(false);
	SetInputMode(FInputModeGameOnly());
}
