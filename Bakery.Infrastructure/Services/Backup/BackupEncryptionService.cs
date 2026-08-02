using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Bakery.Application.Interfaces;
using Bakery.Application.Security;

namespace Bakery.Infrastructure.Services.Backup;

/// <summary>
/// Implements the BakeryERP backup envelope. The inner ZIP is encrypted with
/// AES-256-CBC and authenticated with HMAC-SHA-256 (encrypt-then-MAC).
/// </summary>
public sealed class BackupEncryptionService
{
    private static readonly byte[] Magic = "BKERPENC"u8.ToArray();
    private static readonly byte[] DeviceKeyEntropy = "BakeryERP.BackupEncryption.v1"u8.ToArray();
    private const byte FormatVersion = 1;
    private const byte DeviceKeyMode = 1;
    private const byte PasswordKeyMode = 2;
    private const int PasswordIterations = 210_000;
    private const int SaltSize = 16;
    private const int IvSize = 16;
    private const int TagSize = 32;
    private const int FixedHeaderSize = 16;
    private const int HeaderSize = FixedHeaderSize + SaltSize + IvSize;

    private readonly IApplicationPathService _applicationPaths;
    private readonly SemaphoreSlim _deviceKeyGate = new(1, 1);

    public BackupEncryptionService(IApplicationPathService applicationPaths)
    {
        _applicationPaths = applicationPaths;
    }

    public async Task EncryptAsync(
        string plainArchivePath,
        string encryptedBackupPath,
        string? password,
        CancellationToken cancellationToken = default)
    {
        var passwordProtected = !string.IsNullOrWhiteSpace(password);
        if (passwordProtected) PasswordPolicy.EnsureValid(password);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var iv = RandomNumberGenerator.GetBytes(IvSize);
        var header = BuildHeader(passwordProtected ? PasswordKeyMode : DeviceKeyMode, salt, iv);
        var keyMaterial = await DeriveKeyMaterialAsync(
            passwordProtected ? PasswordKeyMode : DeviceKeyMode,
            password,
            salt,
            PasswordIterations,
            cancellationToken);

        try
        {
            await using var input = new FileStream(
                plainArchivePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var output = new FileStream(
                encryptedBackupPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);

            await output.WriteAsync(header, cancellationToken);
            using var hmac = new HMACSHA256(keyMaterial.MacKey);
            hmac.TransformBlock(header, 0, header.Length, header, 0);

            using (var authenticatedOutput = new HmacWriteStream(output, hmac))
            using (var aes = CreateAes(keyMaterial.EncryptionKey, iv))
            await using (var crypto = new CryptoStream(
                authenticatedOutput, aes.CreateEncryptor(), CryptoStreamMode.Write, leaveOpen: true))
            {
                await input.CopyToAsync(crypto, cancellationToken);
                crypto.FlushFinalBlock();
            }

            hmac.TransformFinalBlock([], 0, 0);
            await output.WriteAsync(hmac.Hash!, cancellationToken);
            await output.FlushAsync(cancellationToken);
            output.Flush(flushToDisk: true);
        }
        catch
        {
            TryDeleteFile(encryptedBackupPath);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyMaterial.EncryptionKey);
            CryptographicOperations.ZeroMemory(keyMaterial.MacKey);
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(iv);
            CryptographicOperations.ZeroMemory(header);
        }
    }

    public async Task<PreparedBackupArchive> PrepareReadAsync(
        string backupPath,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(backupPath)) throw new FileNotFoundException("ملف النسخة الاحتياطية غير موجود.", backupPath);

