#include "UI/EudoriaRegisterScreen.h"

#include "Components/Button.h"
#include "Components/EditableTextBox.h"
#include "EudoriaFrontEndPlayerController.h"
#include "EudoriaSession.h"

#include UE_INLINE_GENERATED_CPP_BY_NAME(EudoriaRegisterScreen)

#define LOCTEXT_NAMESPACE "EudoriaRegisterScreen"

void UEudoriaRegisterScreen::BuildContent()
{
	AddTitle(LOCTEXT("Title", "EUDORIA"), 40);
	AddTitle(LOCTEXT("Subtitle", "Créer un compte"), 20);
	EmailField = AddTextField(LOCTEXT("Email", "Adresse e-mail"), false);
	PseudoField = AddTextField(LOCTEXT("Pseudo", "Pseudo (3 à 20 caractères, public)"), false);
	PasswordField = AddTextField(LOCTEXT("Password", "Mot de passe (12 caractères minimum)"), true);
	ConfirmationField = AddTextField(LOCTEXT("Confirmation", "Confirme le mot de passe"), true);
	ConfirmationField->OnTextCommitted.AddDynamic(this, &ThisClass::HandleConfirmationCommitted);
	AddButton(LOCTEXT("Create", "Créer mon compte"))->OnClicked.AddDynamic(this, &ThisClass::HandleCreateClicked);
	AddButton(LOCTEXT("Back", "Retour"))->OnClicked.AddDynamic(this, &ThisClass::HandleBackClicked);
	AddMessageLine();
}

void UEudoriaRegisterScreen::HandleConfirmationCommitted(const FText& Text, ETextCommit::Type CommitMethod)
{
	if (CommitMethod == ETextCommit::OnEnter)
	{
		HandleCreateClicked();
	}
}

void UEudoriaRegisterScreen::HandleCreateClicked()
{
	FEudoriaSession* Session = GetSession();
	if (!Session)
	{
		return;
	}
	const FString Email = EmailField->GetText().ToString().TrimStartAndEnd();
	const FString Pseudo = PseudoField->GetText().ToString().TrimStartAndEnd();
	const FString Password = PasswordField->GetText().ToString();
	const FString Confirmation = ConfirmationField->GetText().ToString();
	PasswordField->SetText(FText::GetEmpty());
	ConfirmationField->SetText(FText::GetEmpty());

	// Comfort checks only: the core alone decides what is valid (server-authoritative).
	if (Email.IsEmpty() || Pseudo.IsEmpty() || Password.IsEmpty())
	{
		ShowMessage(LOCTEXT("Missing", "Remplis l'e-mail, le pseudo et le mot de passe."), true);
		return;
	}
	if (!Password.Equals(Confirmation, ESearchCase::CaseSensitive))
	{
		ShowMessage(LOCTEXT("Mismatch", "Les deux mots de passe ne correspondent pas."), true);
		return;
	}

	SetBusy(true);
	ShowMessage(LOCTEXT("InProgress", "Création du compte…"), false);
	Session->Register(Email, Pseudo, Password,
		[WeakThis = TWeakObjectPtr<UEudoriaRegisterScreen>(this), Email, Password](const FEudoriaApiStatus& Result)
		{
			UEudoriaRegisterScreen* This = WeakThis.Get();
			if (!This)
			{
				return;
			}
			if (Result.IsSet())
			{
				This->SetBusy(false);
				This->ShowMessage(EudoriaCoreApi::ToUserMessage(Result.GetValue()), true);
				return;
			}
			This->LogInAfterRegistration(Email, Password);
		});
}

void UEudoriaRegisterScreen::LogInAfterRegistration(const FString& Email, const FString& Password)
{
	FEudoriaSession* Session = GetSession();
	if (!Session)
	{
		return;
	}
	ShowMessage(LOCTEXT("LoggingIn", "Connexion…"), false);
	Session->Login(Email, Password, [WeakThis = TWeakObjectPtr<UEudoriaRegisterScreen>(this)](const FEudoriaApiStatus& Result)
	{
		UEudoriaRegisterScreen* This = WeakThis.Get();
		AEudoriaFrontEndPlayerController* FrontEnd = This ? This->GetFrontEnd() : nullptr;
		if (!FrontEnd)
		{
			return;
		}
		if (Result.IsSet())
		{
			// Same message whether the e-mail was new or already used: reveal nothing.
			FrontEnd->ShowLogin(LOCTEXT("Neutral", "Compte créé, ou e-mail déjà utilisé : connecte-toi avec ton mot de passe."), false);
			return;
		}
		FrontEnd->ShowWelcome();
	});
}

void UEudoriaRegisterScreen::HandleBackClicked()
{
	if (AEudoriaFrontEndPlayerController* FrontEnd = GetFrontEnd())
	{
		FrontEnd->ShowLogin(FText::GetEmpty(), false);
	}
}

#undef LOCTEXT_NAMESPACE
