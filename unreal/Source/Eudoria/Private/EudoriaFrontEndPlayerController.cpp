#include "EudoriaFrontEndPlayerController.h"

#include "Blueprint/UserWidget.h"
#include "UI/EudoriaLoginScreen.h"
#include "UI/EudoriaRegisterScreen.h"
#include "UI/EudoriaWelcomeScreen.h"

#include UE_INLINE_GENERATED_CPP_BY_NAME(EudoriaFrontEndPlayerController)

void AEudoriaFrontEndPlayerController::BeginPlay()
{
	Super::BeginPlay();

	if (IsLocalController())
	{
		SetShowMouseCursor(true);
		ShowLogin(FText::GetEmpty(), false);
	}
}

void AEudoriaFrontEndPlayerController::ShowLogin(const FText& Notice, bool bIsError)
{
	if (UEudoriaLoginScreen* Screen = ShowScreen<UEudoriaLoginScreen>())
	{
		Screen->ShowNotice(Notice, bIsError);
	}
}

void AEudoriaFrontEndPlayerController::ShowRegister()
{
	ShowScreen<UEudoriaRegisterScreen>();
}

void AEudoriaFrontEndPlayerController::ShowWelcome()
{
	ShowScreen<UEudoriaWelcomeScreen>();
}

template <typename TScreen>
TScreen* AEudoriaFrontEndPlayerController::ShowScreen()
{
	if (CurrentScreen)
	{
		CurrentScreen->RemoveFromParent();
		CurrentScreen = nullptr;
	}

	TScreen* Screen = CreateWidget<TScreen>(this, TScreen::StaticClass());
	if (!Screen)
	{
		return nullptr;
	}
	Screen->AddToViewport();
	CurrentScreen = Screen;

	FInputModeUIOnly InputMode;
	InputMode.SetWidgetToFocus(Screen->TakeWidget());
	InputMode.SetLockMouseToViewportBehavior(EMouseLockMode::DoNotLock);
	SetInputMode(InputMode);
	return Screen;
}
