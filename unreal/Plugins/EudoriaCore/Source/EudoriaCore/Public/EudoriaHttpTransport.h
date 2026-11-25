#pragma once

#include "CoreMinimal.h"
#include "EudoriaCoreApi.h"

/** One request to the core public API. Path is relative to the base URL (e.g. "/auth/login"). */
struct FEudoriaHttpRequest
{
	FString Verb;
	FString Path;
	/** JSON body, empty for none. May contain a password or a token: never log it. */
	FString JsonBody;
	/** Access token for "Authorization: Bearer", empty for none. Never log it. */
	FString BearerToken;
};

using FEudoriaHttpCallback = TFunction<void(const FEudoriaHttpResponse&)>;

/**
 * Sends requests to the core. The callback is always called exactly once, on the game thread.
 * An interface so that tests can replace the network with a fake.
 */
class IEudoriaHttpTransport
{
public:
	virtual ~IEudoriaHttpTransport() = default;
	virtual void Send(const FEudoriaHttpRequest& Request, FEudoriaHttpCallback OnComplete) = 0;
};

/** Real transport, built on Unreal's HTTP module (TLS certificate checked; Windows certificate store accepted). */
class EUDORIACORE_API FEudoriaHttpModuleTransport final : public IEudoriaHttpTransport
{
public:
	FEudoriaHttpModuleTransport(FString InBaseUrl, float InTimeoutSeconds);

	/** Refuses to send anything (RefusedInsecureUrl) when the base URL is not https://. */
	virtual void Send(const FEudoriaHttpRequest& Request, FEudoriaHttpCallback OnComplete) override;

private:
	FString BaseUrl;
	float TimeoutSeconds;
};
