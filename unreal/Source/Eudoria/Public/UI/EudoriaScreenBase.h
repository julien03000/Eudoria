#pragma once

#include "Blueprint/UserWidget.h"
#include "CoreMinimal.h"

#include "EudoriaScreenBase.generated.h"

class AEudoriaFrontEndPlayerController;
class FEudoriaSession;
class UButton;
class UEditableTextBox;
class UTextBlock;
class UVerticalBox;

/**
 * Base of the front-end screens. The layout is built in C++ (no Widget Blueprint, plan 1.5 decision 1):
 * a centered column on a dark background, filled by each screen in BuildContent().
 */
UCLASS(Abstract)
class EUDORIA_API UEudoriaScreenBase : public UUserWidget
{
	GENERATED_BODY()

public:
	/** Widget that gets keyboard focus when the screen appears: the first text field, else the first button. */
	UWidget* GetInitialFocus() const;

protected:
	virtual void NativeOnInitialized() override;

	/** Colour behind the column (opaque for the front-end screens). */
	virtual FLinearColor GetBackgroundColor() const;

	/** Adds the screen's widgets to the column (AddTitle, AddTextField...). */
	virtual void BuildContent() PURE_VIRTUAL(UEudoriaScreenBase::BuildContent, );

	UTextBlock* AddTitle(const FText& Text, int32 FontSize);
	UEditableTextBox* AddTextField(const FText& Hint, bool bIsPassword);
	UButton* AddButton(const FText& Label);
	/** Line used for errors and progress messages; empty at first. */
	UTextBlock* AddMessageLine();

	/** Shows a message (error or progress) on the message line. */
	void ShowMessage(const FText& Message, bool bIsError);

	/** Disables every button while a request is on its way (no double sending). */
	void SetBusy(bool bBusy);

	AEudoriaFrontEndPlayerController* GetFrontEnd() const;
	/** The player's session; null only if the game instance is gone. */
	FEudoriaSession* GetSession() const;

private:
	UPROPERTY(Transient)
	TObjectPtr<UVerticalBox> Column;

	UPROPERTY(Transient)
	TObjectPtr<UTextBlock> MessageLine;

	UPROPERTY(Transient)
	TArray<TObjectPtr<UButton>> Buttons;

	UPROPERTY(Transient)
	TObjectPtr<UEditableTextBox> FirstField;
};
