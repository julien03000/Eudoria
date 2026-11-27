// Automation tests of the render test (sub-step 1.7): animation choice, keys, settings, character.
// Category: Eudoria.RenderTest. Run by tools/dev/test-unreal.ps1.

#include "Camera/CameraComponent.h"
#include "Engine/Engine.h"
#include "Engine/World.h"
#include "EnhancedActionKeyMapping.h"
#include "GameFramework/CharacterMovementComponent.h"
#include "GameFramework/SpringArmComponent.h"
#include "InputMappingContext.h"
#include "InputModifiers.h"
#include "Misc/AutomationTest.h"
#include "RenderTest/EudoriaLocomotion.h"
#include "RenderTest/EudoriaRenderTestInput.h"
#include "RenderTest/EudoriaRenderTestSettings.h"
#include "RenderTest/EudoriaThirdPersonCharacter.h"

#if WITH_DEV_AUTOMATION_TESTS

namespace EudoriaRenderTestTests
{
	constexpr EAutomationTestFlags TestFlags = EAutomationTestFlags::EditorContext | EAutomationTestFlags::ProductFilter;

	using namespace EudoriaLocomotion;

	FInput Ground(const FVector& Velocity, float Yaw = 0.0f)
	{
		return FInput{Velocity, Yaw, false};
	}

	FInput Air(float VerticalSpeed)
	{
		return FInput{FVector(0.0f, 0.0f, VerticalSpeed), 0.0f, true};
	}

	/** Value given by a mapping for a pressed 1D key, through its modifiers (as Enhanced Input does). */
	FVector2D KeyValue(const FEnhancedActionKeyMapping& Mapping)
	{
		FInputActionValue Value(EInputActionValueType::Axis2D, FVector(1.0f, 0.0f, 0.0f));
		for (const UInputModifier* Modifier : Mapping.Modifiers)
		{
			Value = Modifier->ModifyRaw(nullptr, Value, 0.016f);
		}
		return Value.Get<FVector2D>();
	}

	const FEnhancedActionKeyMapping* FindMapping(const UInputMappingContext& Context, const FKey& Key)
	{
		return Context.GetMappings().FindByPredicate([&Key](const FEnhancedActionKeyMapping& Mapping) { return Mapping.Key == Key; });
	}
}

