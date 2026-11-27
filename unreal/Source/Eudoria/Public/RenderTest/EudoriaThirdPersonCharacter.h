#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Character.h"
#include "RenderTest/EudoriaLocomotion.h"

#include "EudoriaThirdPersonCharacter.generated.h"

class UAnimationAsset;
class UCameraComponent;
class USpringArmComponent;
struct FInputActionValue;

/**
 * Third-person character of the render test (sub-step 1.7): camera on a spring arm behind the shoulder,
 * movement relative to the camera, walk / run (Shift) / jump (Space). Everything is C++: no Blueprint,
 * no Animation Blueprint (plan 1.7, decision D3). Assets come from UEudoriaRenderTestSettings.
 */
UCLASS(Config = Game)
class EUDORIA_API AEudoriaThirdPersonCharacter : public ACharacter
{
	GENERATED_BODY()

public:
	/** Speeds of the locomotion blend space samples (walk 300, jog 600): no foot sliding. */
	static constexpr float WalkSpeed = 300.0f;
	static constexpr float RunSpeed = 600.0f;

	AEudoriaThirdPersonCharacter();

	/**
	 * Loads the mesh and animations named in the settings and starts the ground locomotion.
	 * Returns false (and logs what is missing) when the content is not set up.
	 */
	bool ApplyContent();

	bool HasContent() const { return bHasContent; }
	EudoriaLocomotion::EState GetLocomotionState() const { return Locomotion.State; }
	USpringArmComponent* GetCameraBoom() const { return CameraBoom; }
	UCameraComponent* GetFollowCamera() const { return FollowCamera; }

	/** Run on (Shift held) or off. */
	void SetRunning(bool bRun);

	virtual void Tick(float DeltaSeconds) override;

protected:
	virtual void BeginPlay() override;
	virtual void SetupPlayerInputComponent(UInputComponent* PlayerInputComponent) override;

private:
	void HandleMove(const FInputActionValue& Value);
	void HandleLook(const FInputActionValue& Value);
	void HandleRunStarted(const FInputActionValue& Value);
	void HandleRunCompleted(const FInputActionValue& Value);

	/** Plays the animation of the new state (the blend space for Grounded). */
	void PlayStateAnimation(EudoriaLocomotion::EState State);

	UPROPERTY(VisibleAnywhere, Category = "Camera")
	TObjectPtr<USpringArmComponent> CameraBoom;

	UPROPERTY(VisibleAnywhere, Category = "Camera")
	TObjectPtr<UCameraComponent> FollowCamera;

	UPROPERTY(Transient)
	TObjectPtr<UAnimationAsset> LocomotionAsset;

	UPROPERTY(Transient)
	TObjectPtr<UAnimationAsset> JumpAsset;

	UPROPERTY(Transient)
	TObjectPtr<UAnimationAsset> FallAsset;

	UPROPERTY(Transient)
	TObjectPtr<UAnimationAsset> LandAsset;

	EudoriaLocomotion::FStatus Locomotion;
	EudoriaLocomotion::FTimings Timings;
	bool bHasContent = false;
};
