#include "EudoriaEs256.h"

#if EUDORIA_WITH_TICKET_VERIFIER

#include "Misc/ScopeExit.h"

// The engine's OpenSSL (1.1.1t), included the way the engine itself does it.
#define UI UI_ST
THIRD_PARTY_INCLUDES_START
#include <openssl/bn.h>
#include <openssl/ec.h>
#include <openssl/ecdsa.h>
#include <openssl/obj_mac.h>
#include <openssl/sha.h>
THIRD_PARTY_INCLUDES_END
#undef UI

namespace EudoriaEs256
{
	namespace
	{
		constexpr int32 CoordinateBytes = 32;

		/** A P-256 public key from its coordinates; null when they are not a point on the curve. Caller frees it. */
		EC_KEY* MakePublicKey(TConstArrayView<uint8> X, TConstArrayView<uint8> Y)
		{
			if (X.Num() != CoordinateBytes || Y.Num() != CoordinateBytes)
			{
				return nullptr;
			}
			EC_KEY* Key = EC_KEY_new_by_curve_name(NID_X9_62_prime256v1);
			BIGNUM* BigX = BN_bin2bn(X.GetData(), CoordinateBytes, nullptr);
			BIGNUM* BigY = BN_bin2bn(Y.GetData(), CoordinateBytes, nullptr);
			ON_SCOPE_EXIT
			{
				BN_free(BigX);
				BN_free(BigY);
			};
			// Also checks that the point is on the curve (EC_KEY_check_key).
			if (Key == nullptr || BigX == nullptr || BigY == nullptr || EC_KEY_set_public_key_affine_coordinates(Key, BigX, BigY) != 1)
			{
				EC_KEY_free(Key);
				return nullptr;
			}
			return Key;
		}
	}

	bool IsValidPublicKey(TConstArrayView<uint8> X, TConstArrayView<uint8> Y)
	{
		EC_KEY* Key = MakePublicKey(X, Y);
		EC_KEY_free(Key);
		return Key != nullptr;
	}

	bool Verify(TConstArrayView<uint8> X, TConstArrayView<uint8> Y, TConstArrayView<uint8> SigningInput, TConstArrayView<uint8> Signature)
	{
		if (Signature.Num() != 2 * CoordinateBytes)
		{
			return false;
		}
		EC_KEY* Key = MakePublicKey(X, Y);
		if (Key == nullptr)
		{
			return false;
		}
		ON_SCOPE_EXIT { EC_KEY_free(Key); };

		uint8 Digest[SHA256_DIGEST_LENGTH];
		SHA256(SigningInput.GetData(), SigningInput.Num(), Digest);

		// JWS r||s -> OpenSSL signature: r = first 32 bytes, s = last 32 bytes (big-endian).
		ECDSA_SIG* Sig = ECDSA_SIG_new();
		BIGNUM* R = BN_bin2bn(Signature.GetData(), CoordinateBytes, nullptr);
		BIGNUM* S = BN_bin2bn(Signature.GetData() + CoordinateBytes, CoordinateBytes, nullptr);
		if (Sig == nullptr || R == nullptr || S == nullptr || ECDSA_SIG_set0(Sig, R, S) != 1)
		{
			BN_free(R);
			BN_free(S);
			ECDSA_SIG_free(Sig);
			return false;
		}
		ON_SCOPE_EXIT { ECDSA_SIG_free(Sig); }; // also frees R and S, owned by Sig now

		// 1 = valid, 0 = invalid, -1 = error: only 1 is accepted.
		return ECDSA_do_verify(Digest, SHA256_DIGEST_LENGTH, Sig, Key) == 1;
	}
}

#endif // EUDORIA_WITH_TICKET_VERIFIER