        await using var input = new FileStream(
            backupPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (input.Length < 2) throw new InvalidDataException("ملف النسخة الاحتياطية فارغ أو غير صالح.");

        var prefix = new byte[Magic.Length];
        var prefixLength = (int)Math.Min(input.Length, Magic.Length);
        await input.ReadExactlyAsync(prefix.AsMemory(0, prefixLength), cancellationToken);
        input.Position = 0;
        if (prefixLength >= 2 && prefix[0] == (byte)'P' && prefix[1] == (byte)'K')
            return PreparedBackupArchive.ForLegacyZip(backupPath);
        if (prefixLength < Magic.Length || !prefix.AsSpan().SequenceEqual(Magic))
            throw new InvalidDataException("تنسيق ملف النسخة الاحتياطية غير مدعوم.");
        if (input.Length < HeaderSize + TagSize + 16)
            throw new InvalidDataException("ملف النسخة الاحتياطية المشفر ناقص أو تالف.");

        var header = new byte[HeaderSize];
        await input.ReadExactlyAsync(header, cancellationToken);
        var parsed = ParseHeader(header);
        var cipherLength = input.Length - HeaderSize - TagSize;
        if (cipherLength <= 0 || cipherLength % 16 != 0)
            throw new InvalidDataException("ملف النسخة الاحتياطية المشفر ناقص أو تالف.");

        var keyMaterial = await DeriveKeyMaterialAsync(
            parsed.KeyMode,
            password,
            parsed.Salt,
            parsed.Iterations,
            cancellationToken);
        string? workingDirectory = null;
        try
        {
            var expectedTag = new byte[TagSize];
            input.Position = input.Length - TagSize;
            await input.ReadExactlyAsync(expectedTag, cancellationToken);
            var computedTag = await ComputeTagAsync(input, keyMaterial.MacKey, input.Length - TagSize, cancellationToken);
            var authenticated = CryptographicOperations.FixedTimeEquals(expectedTag, computedTag);
            CryptographicOperations.ZeroMemory(expectedTag);
            CryptographicOperations.ZeroMemory(computedTag);
            if (!authenticated)
                throw new BackupAuthenticationException();

            workingDirectory = Path.Combine(
                _applicationPaths.RestoreWorkDirectory,
                "decrypt-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workingDirectory);
            var plainArchivePath = Path.Combine(workingDirectory, "archive.zip");

            input.Position = HeaderSize;
            await using var limitedInput = new LimitedReadStream(input, cipherLength);
            using var aes = CreateAes(keyMaterial.EncryptionKey, parsed.Iv);
            await using var crypto = new CryptoStream(
                limitedInput, aes.CreateDecryptor(), CryptoStreamMode.Read, leaveOpen: true);
            await using var output = new FileStream(
                plainArchivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await crypto.CopyToAsync(output, cancellationToken);
            await output.FlushAsync(cancellationToken);
            output.Flush(flushToDisk: true);
            return PreparedBackupArchive.ForDecryptedZip(plainArchivePath, workingDirectory);
        }
        catch
        {
            if (workingDirectory is not null) BackupValidationService.TryDeleteDirectory(workingDirectory);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyMaterial.EncryptionKey);
            CryptographicOperations.ZeroMemory(keyMaterial.MacKey);
            CryptographicOperations.ZeroMemory(parsed.Salt);
            CryptographicOperations.ZeroMemory(parsed.Iv);
            CryptographicOperations.ZeroMemory(header);
        }
    }

    private static byte[] BuildHeader(byte keyMode, byte[] salt, byte[] iv)
    {
        var header = new byte[HeaderSize];
        Magic.CopyTo(header, 0);
        header[8] = FormatVersion;
        header[9] = keyMode;
        BinaryPrimitives.WriteInt32LittleEndian(
            header.AsSpan(10, 4),
            keyMode == PasswordKeyMode ? PasswordIterations : 0);
        header[14] = SaltSize;
        header[15] = IvSize;
        salt.CopyTo(header, FixedHeaderSize);
        iv.CopyTo(header, FixedHeaderSize + SaltSize);
        return header;
    }

    private static ParsedHeader ParseHeader(byte[] header)
    {
        if (!header.AsSpan(0, Magic.Length).SequenceEqual(Magic) || header[8] != FormatVersion)
            throw new InvalidDataException("إصدار تشفير النسخة الاحتياطية غير مدعوم.");
        var keyMode = header[9];
        var iterations = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(10, 4));
        if (keyMode is not (DeviceKeyMode or PasswordKeyMode) ||
            header[14] != SaltSize || header[15] != IvSize ||
            (keyMode == DeviceKeyMode && iterations != 0) ||
            (keyMode == PasswordKeyMode && (iterations < 100_000 || iterations > 2_000_000)))
        {
            throw new InvalidDataException("رأس تشفير النسخة الاحتياطية غير صالح.");
        }

        return new ParsedHeader(
            keyMode,
            iterations,
            header.AsSpan(FixedHeaderSize, SaltSize).ToArray(),
            header.AsSpan(FixedHeaderSize + SaltSize, IvSize).ToArray());
    }

