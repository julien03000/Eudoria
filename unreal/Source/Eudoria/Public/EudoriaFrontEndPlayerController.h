#pragma once

#include "CoreMinimal.h"
#include "GameFramework/PlayerController.h"

#include "EudoriaFrontEndPlayerController.generated.h"

class UUserWidget;

/** Front-end player controller: mouse visible, UI-only input, shows one screen at a time. */
UCLASS()
class EUDORIA_API AEudoriaFrontEndPlayerController : public APlayerController
{
	GENERATED_BODY()

public:
	void ShowLogin(const FText& Notice, bool bIsError);
	void ShowRegister();
	void ShowWelcome();

protected:
	virtual void BeginPlay() override;

private:
	template <typename TScreen>
	TScreen* ShowScreen();

	UPROPERTY(Transient)
	TObjectPtr<UUserWidget> CurrentScreen;
};
