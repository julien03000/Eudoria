// EUDORIA — generates one of the core's signing keys (ES256 = ECDSA on curve P-256): access tokens or entry tickets.
// File-based C# program, run by new-signing-key.ps1 with "dotnet run --file": nothing to install.
// Prints the private key (PKCS#8, Base64) on standard output, where the script captures it;
// the script writes it to .env and never displays it.
using System.Security.Cryptography;

using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
Console.Out.Write(Convert.ToBase64String(key.ExportPkcs8PrivateKey()));