    private async Task<KeyMaterial> DeriveKeyMaterialAsync(
        byte keyMode,
        string? password,
        byte[] salt,
        int iterations,
        CancellationToken cancellationToken)
    {
        if (keyMode == PasswordKeyMode)
        {
            if (string.IsNullOrWhiteSpace(password))
                throw new BackupPasswordRequiredException();
            var derived = Rfc2898DeriveBytes.Pbkdf2(
                password, salt, iterations, HashAlgorithmName.SHA256, 64);
            try
            {
                return new KeyMaterial(derived[..32], derived[32..]);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(derived);
            }
        }

        var deviceKey = await GetDeviceKeyAsync(cancellationToken);
        try
        {
            using var extract = new HMACSHA256(salt);
            var pseudoRandomKey = extract.ComputeHash(deviceKey);
            try
            {
                using var encryptionExpand = new HMACSHA256(pseudoRandomKey);
                using var macExpand = new HMACSHA256(pseudoRandomKey);
                return new KeyMaterial(
                    encryptionExpand.ComputeHash("BakeryERP backup encryption"u8.ToArray()),
                    macExpand.ComputeHash("BakeryERP backup authentication"u8.ToArray()));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(pseudoRandomKey);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(deviceKey);
        }
    }

    private async Task<byte[]> GetDeviceKeyAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("BakeryERP device-protected backups require Windows DPAPI.");

        await _deviceKeyGate.WaitAsync(cancellationToken);
        try
        {
            var keyPath = _applicationPaths.BackupEncryptionKeyFile;
            if (File.Exists(keyPath))
            {
                var protectedKey = await File.ReadAllBytesAsync(keyPath, cancellationToken);
                try
                {
                    var key = ProtectedData.Unprotect(
                        protectedKey, DeviceKeyEntropy, DataProtectionScope.CurrentUser);
                    if (key.Length != 32)
                    {
                        CryptographicOperations.ZeroMemory(key);
                        throw new CryptographicException("مفتاح تشفير النسخ الاحتياطية غير صالح.");
                    }
                    return key;
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(protectedKey);
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(keyPath)!);
            var newKey = RandomNumberGenerator.GetBytes(32);
            var protectedNewKey = ProtectedData.Protect(
                newKey, DeviceKeyEntropy, DataProtectionScope.CurrentUser);
            var temporaryPath = keyPath + ".new-" + Guid.NewGuid().ToString("N");
            try
            {
                await File.WriteAllBytesAsync(temporaryPath, protectedNewKey, cancellationToken);
                File.Move(temporaryPath, keyPath, overwrite: false);
                return newKey;
            }
            catch
            {
                CryptographicOperations.ZeroMemory(newKey);
                TryDeleteFile(temporaryPath);
                throw;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(protectedNewKey);
            }
        }
        finally
        {
            _deviceKeyGate.Release();
        }
    }

    private static async Task<byte[]> ComputeTagAsync(
        FileStream input,
        byte[] macKey,
        long authenticatedLength,
        CancellationToken cancellationToken)
    {
        input.Position = 0;
        using var hmac = new HMACSHA256(macKey);
        var buffer = new byte[128 * 1024];
        try
        {
            var remaining = authenticatedLength;
            while (remaining > 0)
            {
                var requested = (int)Math.Min(buffer.Length, remaining);
                var read = await input.ReadAsync(buffer.AsMemory(0, requested), cancellationToken);
                if (read == 0) throw new EndOfStreamException();
                hmac.TransformBlock(buffer, 0, read, buffer, 0);
                remaining -= read;
            }
            hmac.TransformFinalBlock([], 0, 0);
            return hmac.Hash!.ToArray();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }

    private static Aes CreateAes(byte[] key, byte[] iv)
    {
        var aes = Aes.Create();
        aes.KeySize = 256;
        aes.BlockSize = 128;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = key;
        aes.IV = iv;
        return aes;
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private sealed record ParsedHeader(byte KeyMode, int Iterations, byte[] Salt, byte[] Iv);
    private sealed record KeyMaterial(byte[] EncryptionKey, byte[] MacKey);
}

public sealed class PreparedBackupArchive : IDisposable
{
    private readonly string? _ownedDirectory;

    private PreparedBackupArchive(string archivePath, string? ownedDirectory)
    {
        ArchivePath = archivePath;
        _ownedDirectory = ownedDirectory;
    }

    public string ArchivePath { get; }
    public bool IsEncrypted => _ownedDirectory is not null;

    public static PreparedBackupArchive ForLegacyZip(string archivePath) => new(archivePath, null);
    public static PreparedBackupArchive ForDecryptedZip(string archivePath, string ownedDirectory) =>
        new(archivePath, ownedDirectory);

    public void Dispose()
    {
        if (_ownedDirectory is not null)
            BackupValidationService.TryDeleteDirectory(_ownedDirectory);
    }
}

internal sealed class HmacWriteStream : Stream
{
    private readonly Stream _inner;
    private readonly HMAC _hmac;

    public HmacWriteStream(Stream inner, HMAC hmac)
    {
        _inner = inner;
        _hmac = hmac;
    }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() => _inner.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);

    public override void Write(byte[] buffer, int offset, int count)
    {
        _hmac.TransformBlock(buffer, offset, count, buffer, offset);
        _inner.Write(buffer, offset, count);
    }

    public override async Task WriteAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        _hmac.TransformBlock(buffer, offset, count, buffer, offset);
        await _inner.WriteAsync(buffer.AsMemory(offset, count), cancellationToken);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        var copy = buffer.ToArray();
        try { Write(copy, 0, copy.Length); }
        finally { CryptographicOperations.ZeroMemory(copy); }
    }

    public override async ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        var copy = buffer.ToArray();
        try { await WriteAsync(copy, 0, copy.Length, cancellationToken); }
        finally { CryptographicOperations.ZeroMemory(copy); }
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}

internal sealed class LimitedReadStream : Stream
{
    private readonly Stream _inner;
    private long _remaining;

    public LimitedReadStream(Stream inner, long length)
    {
        _inner = inner;
        _remaining = length;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_remaining == 0) return 0;
        var read = _inner.Read(buffer, offset, (int)Math.Min(count, _remaining));
        _remaining -= read;
        return read;
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        if (_remaining == 0) return 0;
        var read = await _inner.ReadAsync(
            buffer[..(int)Math.Min(buffer.Length, _remaining)], cancellationToken);
        _remaining -= read;
        return read;
    }

    public override Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
