#pragma once

#include "CoreMinimal.h"
#include "Engine/DeveloperSettings.h"

#include "EudoriaCoreSettings.generated.h"

/**
 * Settings of the EUDORIA core client, read from Config/DefaultGame.ini
 * (section [/Script/EudoriaCore.EudoriaCoreSettings]).
 */
UCLASS(Config = Game, DefaultConfig, meta = (DisplayName = "EUDORIA Core"))
class EUDORIACORE_API UEudoriaCoreSettings : public UDeveloperSettings
{
	GENERATED_BODY()

public:
	/** Base URL of the core public API, version included (e.g. https://localhost:5443/v1). Must use https://. */
	UPROPERTY(Config, EditAnywhere, Category = "Core")
	FString CoreBaseUrl;

	/** Maximum duration of one request to the core, in seconds. */
	UPROPERTY(Config, EditAnywhere, Category = "Core", meta = (ClampMin = "1", ClampMax = "60"))
	float RequestTimeoutSeconds = 10.0f;

	virtual FName GetCategoryName() const override { return TEXT("Project"); }
};
