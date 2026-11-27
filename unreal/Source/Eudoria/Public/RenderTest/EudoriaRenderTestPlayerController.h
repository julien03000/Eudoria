#pragma once

#include "CoreMinimal.h"
#include "GameFramework/PlayerController.h"

#include "EudoriaRenderTestPlayerController.generated.h"

class UEudoriaRenderTestInput;

/** Player controller of the render test: game input only, mouse hidden, keys of UEudoriaRenderTestInput. */
UCLASS()
class EUDORIA_API AEudoriaRenderTestPlayerController : public APlayerController
{
	GENERATED_BODY()

public:
	/** The actions and keys of the render test (created on first use: the pawn may ask before BeginPlay). */
	UEudoriaRenderTestInput* GetRenderTestInput();

protected:
	virtual void BeginPlay() override;

private:
	UPROPERTY(Transient)
	TObjectPtr<UEudoriaRenderTestInput> RenderTestInput;
};
