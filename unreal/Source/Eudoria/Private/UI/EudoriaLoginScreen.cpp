#include "UI/EudoriaLoginScreen.h"

#include "Components/Button.h"
#include "Components/EditableTextBox.h"
#include "EudoriaFrontEndPlayerController.h"
#include "EudoriaSession.h"

#include UE_INLINE_GENERATED_CPP_BY_NAME(EudoriaLoginScreen)

#define LOCTEXT_NAMESPACE "EudoriaLoginScreen"

void UEudoriaLoginScreen::BuildContent()
{
	AddTitle(LOCTEXT("Title", "EUDORIA"), 40);
	AddTitle(LOCTEXT("Subtitle", "Se connecter"), 20);
	EmailField = AddTextField(LOCTEXT("Email", "Adresse e-mail"), false);
	PasswordField = AddTextField(LOCTEXT("Password", "Mot de passe"), true);
	PasswordField->OnTextCommitted.AddDynamic(this, &ThisClass::HandlePasswordCommitted);
	AddButton(LOCTEXT("Login", "Se connecter"))->OnClicked.AddDynamic(this, &ThisClass::HandleLoginClicked);
	AddButton(LOCTEXT("Register", "Créer un compte"))->OnClicked.AddDynamic(this, &ThisClass::HandleRegisterClicked);
	AddMessageLine();
}

void UEudoriaLoginScreen::ShowNotice(const FText& Notice, bool bIsError)
{
	ShowMessage(Notice, bIsError);
}

void UEudoriaLoginScreen::HandlePasswordCommitted(const FText& Text, ETextCommit::Type CommitMethod)
{
	if (CommitMethod == ETextCommit::OnEnter)
	{
		HandleLoginClicked();
	}
}

void UEudoriaLoginScreen::HandleLoginClicked()
{
	FEudoriaSession* Session = GetSession();
	if (!Session)
	{
		return;
	}
	const FString Email = EmailField->GetText().ToString().TrimStartAndEnd();
	const FString Password = PasswordField->GetText().ToString();
	PasswordField->SetText(FText::GetEmpty()); // the password never stays on screen after sending
	if (Email.IsEmpty() || Password.IsEmpty())
	{
		ShowMessage(LOCTEXT("Missing", "Saisis ton adresse e-mail et ton mot de passe."), true);
		return;
	}

	SetBusy(true);
	ShowMessage(LOCTEXT("InProgress", "Connexion…"), false);
	Session->Login(Email, Password, [WeakThis = TWeakObjectPtr<UEudoriaLoginScreen>(this)](const FEudoriaApiStatus& Result)
	{
		UEudoriaLoginScreen* This = WeakThis.Get();
		if (!This)
		{
			return;
		}
		This->SetBusy(false);
		if (Result.IsSet())
		{
			This->ShowMessage(EudoriaCoreApi::ToUserMessage(Result.GetValue()), true);
			return;
		}
		if (AEudoriaFrontEndPlayerController* FrontEnd = This->GetFrontEnd())
		{
			FrontEnd->ShowWelcome();
		}
	});
}

void UEudoriaLoginScreen::HandleRegisterClicked()
{
	if (AEudoriaFrontEndPlayerController* FrontEnd = GetFrontEnd())
	{
		FrontEnd->ShowRegister();
	}
}

#undef LOCTEXT_NAMESPACE
