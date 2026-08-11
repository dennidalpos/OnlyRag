using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Storage.Security;

public interface ICloudApiKeyVault
{
    Task SaveApiKeyAsync(CloudLlmProvider provider, string apiKey, CancellationToken cancellationToken = default);
    Task<string?> GetApiKeyAsync(CloudLlmProvider provider, CancellationToken cancellationToken = default);
    Task DeleteApiKeyAsync(CloudLlmProvider provider, CancellationToken cancellationToken = default);
}

public sealed class WindowsCredentialManagerCloudKeyVault : ICloudApiKeyVault
{
    private const string TargetPrefix = "OnlyRag/CloudApiKey/";
    private const int CredTypeGeneric = 1;
    private const int CredPersistLocalMachine = 2;

    private readonly string keysDir;

    public WindowsCredentialManagerCloudKeyVault()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        keysDir = Path.Combine(localAppData, "OnlyRag", "keys");
        Directory.CreateDirectory(keysDir);
    }

    public Task SaveApiKeyAsync(CloudLlmProvider provider, string apiKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        string targetName = TargetPrefix + provider;

        bool writtenToCredMgr = WriteToCredentialManager(targetName, apiKey);
        if (!writtenToCredMgr)
        {
            WriteToDpapiFallback(provider, apiKey);
        }

        return Task.CompletedTask;
    }

    public Task<string?> GetApiKeyAsync(CloudLlmProvider provider, CancellationToken cancellationToken = default)
    {
        string targetName = TargetPrefix + provider;
        string? key = ReadFromCredentialManager(targetName);
        if (!string.IsNullOrEmpty(key))
        {
            return Task.FromResult<string?>(key);
        }

        key = ReadFromDpapiFallback(provider);
        return Task.FromResult(key);
    }

    public Task DeleteApiKeyAsync(CloudLlmProvider provider, CancellationToken cancellationToken = default)
    {
        string targetName = TargetPrefix + provider;
        DeleteFromCredentialManager(targetName);
        DeleteFromDpapiFallback(provider);
        return Task.CompletedTask;
    }

    private static string? ReadFromCredentialManager(string targetName)
    {
        try
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return null;
            }

            if (CredRead(targetName, CredTypeGeneric, 0, out IntPtr pBuffer))
            {
                try
                {
                    NativeCredential cred = Marshal.PtrToStructure<NativeCredential>(pBuffer);
                    if (cred.CredentialBlob != IntPtr.Zero && cred.CredentialBlobSize > 0)
                    {
                        byte[] blob = new byte[cred.CredentialBlobSize];
                        Marshal.Copy(cred.CredentialBlob, blob, 0, (int)cred.CredentialBlobSize);
                        return Encoding.UTF8.GetString(blob);
                    }
                }
                finally
                {
                    CredFree(pBuffer);
                }
            }
        }
        catch
        {
            // Fallback
        }
        return null;
    }

    private static bool WriteToCredentialManager(string targetName, string key)
    {
        try
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return false;
            }

            byte[] blob = Encoding.UTF8.GetBytes(key);
            IntPtr pBlob = Marshal.AllocHGlobal(blob.Length);
            try
            {
                Marshal.Copy(blob, 0, pBlob, blob.Length);
                NativeCredential cred = new()
                {
                    Type = CredTypeGeneric,
                    TargetName = targetName,
                    Persist = CredPersistLocalMachine,
                    AttributeCount = 0,
                    Attributes = IntPtr.Zero,
                    TargetAlias = string.Empty,
                    UserName = "OnlyRagUser",
                    CredentialBlob = pBlob,
                    CredentialBlobSize = (uint)blob.Length
                };

                return CredWrite(ref cred, 0);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(blob);
                Marshal.FreeHGlobal(pBlob);
            }
        }
        catch
        {
            return false;
        }
    }

    private static bool DeleteFromCredentialManager(string targetName)
    {
        try
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return false;
            }

            return CredDelete(targetName, CredTypeGeneric, 0);
        }
        catch
        {
            return false;
        }
    }

    private string GetDpapiFilePath(CloudLlmProvider provider)
    {
        return Path.Combine(keysDir, $"cloud_{provider}.key.dpapi");
    }

    private string? ReadFromDpapiFallback(CloudLlmProvider provider)
    {
        try
        {
            string filePath = GetDpapiFilePath(provider);
            if (!OperatingSystem.IsWindows() || !File.Exists(filePath))
            {
                return null;
            }

            byte[] encryptedBytes = File.ReadAllBytes(filePath);
            if (encryptedBytes.Length == 0)
            {
                return null;
            }

            byte[] decryptedBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decryptedBytes);
        }
        catch
        {
            return null;
        }
    }

    private void WriteToDpapiFallback(CloudLlmProvider provider, string key)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            string filePath = GetDpapiFilePath(provider);
            byte[] keyBytes = Encoding.UTF8.GetBytes(key);
            try
            {
                byte[] encryptedBytes = ProtectedData.Protect(keyBytes, null, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(filePath, encryptedBytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(keyBytes);
            }
        }
        catch
        {
            // Best effort
        }
    }

    private void DeleteFromDpapiFallback(CloudLlmProvider provider)
    {
        try
        {
            string filePath = GetDpapiFilePath(provider);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch
        {
            // Best effort
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public int Type;
        public string TargetName;
        public string Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias;
        public string UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string targetName, int type, int reservedFlag, out IntPtr credentialPtr);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref NativeCredential userCredential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string targetName, int type, int reservedFlags);

    [DllImport("advapi32.dll", EntryPoint = "CredFree", SetLastError = true)]
    private static extern void CredFree(IntPtr buffer);
}
