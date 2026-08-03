using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Storage;

public sealed class AesBackupService : IAesBackupService
{
    private static readonly byte[] MagicHeader = Encoding.UTF8.GetBytes("ORAGBAK_V1");
    private const int SaltSizeBytes = 32;
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;
    private const int KeySizeBytes = 32; // AES-256
    private const int Pbkdf2Iterations = 100_000;

    private readonly AppStoragePaths _storagePaths;

    public AesBackupService(AppStoragePaths storagePaths)
    {
        _storagePaths = storagePaths;
    }

    public async Task<BackupResult> CreateEncryptedBackupAsync(string destinationFilePath, string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
        {
            return new BackupResult(false, destinationFilePath, 0, "La password di cifratura del backup deve contenere almeno 6 caratteri.");
        }

        try
        {
            string? dir = Path.GetDirectoryName(destinationFilePath);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // 1. Create Zip in memory containing database and settings
            using MemoryStream zipStream = new();
            using (ZipArchive archive = new(zipStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                if (File.Exists(_storagePaths.DatabasePath))
                {
                    archive.CreateEntryFromFile(_storagePaths.DatabasePath, "onlyrag.db");
                }

                if (Directory.Exists(_storagePaths.DataRoot))
                {
                    string[] jsonFiles = Directory.GetFiles(_storagePaths.DataRoot, "*.json", SearchOption.AllDirectories);
                    foreach (string jsonFile in jsonFiles)
                    {
                        string relativePath = Path.GetRelativePath(_storagePaths.DataRoot, jsonFile);
                        archive.CreateEntryFromFile(jsonFile, Path.Combine("settings", relativePath));
                    }
                }
            }

            byte[] plainBytes = zipStream.ToArray();

            // 2. Derive key using PBKDF2 HMAC SHA256
            byte[] salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
            byte[] nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
            byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, KeySizeBytes);

            // 3. Encrypt using AES-256-GCM
            byte[] cipherBytes = new byte[plainBytes.Length];
            byte[] tag = new byte[TagSizeBytes];

            using (AesGcm aesGcm = new(key, TagSizeBytes))
            {
                aesGcm.Encrypt(nonce, plainBytes, cipherBytes, tag);
            }

            // 4. Write output file: [MagicHeader][Salt][Nonce][Tag][Ciphertext]
            using (FileStream fs = new(destinationFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await fs.WriteAsync(MagicHeader, cancellationToken);
                await fs.WriteAsync(salt, cancellationToken);
                await fs.WriteAsync(nonce, cancellationToken);
                await fs.WriteAsync(tag, cancellationToken);
                await fs.WriteAsync(cipherBytes, cancellationToken);
            }

            FileInfo fi = new(destinationFilePath);
            return new BackupResult(true, destinationFilePath, fi.Length);
        }
        catch (Exception ex)
        {
            return new BackupResult(false, destinationFilePath, 0, $"Errore durante la creazione del backup cifrato: {ex.Message}");
        }
    }

    public async Task<RestoreResult> RestoreFromEncryptedBackupAsync(string sourceFilePath, string password, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourceFilePath))
        {
            return new RestoreResult(false, $"File di backup non trovato: '{sourceFilePath}'.");
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            return new RestoreResult(false, "Password mancante per il ripristino del backup.");
        }

        try
        {
            byte[] fileBytes = await File.ReadAllBytesAsync(sourceFilePath, cancellationToken);
            int minSize = MagicHeader.Length + SaltSizeBytes + NonceSizeBytes + TagSizeBytes;
            if (fileBytes.Length < minSize)
            {
                return new RestoreResult(false, "File di backup non valido o danneggiato.");
            }

            // Verify Magic Header
            Span<byte> header = fileBytes.AsSpan(0, MagicHeader.Length);
            if (!header.SequenceEqual(MagicHeader))
            {
                return new RestoreResult(false, "Formato file backup non riconosciuto (Header irreperibile).");
            }

            int offset = MagicHeader.Length;
            byte[] salt = fileBytes[offset..(offset + SaltSizeBytes)];
            offset += SaltSizeBytes;

            byte[] nonce = fileBytes[offset..(offset + NonceSizeBytes)];
            offset += NonceSizeBytes;

            byte[] tag = fileBytes[offset..(offset + TagSizeBytes)];
            offset += TagSizeBytes;

            byte[] cipherBytes = fileBytes[offset..];

            // Derive key
            byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, KeySizeBytes);

            byte[] plainBytes = new byte[cipherBytes.Length];

            // Decrypt AES-256-GCM
            try
            {
                using AesGcm aesGcm = new(key, TagSizeBytes);
                aesGcm.Decrypt(nonce, cipherBytes, tag, plainBytes);
            }
            catch (CryptographicException)
            {
                return new RestoreResult(false, "Password non valida o archivio backup corrotto (Autenticazione MAC fallita).");
            }

            // Extract zip
            using MemoryStream zipStream = new(plainBytes);
            using (ZipArchive archive = new(zipStream, ZipArchiveMode.Read))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    if (entry.FullName.Equals("onlyrag.db", StringComparison.OrdinalIgnoreCase))
                    {
                        entry.ExtractToFile(_storagePaths.DatabasePath, overwrite: true);
                    }
                    else if (entry.FullName.StartsWith("settings/", StringComparison.OrdinalIgnoreCase))
                    {
                        string subPath = entry.FullName["settings/".Length..];
                        string targetPath = Path.Combine(_storagePaths.DataRoot, subPath);
                        string? targetDir = Path.GetDirectoryName(targetPath);
                        if (!string.IsNullOrWhiteSpace(targetDir)) Directory.CreateDirectory(targetDir);
                        entry.ExtractToFile(targetPath, overwrite: true);
                    }
                }
            }

            return new RestoreResult(true);
        }
        catch (Exception ex)
        {
            return new RestoreResult(false, $"Errore durante il ripristino del backup: {ex.Message}");
        }
    }
}
