#include "RenderTest/EudoriaRenderTestInput.h"

#include "InputAction.h"
#include "InputMappingContext.h"
#include "InputModifiers.h"

#include UE_INLINE_GENERATED_CPP_BY_NAME(EudoriaRenderTestInput)

namespace EudoriaRenderTestInput
{
	TArray<FBinding> DefaultBindings()
	{
		// A 1D key gives (1, 0): forward needs (0, 1) -> swizzle; backward (0, -1) -> negate then swizzle.
		return {
			{EKeys::Z, EAction::Move, false, false, true},
			{EKeys::S, EAction::Move, true, false, true},
			{EKeys::Q, EAction::Move, true, false, false},
			{EKeys::D, EAction::Move, false, false, false},
			{EKeys::Up, EAction::Move, false, false, true},
			{EKeys::Down, EAction::Move, true, false, true},
			{EKeys::Left, EAction::Move, true, false, false},
			{EKeys::Right, EAction::Move, false, false, false},
			{EKeys::Gamepad_Left2D, EAction::Move},

			// Mouse/stick up gives +Y; the camera pitch goes the other way (as in Epic's third-person template).
			{EKeys::Mouse2D, EAction::Look, false, true, false},
			{EKeys::Gamepad_Right2D, EAction::Look, false, true, false},

			{EKeys::SpaceBar, EAction::Jump},
			{EKeys::Gamepad_FaceButton_Bottom, EAction::Jump},

			{EKeys::LeftShift, EAction::Run},
			{EKeys::Gamepad_LeftThumbstick, EAction::Run},

			{EKeys::Escape, EAction::Pause},
			{EKeys::Gamepad_Special_Right, EAction::Pause},
		};
	}
}

namespace
{
	UInputAction* NewAction(UObject* Outer, const TCHAR* Name, EInputActionValueType Type)
	{
		UInputAction* Action = NewObject<UInputAction>(Outer, Name);
		Action->ValueType = Type;
		return Action;
	}
}

UEudoriaRenderTestInput* UEudoriaRenderTestInput::Create(UObject* Outer)
{
	using namespace EudoriaRenderTestInput;

	UEudoriaRenderTestInput* Input = NewObject<UEudoriaRenderTestInput>(Outer);
	Input->Move = NewAction(Input, TEXT("IA_Move"), EInputActionValueType::Axis2D);
	Input->Look = NewAction(Input, TEXT("IA_Look"), EInputActionValueType::Axis2D);
	Input->Jump = NewAction(Input, TEXT("IA_Jump"), EInputActionValueType::Boolean);
	Input->Run = NewAction(Input, TEXT("IA_Run"), EInputActionValueType::Boolean);
	Input->Pause = NewAction(Input, TEXT("IA_Pause"), EInputActionValueType::Boolean);
	Input->Context = NewObject<UInputMappingContext>(Input, TEXT("IMC_RenderTest"));

	for (const FBinding& Binding : DefaultBindings())
	{
		FEnhancedActionKeyMapping& Mapping = Input->Context->MapKey(Input->GetAction(Binding.Action), Binding.Key);
		if (Binding.bNegateX || Binding.bNegateY)
		{
			UInputModifierNegate* Negate = NewObject<UInputModifierNegate>(Input->Context);
			Negate->bX = Binding.bNegateX;
			Negate->bY = Binding.bNegateY;
			Negate->bZ = false;
			Mapping.Modifiers.Add(Negate);
		}
		if (Binding.bSwizzleYX)
		{
			UInputModifierSwizzleAxis* Swizzle = NewObject<UInputModifierSwizzleAxis>(Input->Context);
			Swizzle->Order = EInputAxisSwizzle::YXZ;
			Mapping.Modifiers.Add(Swizzle);
		}
	}
	return Input;
}

const UInputAction* UEudoriaRenderTestInput::GetAction(EudoriaRenderTestInput::EAction Action) const
{
	switch (Action)
	{
	case EudoriaRenderTestInput::EAction::Move:
		return Move;
	case EudoriaRenderTestInput::EAction::Look:
		return Look;
	case EudoriaRenderTestInput::EAction::Jump:
		return Jump;
	case EudoriaRenderTestInput::EAction::Run:
		return Run;
	case EudoriaRenderTestInput::EAction::Pause:
		return Pause;
	}
	return nullptr;
}
