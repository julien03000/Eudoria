#include "RenderTest/EudoriaThirdPersonCharacter.h"

#include "Animation/AnimSequence.h"
#include "Animation/AnimSingleNodeInstance.h"
#include "Animation/BlendSpace.h"
#include "Camera/CameraComponent.h"
#include "Components/CapsuleComponent.h"
#include "Components/SkeletalMeshComponent.h"
#include "EnhancedInputComponent.h"
#include "Eudoria.h"
#include "GameFramework/CharacterMovementComponent.h"
#include "GameFramework/SpringArmComponent.h"
#include "InputActionValue.h"
#include "RenderTest/EudoriaRenderTestInput.h"
#include "RenderTest/EudoriaRenderTestPlayerController.h"
#include "RenderTest/EudoriaRenderTestSettings.h"

#include UE_INLINE_GENERATED_CPP_BY_NAME(EudoriaThirdPersonCharacter)

AEudoriaThirdPersonCharacter::AEudoriaThirdPersonCharacter()
{
	PrimaryActorTick.bCanEverTick = true;

	// Same proportions as Epic's third-person template (mannequin 1.8 m).
	GetCapsuleComponent()->InitCapsuleSize(42.0f, 96.0f);

	// The camera turns with the mouse; the character turns towards where it walks.
	bUseControllerRotationPitch = false;
	bUseControllerRotationYaw = false;
	bUseControllerRotationRoll = false;

	UCharacterMovementComponent* Movement = GetCharacterMovement();
	Movement->bOrientRotationToMovement = true;
	Movement->RotationRate = FRotator(0.0f, 500.0f, 0.0f);
	Movement->MaxWalkSpeed = WalkSpeed;
	Movement->MinAnalogWalkSpeed = 20.0f;
	Movement->JumpZVelocity = 500.0f;
	Movement->AirControl = 0.35f;
	Movement->BrakingDecelerationWalking = 2000.0f;
	Movement->BrakingDecelerationFalling = 1500.0f;

	CameraBoom = CreateDefaultSubobject<USpringArmComponent>(TEXT("CameraBoom"));
	CameraBoom->SetupAttachment(RootComponent);
	CameraBoom->TargetArmLength = 400.0f;
	CameraBoom->SocketOffset = FVector(0.0f, 0.0f, 60.0f);
	CameraBoom->bUsePawnControlRotation = true;

	FollowCamera = CreateDefaultSubobject<UCameraComponent>(TEXT("FollowCamera"));
	FollowCamera->SetupAttachment(CameraBoom, USpringArmComponent::SocketName);
	FollowCamera->bUsePawnControlRotation = false;

	// The mannequin's feet at the bottom of the capsule, facing +X.
	GetMesh()->SetRelativeLocationAndRotation(FVector(0.0f, 0.0f, -96.0f), FRotator(0.0f, -90.0f, 0.0f));
	GetMesh()->SetAnimationMode(EAnimationMode::AnimationSingleNode);
}

void AEudoriaThirdPersonCharacter::BeginPlay()
{
	Super::BeginPlay();
	ApplyContent();
}

bool AEudoriaThirdPersonCharacter::ApplyContent()
{
	const UEudoriaRenderTestSettings* Settings = GetDefault<UEudoriaRenderTestSettings>();
	USkeletalMesh* MannequinMesh = Settings->CharacterMesh.LoadSynchronous();
	LocomotionAsset = Settings->Locomotion.LoadSynchronous();
	JumpAsset = Settings->JumpStart.LoadSynchronous();
	FallAsset = Settings->FallLoop.LoadSynchronous();
	LandAsset = Settings->Land.LoadSynchronous();

	bHasContent = MannequinMesh && LocomotionAsset && JumpAsset && FallAsset && LandAsset;
	if (!bHasContent)
	{
		UE_LOG(LogEudoria, Warning, TEXT("Render test character: content missing (%s). Run tools/dev/setup-render-test.ps1."),
			*FString::Join(Settings->FindMissingCharacterContent(), TEXT(", ")));
		return false;
	}

	if (const UAnimSequence* Jump = Cast<UAnimSequence>(JumpAsset))
	{
		Timings.JumpSeconds = Jump->GetPlayLength();
	}
	if (const UAnimSequence* Landing = Cast<UAnimSequence>(LandAsset))
	{
		Timings.LandSeconds = Landing->GetPlayLength();
	}

	GetMesh()->SetSkeletalMesh(MannequinMesh);
	GetMesh()->SetAnimationMode(EAnimationMode::AnimationSingleNode);
	Locomotion = EudoriaLocomotion::FStatus();
	PlayStateAnimation(Locomotion.State);
	return true;
}

