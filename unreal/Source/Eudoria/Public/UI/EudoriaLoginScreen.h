#pragma once

#include "CoreMinimal.h"
#include "UI/EudoriaScreenBase.h"

#include "EudoriaLoginScreen.generated.h"

/** "Se connecter": e-mail, password, link to account creation. */
UCLASS()
class EUDORIA_API UEudoriaLoginScreen : public UEudoriaScreenBase
{
	GENERATED_BODY()

public:
	/** Message shown on arrival (e.g. after a logout or an expired session). */
	void ShowNotice(const FText& Notice, bool bIsError);

protected:
	virtual void BuildContent() override;

private:
	UFUNCTION()
	void HandleLoginClicked();

	UFUNCTION()
	void HandleRegisterClicked();

	UFUNCTION()
	void HandlePasswordCommitted(const FText& Text, ETextCommit::Type CommitMethod);

	UPROPERTY(Transient)
	TObjectPtr<UEditableTextBox> EmailField;

	UPROPERTY(Transient)
	TObjectPtr<UEditableTextBox> PasswordField;
};
