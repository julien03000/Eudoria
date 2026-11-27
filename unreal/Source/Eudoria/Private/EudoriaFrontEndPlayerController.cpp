#include "EudoriaFrontEndPlayerController.h"

#include "Blueprint/UserWidget.h"
#include "EudoriaSession.h"
#include "EudoriaSessionSubsystem.h"
#include "RenderTest/EudoriaRenderTestFlow.h"
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
		// Back from the render test with the session still open: "Bienvenue", not the login.
		const UGameInstance* GameInstance = GetGameInstance();
		const UEudoriaSessionSubsystem* Sessions = GameInstance ? GameInstance->GetSubsystem<UEudoriaSessionSubsystem>() : nullptr;
		const bool bLoggedIn = Sessions && Sessions->GetSession().IsLoggedIn();
		if (EudoriaRenderTestFlow::ScreenOnArrival(bLoggedIn) == EudoriaRenderTestFlow::EFrontEndScreen::Welcome)
		{
			ShowWelcome();
		}
		else
		{
			ShowLogin(FText::GetEmpty(), false);
		}
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
	if (UWidget* Focus = Screen->GetInitialFocus())
	{
		InputMode.SetWidgetToFocus(Focus->TakeWidget());
	}
	InputMode.SetLockMouseToViewportBehavior(EMouseLockMode::DoNotLock);
	SetInputMode(InputMode);
	return Screen;
}
