#pragma once

#include "CoreMinimal.h"
#include "EudoriaSession.h"
#include "Subsystems/GameInstanceSubsystem.h"

#include "EudoriaSessionSubsystem.generated.h"

/**
 * Owns the player's session for the lifetime of the game instance.
 * Tokens live in memory only: closing the client forgets them.
 */
UCLASS()
class EUDORIACORE_API UEudoriaSessionSubsystem : public UGameInstanceSubsystem
{
	GENERATED_BODY()

public:
	virtual void Initialize(FSubsystemCollectionBase& Collection) override;
	virtual void Deinitialize() override;

	FEudoriaSession& GetSession() const { return *Session; }

	/** False when CoreBaseUrl is not https://: every call then fails with InsecureConfiguration. */
	bool HasSecureConfiguration() const { return bSecureConfiguration; }

private:
	TSharedPtr<FEudoriaSession> Session;
	bool bSecureConfiguration = false;
};
