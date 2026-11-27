#pragma once

#include "CoreMinimal.h"
#include "GameFramework/PlayerController.h"

#include "EudoriaRenderTestPlayerController.generated.h"

class UEudoriaPauseMenu;
class UEudoriaRenderTestInput;

/**
 * Player controller of the render test: game input only, mouse hidden, keys of UEudoriaRenderTestInput,
 * Escape menu (resume, back to "Bienvenue", log out).
 */
UCLASS()
class EUDORIA_API AEudoriaRenderTestPlayerController : public APlayerController
{
	GENERATED_BODY()

public:
	/** The actions and keys of the render test (created on first use: the pawn may ask before BeginPlay). */
	UEudoriaRenderTestInput* GetRenderTestInput();

	void OpenPauseMenu();
	void ClosePauseMenu();
	bool IsPauseMenuOpen() const;

	/** Back to the front-end map: "Bienvenue" while the session is open. */
	void ReturnToFrontEnd();

	/** Logs out (tokens forgotten at once, the core told in the background), then back to the front-end. */
	void LogoutAndReturnToFrontEnd();

protected:
	virtual void BeginPlay() override;
	virtual void SetupInputComponent() override;

private:
	void UseGameInput();

	UPROPERTY(Transient)
	TObjectPtr<UEudoriaRenderTestInput> RenderTestInput;

	UPROPERTY(Transient)
	TObjectPtr<UEudoriaPauseMenu> PauseMenu;
};
