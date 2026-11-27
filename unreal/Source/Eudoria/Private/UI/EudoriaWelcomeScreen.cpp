#include "UI/EudoriaWelcomeScreen.h"

#include "Components/Button.h"
#include "Components/TextBlock.h"
#include "Eudoria.h"
#include "EudoriaFrontEndPlayerController.h"
#include "EudoriaSession.h"
#include "Kismet/GameplayStatics.h"
#include "RenderTest/EudoriaRenderTestFlow.h"
#include "RenderTest/EudoriaRenderTestSettings.h"

#include UE_INLINE_GENERATED_CPP_BY_NAME(EudoriaWelcomeScreen)

#define LOCTEXT_NAMESPACE "EudoriaWelcomeScreen"

void UEudoriaWelcomeScreen::BuildContent()
{
	GreetingText = AddTitle(LOCTEXT("Loading", "Chargement du profil…"), 32);
	DetailText = AddTitle(FText::GetEmpty(), 16);
	UButton* Enter = AddButton(LOCTEXT("EnterRenderTest", "Entrer dans la scène de test"));
	Enter->OnClicked.AddDynamic(this, &ThisClass::HandleEnterRenderTestClicked);
	AddButton(LOCTEXT("Logout", "Se déconnecter"))->OnClicked.AddDynamic(this, &ThisClass::HandleLogoutClicked);
	AddMessageLine();

	const EudoriaRenderTestFlow::FEnterButton State =
		EudoriaRenderTestFlow::EnterButtonState(GetDefault<UEudoriaRenderTestSettings>()->FindMissingContent());
	if (!State.bEnabled)
	{
		Enter->SetIsEnabled(false);
		ShowMessage(State.Reason, false);
	}
}

void UEudoriaWelcomeScreen::NativeConstruct()
{
	Super::NativeConstruct();

	FEudoriaSession* Session = GetSession();
	if (!Session)
	{
		return;
	}
	Session->FetchProfile([WeakThis = TWeakObjectPtr<UEudoriaWelcomeScreen>(this)](const TValueOrError<FEudoriaProfile, FEudoriaApiError>& Result)
	{
		UEudoriaWelcomeScreen* This = WeakThis.Get();
		if (!This || !This->IsInViewport())
		{
			return; // the player left this screen (e.g. logged out) before the answer came
		}
		if (Result.HasError())
		{
			const FEudoriaApiError& Error = Result.GetError();
			if (Error.Kind == EEudoriaApiErrorKind::SessionEnded)
			{
				if (AEudoriaFrontEndPlayerController* FrontEnd = This->GetFrontEnd())
				{
					FrontEnd->ShowLogin(EudoriaCoreApi::ToUserMessage(Error), true);
				}
				return;
			}
			This->GreetingText->SetText(LOCTEXT("NoProfile", "Profil indisponible"));
			This->ShowMessage(EudoriaCoreApi::ToUserMessage(Error), true);
			return;
		}

		const FEudoriaProfile& Profile = Result.GetValue();
		This->GreetingText->SetText(FText::Format(LOCTEXT("Greeting", "Bienvenue, {0}"), FText::FromString(Profile.Pseudo)));
		This->DetailText->SetText(FText::Format(LOCTEXT("CreatedAt", "Membre depuis le {0}"), FText::AsDate(Profile.CreatedAt, EDateTimeStyle::Long)));
	});
}

void UEudoriaWelcomeScreen::HandleEnterRenderTestClicked()
{
	SetBusy(true);
	// The session lives in the game instance: it survives the change of map.
	UGameplayStatics::OpenLevelBySoftObjectPtr(this, GetDefault<UEudoriaRenderTestSettings>()->Map);
}

void UEudoriaWelcomeScreen::HandleLogoutClicked()
{
	FEudoriaSession* Session = GetSession();
	if (!Session)
	{
		return;
	}
	// The tokens are forgotten at once; the core is told in the background (a failure only leaves the
	// session open on the core until it expires, as decided in plan 1.3).
	Session->Logout([](const FEudoriaApiStatus& Result)
	{
		if (Result.IsSet())
		{
			UE_LOG(LogEudoria, Warning, TEXT("The core did not confirm the logout (status %d)."), Result.GetValue().StatusCode);
		}
	});
	if (AEudoriaFrontEndPlayerController* FrontEnd = GetFrontEnd())
	{
		FrontEnd->ShowLogin(LOCTEXT("LoggedOut", "Tu es déconnecté."), false);
	}
}

#undef LOCTEXT_NAMESPACE