void AEudoriaThirdPersonCharacter::Tick(float DeltaSeconds)
{
	Super::Tick(DeltaSeconds);
	if (!bHasContent)
	{
		return;
	}

	EudoriaLocomotion::FInput Input;
	Input.Velocity = GetVelocity();
	Input.ActorYawDegrees = GetActorRotation().Yaw;
	Input.bIsFalling = GetCharacterMovement()->IsFalling();

	const EudoriaLocomotion::EState Previous = Locomotion.State;
	Locomotion = EudoriaLocomotion::Advance(Locomotion, Input, DeltaSeconds, Timings);
	if (Locomotion.State != Previous)
	{
		PlayStateAnimation(Locomotion.State);
	}
	if (Locomotion.State == EudoriaLocomotion::EState::Grounded)
	{
		if (UAnimSingleNodeInstance* Instance = GetMesh()->GetSingleNodeInstance())
		{
			const FVector2D Position = EudoriaLocomotion::BlendSpacePosition(Input);
			Instance->SetBlendSpacePosition(FVector(Position.X, Position.Y, 0.0f));
		}
	}
}

void AEudoriaThirdPersonCharacter::PlayStateAnimation(EudoriaLocomotion::EState State)
{
	switch (State)
	{
	case EudoriaLocomotion::EState::Grounded:
		GetMesh()->PlayAnimation(LocomotionAsset, true);
		break;
	case EudoriaLocomotion::EState::Jumping:
		GetMesh()->PlayAnimation(JumpAsset, false);
		break;
	case EudoriaLocomotion::EState::Falling:
		GetMesh()->PlayAnimation(FallAsset, true);
		break;
	case EudoriaLocomotion::EState::Landing:
		GetMesh()->PlayAnimation(LandAsset, false);
		break;
	}
}

void AEudoriaThirdPersonCharacter::SetupPlayerInputComponent(UInputComponent* PlayerInputComponent)
{
	Super::SetupPlayerInputComponent(PlayerInputComponent);

	UEnhancedInputComponent* Enhanced = Cast<UEnhancedInputComponent>(PlayerInputComponent);
	AEudoriaRenderTestPlayerController* PlayerController = Cast<AEudoriaRenderTestPlayerController>(GetController());
	if (!Enhanced || !PlayerController)
	{
		UE_LOG(LogEudoria, Error, TEXT("Render test character: no Enhanced Input component or wrong controller, input not bound."));
		return;
	}

	const UEudoriaRenderTestInput* Input = PlayerController->GetRenderTestInput();
	Enhanced->BindAction(Input->Move, ETriggerEvent::Triggered, this, &ThisClass::HandleMove);
	Enhanced->BindAction(Input->Look, ETriggerEvent::Triggered, this, &ThisClass::HandleLook);
	Enhanced->BindAction(Input->Jump, ETriggerEvent::Started, this, &ACharacter::Jump);
	Enhanced->BindAction(Input->Jump, ETriggerEvent::Completed, this, &ACharacter::StopJumping);
	Enhanced->BindAction(Input->Run, ETriggerEvent::Started, this, &ThisClass::HandleRunStarted);
	Enhanced->BindAction(Input->Run, ETriggerEvent::Completed, this, &ThisClass::HandleRunCompleted);
}

void AEudoriaThirdPersonCharacter::HandleMove(const FInputActionValue& Value)
{
	const FVector2D Axis = Value.Get<FVector2D>();
	if (!Controller)
	{
		return;
	}
	// Relative to where the camera looks, on the ground plane.
	const FRotator Yaw(0.0f, Controller->GetControlRotation().Yaw, 0.0f);
	AddMovementInput(FRotationMatrix(Yaw).GetUnitAxis(EAxis::X), Axis.Y);
	AddMovementInput(FRotationMatrix(Yaw).GetUnitAxis(EAxis::Y), Axis.X);
}

void AEudoriaThirdPersonCharacter::HandleLook(const FInputActionValue& Value)
{
	const FVector2D Axis = Value.Get<FVector2D>();
	AddControllerYawInput(Axis.X);
	AddControllerPitchInput(Axis.Y);
}

void AEudoriaThirdPersonCharacter::HandleRunStarted(const FInputActionValue&)
{
	SetRunning(true);
}

void AEudoriaThirdPersonCharacter::HandleRunCompleted(const FInputActionValue&)
{
	SetRunning(false);
}

void AEudoriaThirdPersonCharacter::SetRunning(bool bRun)
{
	GetCharacterMovement()->MaxWalkSpeed = bRun ? RunSpeed : WalkSpeed;
}
