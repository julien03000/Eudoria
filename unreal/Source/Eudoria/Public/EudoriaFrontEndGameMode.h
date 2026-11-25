#pragma once

#include "CoreMinimal.h"
#include "GameFramework/GameModeBase.h"

#include "EudoriaFrontEndGameMode.generated.h"

/** Game mode of the client front-end (login, account creation, welcome): no pawn, no HUD. */
UCLASS()
class EUDORIA_API AEudoriaFrontEndGameMode : public AGameModeBase
{
	GENERATED_BODY()

public:
	AEudoriaFrontEndGameMode();
};
