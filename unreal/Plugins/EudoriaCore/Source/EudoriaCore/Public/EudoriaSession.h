#pragma once

#include "CoreMinimal.h"
#include "EudoriaCoreApi.h"
#include "EudoriaHttpTransport.h"

/**
 * The player's session with the core: tokens kept in memory only (never on disk, never logged).
 *
 * Access-token renewal (plan 1.5, § 10.1): when an authorized request gets 401, the session sends
 * a single /auth/refresh, however many requests got 401 at the same time. The others wait for its
 * result and are replayed once with the new access token. The core treats a second use of the same
 * refresh token as theft and revokes the whole session, hence this rule.
 *
 * Everything runs on the game thread (the transport calls back there): no lock needed.
 */
class EUDORIACORE_API FEudoriaSession : public TSharedFromThis<FEudoriaSession>
{
public:
	using FStatusCallback = TFunction<void(const FEudoriaApiStatus&)>;
	using FProfileCallback = TFunction<void(const TValueOrError<FEudoriaProfile, FEudoriaApiError>&)>;

	explicit FEudoriaSession(TSharedRef<IEudoriaHttpTransport> InTransport);

	/** POST /accounts. Does not log in. */
	void Register(const FString& Email, const FString& Pseudo, const FString& Password, FStatusCallback OnComplete);

	/** POST /auth/login. On success the tokens are kept in memory. */
	void Login(const FString& Email, const FString& Password, FStatusCallback OnComplete);

	/** GET /me, renewing the access token if needed. SessionEnded error: the player must log in again. */
	void FetchProfile(FProfileCallback OnComplete);

	/** Forgets the tokens at once, then asks the core to close the session (POST /auth/logout). */
	void Logout(FStatusCallback OnComplete);

	bool IsLoggedIn() const { return !Tokens.AccessToken.IsEmpty(); }

private:
	/** Sends a request with the current access token; handles 401 and renewal. */
	void SendAuthorized(FEudoriaHttpRequest Request, FEudoriaHttpCallback OnComplete);

	/** Sends with the current access token; on 401, either replays (token already renewed), waits for renewal, or fails. */
	void SendWithAccessToken(FEudoriaHttpRequest Request, FEudoriaHttpCallback OnComplete, bool bMayRenew);

	void StartRenewal();
	void FinishRenewal(const FEudoriaHttpResponse& Response, uint32 RenewalGeneration);

	/** Forgets the tokens and fails every waiting request (with a 401). */
	void EndSession();

	/** The answer given to a request whose session is over: a 401 without body. */
	static FEudoriaHttpResponse MakeSessionOverResponse();

	struct FPendingRequest
	{
		FEudoriaHttpRequest Request;
		FEudoriaHttpCallback OnComplete;
	};

	TSharedRef<IEudoriaHttpTransport> Transport;
	FEudoriaTokenPair Tokens;
	/** Requests that got 401 and wait for the renewal in progress. */
	TArray<FPendingRequest> WaitingForRenewal;
	bool bRenewing = false;
	/** Changes at every login and logout: late answers from an older session are ignored. */
	uint32 Generation = 0;
};
