using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace OnlyRag.Infrastructure.Storage.Security;

public sealed class WindowsCredentialManagerSqliteKeyProvider : ISqliteKeyProvider
{
    private const string TargetName = "OnlyRag/DatabaseEncryptionKey";
    private const int CredTypeGeneric = 1;
    private const int CredPersistLocalMachine = 2;

    private readonly object lockObj = new();
    private string? cachedKey;
    private readonly string fallbackKeyFilePath;

    public WindowsCredentialManagerSqliteKeyProvider()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string keysDir = Path.Combine(localAppData, "OnlyRag", "keys");
        Directory.CreateDirectory(keysDir);
        fallbackKeyFilePath = Path.Combine(keysDir, "db.key.dpapi");
    }

    public string GetOrCreateDatabaseKey()
    {
        lock (lockObj)
        {
            if (!string.IsNullOrEmpty(cachedKey))
            {
                return cachedKey;
            }

            // 1. Try Credential Manager
            string? key = ReadFromCredentialManager();
            if (!string.IsNullOrEmpty(key))
            {
                cachedKey = key;
                return key;
            }

            // 2. Try DPAPI fallback file
            key = ReadFromDpapiFallback();
            if (!string.IsNullOrEmpty(key))
            {
                // Sync back to Credential Manager if possible
                WriteToCredentialManager(key);
                cachedKey = key;
                return key;
            }

            // 3. Generate new 256-bit (32 bytes) cryptographically secure hex key
            byte[] keyBytes = RandomNumberGenerator.GetBytes(32);
            key = Convert.ToHexStringLower(keyBytes);

            // Save to both Credential Manager and DPAPI
            WriteToCredentialManager(key);
            WriteToDpapiFallback(key);

            cachedKey = key;
            return key;
        }
    }

    private static string? ReadFromCredentialManager()
    {
        try
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return null;
            }

            if (CredRead(TargetName, CredTypeGeneric, 0, out IntPtr pBuffer))
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
            // Fallback to DPAPI
        }
        return null;
    }

    private static bool WriteToCredentialManager(string key)
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
                    TargetName = TargetName,
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
                Marshal.FreeHGlobal(pBlob);
            }
        }
        catch
        {
            return false;
        }
    }

    private string? ReadFromDpapiFallback()
    {
        try
        {
            if (!OperatingSystem.IsWindows() || !File.Exists(fallbackKeyFilePath))
            {
                return null;
            }

            byte[] encryptedBytes = File.ReadAllBytes(fallbackKeyFilePath);
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

    private void WriteToDpapiFallback(string key)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            byte[] keyBytes = Encoding.UTF8.GetBytes(key);
            byte[] encryptedBytes = ProtectedData.Protect(keyBytes, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(fallbackKeyFilePath, encryptedBytes);
        }
        catch
        {
            // Best effort fallback
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

    [DllImport("advapi32.dll", EntryPoint = "CredFree", SetLastError = true)]
    private static extern void CredFree(IntPtr buffer);
}
