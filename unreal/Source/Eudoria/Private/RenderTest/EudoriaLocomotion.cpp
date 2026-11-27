#include "RenderTest/EudoriaLocomotion.h"

namespace EudoriaLocomotion
{
	namespace
	{
		float GroundSpeed(const FInput& Input)
		{
			return FVector2D(Input.Velocity.X, Input.Velocity.Y).Size();
		}

		EState NextState(const FStatus& Current, const FInput& Input, float SecondsInState, const FTimings& Timings)
		{
			if (Input.bIsFalling)
			{
				switch (Current.State)
				{
				case EState::Grounded:
				case EState::Landing:
					// Going up: a jump. Going down: walked off a ledge, no jump animation.
					return Input.Velocity.Z > 0.0f ? EState::Jumping : EState::Falling;
				case EState::Jumping:
					return SecondsInState >= Timings.JumpSeconds ? EState::Falling : EState::Jumping;
				case EState::Falling:
					return EState::Falling;
				}
			}

			switch (Current.State)
			{
			case EState::Jumping:
			case EState::Falling:
				return EState::Landing;
			case EState::Landing:
				return SecondsInState >= Timings.LandSeconds || GroundSpeed(Input) > Timings.LandInterruptSpeed
					? EState::Grounded
					: EState::Landing;
			case EState::Grounded:
				return EState::Grounded;
			}
			return EState::Grounded;
		}
	}

	FStatus Advance(const FStatus& Current, const FInput& Input, float DeltaSeconds, const FTimings& Timings)
	{
		const float Elapsed = Current.SecondsInState + FMath::Max(DeltaSeconds, 0.0f);
		const EState Next = NextState(Current, Input, Elapsed, Timings);
		return FStatus{Next, Next == Current.State ? Elapsed : 0.0f};
	}

	FVector2D BlendSpacePosition(const FInput& Input)
	{
		const float Speed = GroundSpeed(Input);
		if (Speed < KINDA_SMALL_NUMBER)
		{
			return FVector2D::ZeroVector;
		}
		// Velocity in the character's own frame: X = forward, Y = right.
		const FVector Local = FRotator(0.0f, Input.ActorYawDegrees, 0.0f).UnrotateVector(FVector(Input.Velocity.X, Input.Velocity.Y, 0.0f));
		const float Direction = FMath::RadiansToDegrees(FMath::Atan2(Local.Y, Local.X));
		return FVector2D(Direction, FMath::Min(Speed, MaxBlendSpeed));
	}
}
