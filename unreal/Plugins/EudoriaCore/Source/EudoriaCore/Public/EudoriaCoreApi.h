#pragma once

#include "CoreMinimal.h"
#include "Templates/ValueOrError.h"

/**
 * Types and pure functions for the core public API (contract shared/api/core-v1.yaml).
 * Nothing here touches the network: every function can be tested on its own.
 */

/** What happened to an HTTP exchange, before looking at the status code. */
enum class EEudoriaTransportStatus : uint8
{
	/** A response was received (any status code). */
	Received,
	/** No response: connection failure, TLS failure or timeout. */
	Failed,
	/** Not sent: the configured base URL is not https://. */
	RefusedInsecureUrl,
};

/** A raw HTTP response, as seen by the parsing functions. */
struct FEudoriaHttpResponse
{
	EEudoriaTransportStatus TransportStatus = EEudoriaTransportStatus::Failed;
	int32 StatusCode = 0;
	FString Body;
	/** Value of the Retry-After header, empty when absent. */
	FString RetryAfter;

	static FEudoriaHttpResponse Make(int32 InStatusCode, FString InBody = FString(), FString InRetryAfter = FString())
	{
		return FEudoriaHttpResponse{EEudoriaTransportStatus::Received, InStatusCode, MoveTemp(InBody), MoveTemp(InRetryAfter)};
	}
};

/** One error of a problem response: { "field": ..., "code": ... }. */
struct FEudoriaFieldError
{
	FString Field;
	FString Code;

	bool operator==(const FEudoriaFieldError& Other) const = default;
};

/** Kind of failure of a call to the core. */
enum class EEudoriaApiErrorKind : uint8
{
	/** 400: invalid field(s); see Errors. */
	InvalidInput,
	/** 401: wrong credentials or invalid token; see Errors. */
	Unauthorized,
	/** 409: conflict (pseudo taken); see Errors. */
	Conflict,
	/** 429: too many attempts; see RetryAfterSeconds. */
	TooManyRequests,
	/** 503: core too busy; see RetryAfterSeconds. */
	ServerBusy,
	/** The session could not be renewed (or was closed): the player must log in again. */
	SessionEnded,
	/** No response from the core (network, TLS or timeout). */
	NetworkError,
	/** The client is configured with a non-https:// core URL: nothing was sent. */
	InsecureConfiguration,
	/** A response the contract does not allow (unknown status, unreadable JSON...). */
	UnexpectedResponse,
};

struct FEudoriaApiError
{
	EEudoriaApiErrorKind Kind = EEudoriaApiErrorKind::UnexpectedResponse;
	/** HTTP status code, 0 when no response was received. */
	int32 StatusCode = 0;
	TArray<FEudoriaFieldError> Errors;
	/** Seconds to wait (Retry-After), or -1 when not given. */
	int32 RetryAfterSeconds = -1;

	bool HasCode(const TCHAR* Code) const;
};

/** Result of a call that returns no data (empty = success). */
using FEudoriaApiStatus = TOptional<FEudoriaApiError>;

/** Tokens returned by /auth/login and /auth/refresh. Never logged, never written to disk. */
struct FEudoriaTokenPair
{
	FString AccessToken;
	FString RefreshToken;
	int32 ExpiresInSeconds = 0;
	int32 RefreshExpiresInSeconds = 0;
};

/** The player's own profile (GET /me). */
struct FEudoriaProfile
{
	FString Id;
	FString Pseudo;
	FDateTime CreatedAt;
};

namespace EudoriaCoreApi
{
	/** True when the URL uses https:// and names a host (the client refuses anything else). */
	EUDORIACORE_API bool IsSecureBaseUrl(const FString& BaseUrl);

	/** Turns a non-success response (or a missing one) into an error. */
	EUDORIACORE_API FEudoriaApiError ParseError(const FEudoriaHttpResponse& Response);

	/** POST /accounts: 202 = success. */
	EUDORIACORE_API FEudoriaApiStatus ParseRegistration(const FEudoriaHttpResponse& Response);

	/** POST /auth/login and /auth/refresh: 200 with a token pair. */
	EUDORIACORE_API TValueOrError<FEudoriaTokenPair, FEudoriaApiError> ParseTokenPair(const FEudoriaHttpResponse& Response);

	/** GET /me: 200 with the profile. */
	EUDORIACORE_API TValueOrError<FEudoriaProfile, FEudoriaApiError> ParseProfile(const FEudoriaHttpResponse& Response);

	/** POST /auth/logout: 204 = success. */
	EUDORIACORE_API FEudoriaApiStatus ParseLogout(const FEudoriaHttpResponse& Response);

	/** JSON request bodies (proper escaping). */
	EUDORIACORE_API FString MakeRegisterBody(const FString& Email, const FString& Pseudo, const FString& Password);
	EUDORIACORE_API FString MakeLoginBody(const FString& Email, const FString& Password);
	EUDORIACORE_API FString MakeRefreshTokenBody(const FString& RefreshToken);

	/** French message for the player. Never says whether an e-mail has an account. */
	EUDORIACORE_API FText ToUserMessage(const FEudoriaApiError& Error);
}
