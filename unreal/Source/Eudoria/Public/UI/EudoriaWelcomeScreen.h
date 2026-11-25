#pragma once

#include "CoreMinimal.h"
#include "UI/EudoriaScreenBase.h"

#include "EudoriaWelcomeScreen.generated.h"

/** "Bienvenue, <pseudo>": profile read from the core (GET /me), logout button. */
UCLASS()
class EUDORIA_API UEudoriaWelcomeScreen : public UEudoriaScreenBase
{
	GENERATED_BODY()

protected:
	virtual void BuildContent() override;
	virtual void NativeConstruct() override;

private:
	UFUNCTION()
	void HandleLogoutClicked();

	UPROPERTY(Transient)
	TObjectPtr<UTextBlock> GreetingText;

	UPROPERTY(Transient)
	TObjectPtr<UTextBlock> DetailText;
};
