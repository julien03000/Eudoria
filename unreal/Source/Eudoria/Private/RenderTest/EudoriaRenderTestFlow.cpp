#include "RenderTest/EudoriaRenderTestFlow.h"

#define LOCTEXT_NAMESPACE "EudoriaRenderTestFlow"

namespace EudoriaRenderTestFlow
{
	EFrontEndScreen ScreenOnArrival(bool bIsLoggedIn)
	{
		return bIsLoggedIn ? EFrontEndScreen::Welcome : EFrontEndScreen::Login;
	}

	FEnterButton EnterButtonState(const TArray<FString>& MissingContent)
	{
		if (MissingContent.IsEmpty())
		{
			return FEnterButton{true, FText::GetEmpty()};
		}
		return FEnterButton{false, LOCTEXT("ContentMissing", "Scène de test absente : lancer tools/dev/setup-render-test.ps1 -BuildMap")};
	}
}

#undef LOCTEXT_NAMESPACE
