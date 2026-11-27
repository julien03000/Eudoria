#include "UI/EudoriaPauseMenu.h"

#include "Components/Button.h"
#include "RenderTest/EudoriaRenderTestPlayerController.h"

#include UE_INLINE_GENERATED_CPP_BY_NAME(EudoriaPauseMenu)

#define LOCTEXT_NAMESPACE "EudoriaPauseMenu"

void UEudoriaPauseMenu::BuildContent()
{
	AddTitle(LOCTEXT("Title", "Pause"), 32);
	AddButton(LOCTEXT("Resume", "Reprendre"))->OnClicked.AddDynamic(this, &ThisClass::HandleResumeClicked);
	AddButton(LOCTEXT("Welcome", "Retour à l'accueil"))->OnClicked.AddDynamic(this, &ThisClass::HandleWelcomeClicked);
	AddButton(LOCTEXT("Logout", "Se déconnecter"))->OnClicked.AddDynamic(this, &ThisClass::HandleLogoutClicked);
}

FLinearColor UEudoriaPauseMenu::GetBackgroundColor() const
{
	return FLinearColor(0.0f, 0.0f, 0.0f, 0.6f); // the scene stays visible behind the menu
}

FReply UEudoriaPauseMenu::NativeOnKeyDown(const FGeometry& InGeometry, const FKeyEvent& InKeyEvent)
{
	// While the menu is open the game input is off: Escape (or the gamepad's Start) closes it here.
	if (InKeyEvent.GetKey() == EKeys::Escape || InKeyEvent.GetKey() == EKeys::Gamepad_Special_Right)
	{
		HandleResumeClicked();
		return FReply::Handled();
	}
	return Super::NativeOnKeyDown(InGeometry, InKeyEvent);
}

void UEudoriaPauseMenu::HandleResumeClicked()
{
	if (AEudoriaRenderTestPlayerController* PlayerController = Cast<AEudoriaRenderTestPlayerController>(GetOwningPlayer()))
	{
		PlayerController->ClosePauseMenu();
	}
}

void UEudoriaPauseMenu::HandleWelcomeClicked()
{
	if (AEudoriaRenderTestPlayerController* PlayerController = Cast<AEudoriaRenderTestPlayerController>(GetOwningPlayer()))
	{
		PlayerController->ReturnToFrontEnd();
	}
}

void UEudoriaPauseMenu::HandleLogoutClicked()
{
	SetBusy(true);
	if (AEudoriaRenderTestPlayerController* PlayerController = Cast<AEudoriaRenderTestPlayerController>(GetOwningPlayer()))
	{
		PlayerController->LogoutAndReturnToFrontEnd();
	}
}

#undef LOCTEXT_NAMESPACE
