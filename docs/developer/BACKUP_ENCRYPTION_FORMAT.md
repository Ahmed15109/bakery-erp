# BakeryERP Backup Encryption Format v1

New backups use the `.berpbackup` extension. The file is an authenticated encrypted envelope around the existing ZIP payload; the ZIP still contains `metadata.json`, the SQL Server `.bak`, and application content needed by restore.

## Cryptographic construction

- Magic: ASCII `BKERPENC`
- Envelope version: `1`
- Encryption: AES-256-CBC with a random 128-bit IV and PKCS#7 padding
- Authentication: HMAC-SHA-256 over the complete header and ciphertext
- Construction: encrypt-then-MAC; restore verifies the HMAC in fixed time before decrypting any content
- Random salt: 128 bits per backup

The binary header contains the magic, version, key mode, derivation iteration count, salt length, IV length, salt, and IV. A 32-byte authentication tag follows the ciphertext.

## Key modes

`Device` mode is used for unattended automatic, safety, and ordinary manual backups. A random 256-bit master key is stored at `%LOCALAPPDATA%\BakeryERP\backup-encryption.key` and protected with Windows DPAPI `CurrentUser`. Per-backup encryption and authentication keys are independently derived from the master key and random salt. These backups are intended to be restored by the same Windows user profile; disaster-recovery procedures must preserve that Windows profile and its DPAPI material.

`Password` mode is used when `CreateBackupAsync(customPath, password)` or `BackupRequest.EncryptionPassword` supplies a password. The password must satisfy the application password policy. PBKDF2-HMAC-SHA-256 with 210,000 iterations derives separate 256-bit encryption and authentication keys. The password is not stored in metadata, history, logs, or the envelope.

## Compatibility and restore policy

- Restore and validation detect both v1 encrypted envelopes and legacy ZIP backups.
- Existing ZIP backups are read-only compatible and do not need conversion before restore.
- New backups are always encrypted; plain ZIP creation is no longer published as a final backup artifact.
- Password-protected backups require the password for validation and restore. Authentication failure is treated identically for a wrong password and corrupted ciphertext.
- Decrypted restore files exist only inside the centralized restore work directory and are removed after validation or restore. Stale work directories remain covered by startup cleanup.

This format is versioned so a future implementation can add a new algorithm or key mode without silently changing v1 interpretation.
