#include "UI/EudoriaScreenBase.h"

#include "Blueprint/WidgetTree.h"
#include "Components/Border.h"
#include "Components/Button.h"
#include "Components/EditableTextBox.h"
#include "Components/SizeBox.h"
#include "Components/TextBlock.h"
#include "Components/VerticalBox.h"
#include "Components/VerticalBoxSlot.h"
#include "EudoriaFrontEndPlayerController.h"
#include "EudoriaSessionSubsystem.h"
#include "Styling/CoreStyle.h"

#include UE_INLINE_GENERATED_CPP_BY_NAME(EudoriaScreenBase)

namespace
{
	const FLinearColor BackgroundColor(0.02f, 0.025f, 0.04f);
	const FLinearColor ErrorColor(1.0f, 0.45f, 0.4f);
	const FLinearColor InfoColor(0.75f, 0.8f, 0.9f);
	constexpr float ColumnWidth = 420.0f;
}

void UEudoriaScreenBase::NativeOnInitialized()
{
	Super::NativeOnInitialized();

	UBorder* Background = WidgetTree->ConstructWidget<UBorder>(UBorder::StaticClass(), TEXT("Background"));
	Background->SetBrushColor(BackgroundColor);
	Background->SetHorizontalAlignment(HAlign_Center);
	Background->SetVerticalAlignment(VAlign_Center);
	WidgetTree->RootWidget = Background;

	USizeBox* Width = WidgetTree->ConstructWidget<USizeBox>(USizeBox::StaticClass(), TEXT("Width"));
	Width->SetWidthOverride(ColumnWidth);
	Background->SetContent(Width);

	Column = WidgetTree->ConstructWidget<UVerticalBox>(UVerticalBox::StaticClass(), TEXT("Column"));
	Width->SetContent(Column);

	BuildContent();
}

UTextBlock* UEudoriaScreenBase::AddTitle(const FText& Text, int32 FontSize)
{
	UTextBlock* Title = WidgetTree->ConstructWidget<UTextBlock>(UTextBlock::StaticClass());
	Title->SetText(Text);
	Title->SetFont(FCoreStyle::GetDefaultFontStyle("Bold", FontSize));
	Title->SetJustification(ETextJustify::Center);
	Title->SetAutoWrapText(true);
	Column->AddChildToVerticalBox(Title)->SetPadding(FMargin(0.0f, 0.0f, 0.0f, 16.0f));
	return Title;
}

UEditableTextBox* UEudoriaScreenBase::AddTextField(const FText& Hint, bool bIsPassword)
{
	UEditableTextBox* Field = WidgetTree->ConstructWidget<UEditableTextBox>(UEditableTextBox::StaticClass());
	Field->SetHintText(Hint);
	Field->SetIsPassword(bIsPassword);
	Column->AddChildToVerticalBox(Field)->SetPadding(FMargin(0.0f, 4.0f));
	if (!FirstField)
	{
		FirstField = Field;
	}
	return Field;
}

UButton* UEudoriaScreenBase::AddButton(const FText& Label)
{
	UButton* Button = WidgetTree->ConstructWidget<UButton>(UButton::StaticClass());
	UTextBlock* Text = WidgetTree->ConstructWidget<UTextBlock>(UTextBlock::StaticClass());
	Text->SetText(Label);
	Text->SetColorAndOpacity(FSlateColor(FLinearColor::Black));
	Button->SetContent(Text);
	Column->AddChildToVerticalBox(Button)->SetPadding(FMargin(0.0f, 8.0f, 0.0f, 0.0f));
	Buttons.Add(Button);
	return Button;
}

UTextBlock* UEudoriaScreenBase::AddMessageLine()
{
	MessageLine = WidgetTree->ConstructWidget<UTextBlock>(UTextBlock::StaticClass(), TEXT("Message"));
	MessageLine->SetAutoWrapText(true);
	MessageLine->SetJustification(ETextJustify::Center);
	Column->AddChildToVerticalBox(MessageLine)->SetPadding(FMargin(0.0f, 16.0f, 0.0f, 0.0f));
	return MessageLine;
}

void UEudoriaScreenBase::ShowMessage(const FText& Message, bool bIsError)
{
	if (MessageLine)
	{
		MessageLine->SetColorAndOpacity(FSlateColor(bIsError ? ErrorColor : InfoColor));
		MessageLine->SetText(Message);
	}
}

void UEudoriaScreenBase::SetBusy(bool bBusy)
{
	for (UButton* Button : Buttons)
	{
		Button->SetIsEnabled(!bBusy);
	}
}

UWidget* UEudoriaScreenBase::GetInitialFocus() const
{
	if (FirstField)
	{
		return FirstField;
	}
	return Buttons.IsEmpty() ? nullptr : Buttons[0].Get();
}

AEudoriaFrontEndPlayerController* UEudoriaScreenBase::GetFrontEnd() const
{
	return Cast<AEudoriaFrontEndPlayerController>(GetOwningPlayer());
}

FEudoriaSession* UEudoriaScreenBase::GetSession() const
{
	const UGameInstance* GameInstance = GetGameInstance();
	UEudoriaSessionSubsystem* Subsystem = GameInstance ? GameInstance->GetSubsystem<UEudoriaSessionSubsystem>() : nullptr;
	return Subsystem ? &Subsystem->GetSession() : nullptr;
}
