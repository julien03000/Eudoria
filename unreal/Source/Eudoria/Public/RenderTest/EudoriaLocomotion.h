#pragma once

#include "CoreMinimal.h"

/**
 * Animation choice of the render-test character, as pure functions (no engine object): which animation
 * to play (ground locomotion, jump, fall, landing) and where in the locomotion blend space.
 *
 * Plan 1.7, decision D3: the mesh plays one animation at a time ("single node" mode), driven by C++;
 * no Animation Blueprint. The rules for Animation Blueprints are an open point of ADR-0003 (step 2).
 */
namespace EudoriaLocomotion
{
	enum class EState : uint8
	{
		Grounded, // blend space BS_Idle_Walk_Run (idle, walk, jog in 8 directions)
		Jumping,  // MM_Jump, once
		Falling,  // MM_Fall_Loop, looped
		Landing,  // MM_Land, once (cut short if the player moves again)
	};

	struct FInput
	{
		FVector Velocity = FVector::ZeroVector;
		float ActorYawDegrees = 0.0f;
		bool bIsFalling = false;
	};

	struct FTimings
	{
		float JumpSeconds = 0.87f;          // length of MM_Jump
		float LandSeconds = 0.87f;          // length of MM_Land
		float LandInterruptSpeed = 50.0f;   // cm/s: moving again ends the landing at once
	};

	struct FStatus
	{
		EState State = EState::Grounded;
		float SecondsInState = 0.0f;
	};

	/** Axes of BS_Idle_Walk_Run (read from the asset on 2026-11-27): X = direction, Y = speed. */
	constexpr float MaxBlendSpeed = 600.0f; // jog samples; walk samples are at 300

	/** Next state after DeltaSeconds. */
	EUDORIA_API FStatus Advance(const FStatus& Current, const FInput& Input, float DeltaSeconds, const FTimings& Timings);

	/**
	 * Position in the blend space: X = direction of the ground velocity relative to where the character
	 * faces, in degrees [-180, 180] (0 = forward, 90 = to its right); Y = ground speed in cm/s [0, 600].
	 */
	EUDORIA_API FVector2D BlendSpacePosition(const FInput& Input);
}
