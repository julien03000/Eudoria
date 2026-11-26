#pragma once

#include "CoreMinimal.h"

#if EUDORIA_WITH_TICKET_VERIFIER

/**
 * Strict reading of the pieces of a JWS compact ticket (ADR-0007). Same rules as the .NET reference
 * verifier (core/tests/Eudoria.ThirdParty.WorldVerifier), checked by the shared test vectors.
 */
namespace EudoriaJwt
{
	/**
	 * Base64 URL-safe without padding, strictly: only A-Z a-z 0-9 - _, no '=', no whitespace, length % 4 != 1,
	 * and the unused bits of the last character must be zero (one encoding per value).
	 */
	bool DecodeBase64Url(FStringView Text, TArray<uint8>& OutBytes);

	/** Base64 URL-safe without padding (used by the tests to rebuild tickets). */
	FString EncodeBase64Url(TConstArrayView<uint8> Bytes);

	/** Well-formed UTF-8 (no overlong form, no surrogate, nothing above U+10FFFF), converted to text. */
	bool DecodeUtf8(TConstArrayView<uint8> Bytes, FString& OutText);

	/** A top-level member of a JSON object: a string, a number (its exact text), or anything else. */
	struct FJsonMember
	{
		enum class EKind : uint8
		{
			String,
			Number,
			Other,
		};

		EKind Kind = EKind::Other;
		/** The string value, or the number exactly as written ("60", "60.5", "1e3"). Empty for Other. */
		FString Text;
	};

	/**
	 * Reads a JSON object and returns its top-level members. False when the text is not exactly one
	 * JSON object, or when any object (at any depth) repeats a member name, or nests deeper than 4 levels.
	 */
	bool ReadObject(const FString& Json, TMap<FString, FJsonMember>& OutMembers);
}

#endif // EUDORIA_WITH_TICKET_VERIFIER
