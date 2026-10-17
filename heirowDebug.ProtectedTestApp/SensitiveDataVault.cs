using System.Security.Cryptography;

namespace heirowDebug.ProtectedTestApp;

internal sealed class SensitiveDataVault
{
    private static readonly byte[] Entropy = [0x48, 0x44, 0x4E, 0x53, 0x2D, 0x54, 0x45, 0x53, 0x54];
    private readonly byte[] _protectedValue;

    internal SensitiveDataVault()
    {
        var plaintext = RandomNumberGenerator.GetBytes(32);

        try
        {
            _protectedValue = ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    internal string ComputeFingerprint()
    {
        var plaintext = ProtectedData.Unprotect(_protectedValue, Entropy, DataProtectionScope.CurrentUser);
        try
        {
            var hash = SHA256.HashData(plaintext);
            return Convert.ToHexString(hash.AsSpan(0, 6));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class ShieldSelfTestAttribute : Attribute
{
}

internal static class SelfDiagnostics
{
    [ShieldSelfTest]
    private static string Probe() => $"reflection from {typeof(SelfDiagnostics).Assembly.GetName().Name}";
}
