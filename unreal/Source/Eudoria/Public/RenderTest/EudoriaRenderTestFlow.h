#pragma once

#include "CoreMinimal.h"

/**
 * Rules of the way in and out of the render test, as pure functions (testable without a world):
 * which front-end screen to show on arrival, and whether the "enter the test scene" button can be used.
 */
namespace EudoriaRenderTestFlow
{
	enum class EFrontEndScreen : uint8
	{
		Login,
		Welcome,
	};

	/** Coming back from the test scene with a session still open lands on "Bienvenue", not on the login. */
	EUDORIA_API EFrontEndScreen ScreenOnArrival(bool bIsLoggedIn);

	struct FEnterButton
	{
		bool bEnabled = false;
		/** Shown under the button when it is disabled; empty otherwise. */
		FText Reason;
	};

	/** The button is usable only when the test content is set up (MissingContent = names of what is missing). */
	EUDORIA_API FEnterButton EnterButtonState(const TArray<FString>& MissingContent);
}