using namespace EudoriaRenderTestTests;

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FEudoriaLocomotionStatesTest, "Eudoria.RenderTest.Locomotion.States", TestFlags)
bool FEudoriaLocomotionStatesTest::RunTest(const FString&)
{
	const FTimings Timings; // jump 0.87 s, land 0.87 s, interrupt above 50 cm/s

	FStatus Status;
	Status = Advance(Status, Ground(FVector(300.0f, 0.0f, 0.0f)), 0.1f, Timings);
	TestTrue(TEXT("walking stays on the ground"), Status.State == EState::Grounded);
	TestEqual(TEXT("time in state grows"), Status.SecondsInState, 0.1f);

	Status = Advance(Status, Air(500.0f), 0.1f, Timings);
	TestTrue(TEXT("leaving the ground upwards = jump"), Status.State == EState::Jumping);
	TestEqual(TEXT("time restarts on a new state"), Status.SecondsInState, 0.0f);

	Status = Advance(Status, Air(100.0f), 0.5f, Timings);
	TestTrue(TEXT("still jumping before the jump animation ends"), Status.State == EState::Jumping);
	Status = Advance(Status, Air(-200.0f), 0.5f, Timings);
	TestTrue(TEXT("falling once the jump animation is over"), Status.State == EState::Falling);

	Status = Advance(Status, Ground(FVector::ZeroVector), 0.016f, Timings);
	TestTrue(TEXT("touching the ground = landing"), Status.State == EState::Landing);
	Status = Advance(Status, Ground(FVector::ZeroVector), 0.5f, Timings);
	TestTrue(TEXT("landing plays while standing still"), Status.State == EState::Landing);
	Status = Advance(Status, Ground(FVector::ZeroVector), 0.5f, Timings);
	TestTrue(TEXT("back to the ground locomotion after the landing"), Status.State == EState::Grounded);

	// Walking off a ledge: no jump animation.
	FStatus Ledge = Advance(FStatus(), Air(-50.0f), 0.016f, Timings);
	TestTrue(TEXT("leaving the ground downwards = falling"), Ledge.State == EState::Falling);

	// Moving again cuts the landing short.
	FStatus Landing{EState::Landing, 0.1f};
	Landing = Advance(Landing, Ground(FVector(300.0f, 0.0f, 0.0f)), 0.016f, Timings);
	TestTrue(TEXT("moving ends the landing at once"), Landing.State == EState::Grounded);

	// A negative delta (should not happen) never makes time go back.
	const FStatus Same = Advance(FStatus{EState::Grounded, 1.0f}, Ground(FVector::ZeroVector), -1.0f, Timings);
	TestEqual(TEXT("negative delta ignored"), Same.SecondsInState, 1.0f);
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FEudoriaLocomotionBlendSpaceTest, "Eudoria.RenderTest.Locomotion.BlendSpacePosition", TestFlags)
bool FEudoriaLocomotionBlendSpaceTest::RunTest(const FString&)
{
	constexpr float Tolerance = 0.01f;
	TestTrue(TEXT("standing still = idle"), BlendSpacePosition(Ground(FVector::ZeroVector)).Equals(FVector2D::ZeroVector, Tolerance));
	TestTrue(TEXT("forward walk"), BlendSpacePosition(Ground(FVector(300.0f, 0.0f, 0.0f))).Equals(FVector2D(0.0f, 300.0f), Tolerance));
	TestTrue(TEXT("to its right = +90"), BlendSpacePosition(Ground(FVector(0.0f, 300.0f, 0.0f))).Equals(FVector2D(90.0f, 300.0f), Tolerance));
	TestTrue(TEXT("to its left = -90"), BlendSpacePosition(Ground(FVector(0.0f, -300.0f, 0.0f))).Equals(FVector2D(-90.0f, 300.0f), Tolerance));
	TestEqual(TEXT("backward = 180 (either sign)"), static_cast<float>(FMath::Abs(BlendSpacePosition(Ground(FVector(-300.0f, 0.0f, 0.0f))).X)), 180.0f, Tolerance);
	TestTrue(TEXT("relative to where it faces (yaw 90, moving +Y = forward)"),
		BlendSpacePosition(Ground(FVector(0.0f, 300.0f, 0.0f), 90.0f)).Equals(FVector2D(0.0f, 300.0f), Tolerance));
	TestEqual(TEXT("speed capped at the jog samples (600)"), static_cast<float>(BlendSpacePosition(Ground(FVector(900.0f, 0.0f, 0.0f))).Y), 600.0f, Tolerance);
	TestEqual(TEXT("vertical speed ignored"), static_cast<float>(BlendSpacePosition(Ground(FVector(300.0f, 0.0f, -400.0f))).Y), 300.0f, Tolerance);
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FEudoriaRenderTestKeysTest, "Eudoria.RenderTest.Input.Keys", TestFlags)
bool FEudoriaRenderTestKeysTest::RunTest(const FString&)
{
	const UEudoriaRenderTestInput* Input = UEudoriaRenderTestInput::Create(GetTransientPackage());
	if (!TestNotNull(TEXT("input created"), Input) || !TestNotNull(TEXT("mapping context"), Input->Context.Get()))
	{
		return false;
	}
	TestEqual(TEXT("one mapping per binding"), Input->Context->GetMappings().Num(), EudoriaRenderTestInput::DefaultBindings().Num());

	// ZQSD (French keyboard) and arrows: each key moves the right way.
	const TArray<TPair<FKey, FVector2D>> Expected = {
		{EKeys::Z, FVector2D(0.0f, 1.0f)}, {EKeys::S, FVector2D(0.0f, -1.0f)},
		{EKeys::Q, FVector2D(-1.0f, 0.0f)}, {EKeys::D, FVector2D(1.0f, 0.0f)},
		{EKeys::Up, FVector2D(0.0f, 1.0f)}, {EKeys::Down, FVector2D(0.0f, -1.0f)},
		{EKeys::Left, FVector2D(-1.0f, 0.0f)}, {EKeys::Right, FVector2D(1.0f, 0.0f)},
	};
	for (const TPair<FKey, FVector2D>& Pair : Expected)
	{
		const FEnhancedActionKeyMapping* Mapping = FindMapping(*Input->Context, Pair.Key);
		if (!TestNotNull(*FString::Printf(TEXT("%s is mapped"), *Pair.Key.ToString()), Mapping))
		{
			continue;
		}
		TestTrue(*FString::Printf(TEXT("%s moves"), *Pair.Key.ToString()), Mapping->Action == Input->Move);
		TestTrue(*FString::Printf(TEXT("%s direction"), *Pair.Key.ToString()), KeyValue(*Mapping).Equals(Pair.Value, 0.001f));
	}

	const TArray<TPair<FKey, const UInputAction*>> Others = {
		{EKeys::Gamepad_Left2D, Input->Move}, {EKeys::Mouse2D, Input->Look}, {EKeys::Gamepad_Right2D, Input->Look},
		{EKeys::SpaceBar, Input->Jump}, {EKeys::Gamepad_FaceButton_Bottom, Input->Jump},
		{EKeys::LeftShift, Input->Run}, {EKeys::Gamepad_LeftThumbstick, Input->Run},
	};
	for (const TPair<FKey, const UInputAction*>& Pair : Others)
	{
		const FEnhancedActionKeyMapping* Mapping = FindMapping(*Input->Context, Pair.Key);
		TestTrue(*FString::Printf(TEXT("%s bound to the right action"), *Pair.Key.ToString()), Mapping && Mapping->Action == Pair.Value);
	}

	TestTrue(TEXT("move and look are 2D"), Input->Move->ValueType == EInputActionValueType::Axis2D && Input->Look->ValueType == EInputActionValueType::Axis2D);
	TestTrue(TEXT("jump and run are buttons"), Input->Jump->ValueType == EInputActionValueType::Boolean && Input->Run->ValueType == EInputActionValueType::Boolean);
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FEudoriaRenderTestSettingsTest, "Eudoria.RenderTest.Settings.Configured", TestFlags)
bool FEudoriaRenderTestSettingsTest::RunTest(const FString&)
{
	const UEudoriaRenderTestSettings* Settings = GetDefault<UEudoriaRenderTestSettings>();
	const TArray<FString> Empty = Settings->FindEmptySettings();
	TestTrue(FString::Printf(TEXT("every setting filled in DefaultGame.ini (empty: %s)"), *FString::Join(Empty, TEXT(", "))), Empty.IsEmpty());
	TestTrue(TEXT("the map is project content"), Settings->Map.ToSoftObjectPath().GetLongPackageName().StartsWith(TEXT("/Game/")));

	// The content is out of git: absent on a fresh clone. Say so instead of passing silently.
	const TArray<FString> Missing = Settings->FindMissingContent();
	if (!Missing.IsEmpty())
	{
		AddWarning(FString::Printf(TEXT("Render test content not set up (%s): run tools/dev/setup-render-test.ps1."), *FString::Join(Missing, TEXT(", "))));
	}
	return true;
}

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FEudoriaRenderTestCharacterTest, "Eudoria.RenderTest.Character.Setup", TestFlags)
bool FEudoriaRenderTestCharacterTest::RunTest(const FString&)
{
	// A throwaway game world, never saved.
	UWorld* World = UWorld::CreateWorld(EWorldType::Game, false);
	FWorldContext& Context = GEngine->CreateNewWorldContext(EWorldType::Game);
	Context.SetCurrentWorld(World);

	AEudoriaThirdPersonCharacter* Character = World->SpawnActor<AEudoriaThirdPersonCharacter>();
	if (TestNotNull(TEXT("character spawned"), Character))
	{
		TestTrue(TEXT("camera on the spring arm"), Character->GetFollowCamera()->GetAttachParent() == Character->GetCameraBoom());
		TestTrue(TEXT("the spring arm follows the mouse"), Character->GetCameraBoom()->bUsePawnControlRotation);
		TestFalse(TEXT("the body does not turn with the camera"), Character->bUseControllerRotationYaw);
		TestTrue(TEXT("turns towards where it walks"), Character->GetCharacterMovement()->bOrientRotationToMovement);
		TestEqual(TEXT("walks at the walk samples speed"), Character->GetCharacterMovement()->MaxWalkSpeed, AEudoriaThirdPersonCharacter::WalkSpeed);
		Character->SetRunning(true);
		TestEqual(TEXT("runs at the jog samples speed"), Character->GetCharacterMovement()->MaxWalkSpeed, AEudoriaThirdPersonCharacter::RunSpeed);
		Character->SetRunning(false);
		TestEqual(TEXT("back to walking"), Character->GetCharacterMovement()->MaxWalkSpeed, AEudoriaThirdPersonCharacter::WalkSpeed);
		TestTrue(TEXT("animation driven by C++ (single node, no Animation Blueprint)"),
			Character->GetMesh()->GetAnimationMode() == EAnimationMode::AnimationSingleNode);

		if (GetDefault<UEudoriaRenderTestSettings>()->FindMissingCharacterContent().IsEmpty())
		{
			TestTrue(TEXT("mesh and animations loaded"), Character->ApplyContent());
			TestNotNull(TEXT("mannequin mesh assigned"), Character->GetMesh()->GetSkeletalMeshAsset());
			TestTrue(TEXT("starts on the ground locomotion"), Character->GetLocomotionState() == EudoriaLocomotion::EState::Grounded);
		}
		else
		{
			AddWarning(TEXT("Render test content not set up: mesh and animations not checked (run tools/dev/setup-render-test.ps1)."));
		}
	}

	GEngine->DestroyWorldContext(World);
	World->DestroyWorld(false);
	return true;
}

#endif
