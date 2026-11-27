#pragma once

#include "CoreMinimal.h"
#include "InputCoreTypes.h"
#include "UObject/Object.h"

#include "EudoriaRenderTestInput.generated.h"

class UInputAction;
class UInputMappingContext;

/**
 * Keys of the render test, as data (testable without any engine object). Built in C++: no input asset
 * (plan 1.7, § 6.3). Decision D8: ZQSD (French keyboard) and arrows, mouse, gamepad.
 */
namespace EudoriaRenderTestInput
{
	enum class EAction : uint8
	{
		Move, // 2D: X = right, Y = forward
		Look, // 2D: X = yaw, Y = pitch
		Jump,
		Run,  // held
		Pause, // opens the Escape menu
	};

	struct FBinding
	{
		FKey Key;
		EAction Action;
		/** Applied in this order: negate X (1D keys) or Y (mouse/stick pitch), then swap X and Y. */
		bool bNegateX = false;
		bool bNegateY = false;
		bool bSwizzleYX = false;
	};

	EUDORIA_API TArray<FBinding> DefaultBindings();
}

/** The input actions and mapping context of the render test, created at run time from DefaultBindings(). */
UCLASS()
class EUDORIA_API UEudoriaRenderTestInput : public UObject
{
	GENERATED_BODY()

public:
	static UEudoriaRenderTestInput* Create(UObject* Outer);

	const UInputAction* GetAction(EudoriaRenderTestInput::EAction Action) const;

	UPROPERTY(Transient)
	TObjectPtr<UInputMappingContext> Context;

	UPROPERTY(Transient)
	TObjectPtr<UInputAction> Move;

	UPROPERTY(Transient)
	TObjectPtr<UInputAction> Look;

	UPROPERTY(Transient)
	TObjectPtr<UInputAction> Jump;

	UPROPERTY(Transient)
	TObjectPtr<UInputAction> Run;

	UPROPERTY(Transient)
	TObjectPtr<UInputAction> Pause;
};
