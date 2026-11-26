#pragma once

#include "CoreMinimal.h"

#if EUDORIA_WITH_TICKET_VERIFIER

/**
 * ES256 (RFC 7518, section 3.4) verification with the engine's OpenSSL: ECDSA on curve P-256 with SHA-256.
 * Public keys only: this code never signs. The JWS signature is r||s (32 + 32 bytes), not the DER
 * format OpenSSL uses internally; the conversion is done here.
 */
namespace EudoriaEs256
{
	/** True when X and Y (32 bytes each) are the coordinates of a point on P-256. */
	bool IsValidPublicKey(TConstArrayView<uint8> X, TConstArrayView<uint8> Y);

	/** True when Signature (exactly 64 bytes, r||s) is a valid ES256 signature of SigningInput by the key (X, Y). */
	bool Verify(TConstArrayView<uint8> X, TConstArrayView<uint8> Y, TConstArrayView<uint8> SigningInput, TConstArrayView<uint8> Signature);
}

#endif // EUDORIA_WITH_TICKET_VERIFIER
