#pragma once

#include "CoreMinimal.h"
#include "GameFramework/GameModeBase.h"

#include "EudoriaRenderTestGameMode.generated.h"

/** Game mode of the render test scene (set on the map by unreal/Scripts/build_render_test_map.py). */
UCLASS()
class EUDORIA_API AEudoriaRenderTestGameMode : public AGameModeBase
{
	GENERATED_BODY()

public:
	AEudoriaRenderTestGameMode();
};
