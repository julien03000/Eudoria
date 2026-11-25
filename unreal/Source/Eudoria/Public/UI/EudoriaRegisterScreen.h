#pragma once

#include "CoreMinimal.h"
#include "UI/EudoriaScreenBase.h"

#include "EudoriaRegisterScreen.generated.h"

/**
 * "Créer un compte": e-mail, pseudo, password and confirmation, then automatic login.
 * The core answers 202 the same way whether the e-mail is new or already used (plan 1.2), so when the
 * automatic login fails the player only gets a neutral message: nothing more than the login screen reveals.
 */
UCLASS()
class EUDORIA_API UEudoriaRegisterScreen : public UEudoriaScreenBase
{
	GENERATED_BODY()

protected:
	virtual void BuildContent() override;

private:
	UFUNCTION()
	void HandleCreateClicked();

	UFUNCTION()
	void HandleBackClicked();

	UFUNCTION()
	void HandleConfirmationCommitted(const FText& Text, ETextCommit::Type CommitMethod);

	void LogInAfterRegistration(const FString& Email, const FString& Password);

	UPROPERTY(Transient)
	TObjectPtr<UEditableTextBox> EmailField;

	UPROPERTY(Transient)
	TObjectPtr<UEditableTextBox> PseudoField;

	UPROPERTY(Transient)
	TObjectPtr<UEditableTextBox> PasswordField;

	UPROPERTY(Transient)
	TObjectPtr<UEditableTextBox> ConfirmationField;
};
