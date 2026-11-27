#pragma once

#include "CoreMinimal.h"
#include "UI/EudoriaScreenBase.h"

#include "EudoriaPauseMenu.generated.h"

/** Escape menu of the render test: resume, back to "Bienvenue", log out. Built in C++ like the other screens. */
UCLASS()
class EUDORIA_API UEudoriaPauseMenu : public UEudoriaScreenBase
{
	GENERATED_BODY()

protected:
	virtual void BuildContent() override;
	virtual FLinearColor GetBackgroundColor() const override;
	virtual FReply NativeOnKeyDown(const FGeometry& InGeometry, const FKeyEvent& InKeyEvent) override;

private:
	UFUNCTION()
	void HandleResumeClicked();

	UFUNCTION()
	void HandleWelcomeClicked();

	UFUNCTION()
	void HandleLogoutClicked();
};
