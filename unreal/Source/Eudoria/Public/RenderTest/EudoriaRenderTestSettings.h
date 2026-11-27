#pragma once

#include "CoreMinimal.h"
#include "Engine/DeveloperSettings.h"

#include "EudoriaRenderTestSettings.generated.h"

class UAnimSequence;
class UBlendSpace;
class USkeletalMesh;
class UWorld;

/**
 * Assets of the render test (sub-step 1.7), read from Config/DefaultGame.ini
 * (section [/Script/Eudoria.EudoriaRenderTestSettings]). The content itself is kept out of git
 * (plan 1.7, decision D1): tools/dev/setup-render-test.ps1 puts it back.
 */
UCLASS(Config = Game, DefaultConfig, meta = (DisplayName = "EUDORIA Render Test"))
class EUDORIA_API UEudoriaRenderTestSettings : public UDeveloperSettings
{
	GENERATED_BODY()

public:
	/** The test scene (built by unreal/Scripts/build_render_test_map.py). */
	UPROPERTY(Config, EditAnywhere, Category = "Scene")
	TSoftObjectPtr<UWorld> Map;

	UPROPERTY(Config, EditAnywhere, Category = "Character")
	TSoftObjectPtr<USkeletalMesh> CharacterMesh;

	/** Ground locomotion: X = direction (-180..180), Y = speed (0..600). */
	UPROPERTY(Config, EditAnywhere, Category = "Character")
	TSoftObjectPtr<UBlendSpace> Locomotion;

	UPROPERTY(Config, EditAnywhere, Category = "Character")
	TSoftObjectPtr<UAnimSequence> JumpStart;

	UPROPERTY(Config, EditAnywhere, Category = "Character")
	TSoftObjectPtr<UAnimSequence> FallLoop;

	UPROPERTY(Config, EditAnywhere, Category = "Character")
	TSoftObjectPtr<UAnimSequence> Land;

	/** Names of the settings left empty (an empty list means every setting is filled). */
	TArray<FString> FindEmptySettings() const;

	/** Names of the settings whose asset is not on disk (content not set up). Does not load anything. */
	TArray<FString> FindMissingContent() const;

	/** Same, for the character's assets only (mesh and animations, not the map). */
	TArray<FString> FindMissingCharacterContent() const;

	virtual FName GetCategoryName() const override { return TEXT("Project"); }
};
