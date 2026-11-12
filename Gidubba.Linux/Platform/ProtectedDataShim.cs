// DPAPI is Windows-only. The Linux build keeps the dddice login in the desktop keyring instead (Dice/KeyringDddiceAccountStore.cs),
// so the shared, DPAPI-based DddiceAccountStore only needs to compile; these stand-ins are never reached.
namespace System.Security.Cryptography;

public enum DataProtectionScope { CurrentUser, LocalMachine }

public static class ProtectedData
{
    public static byte[] Protect(byte[] userData, byte[]? optionalEntropy, DataProtectionScope scope)
        => throw new CryptographicException("DPAPI is not available on Linux.");

    public static byte[] Unprotect(byte[] encryptedData, byte[]? optionalEntropy, DataProtectionScope scope)
        => throw new CryptographicException("DPAPI is not available on Linux.");
}
