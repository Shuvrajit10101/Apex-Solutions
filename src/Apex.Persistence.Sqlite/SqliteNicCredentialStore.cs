using System.Security.Cryptography;
using System.Text;
using Apex.Ledger.Domain;
using Microsoft.Data.Sqlite;

namespace Apex.Persistence.Sqlite;

/// <summary>
/// The cross-platform, SQLite-backed <see cref="INicCredentialStore"/> (Phase 9 slice 4a; RQ-30; ER-16). It is the
/// <b>only</b> writer/reader of the <c>nic_*_enc</c> ciphertext-BLOB columns on <c>companies</c>, keyed by GSTIN. The
/// customer's own NIC-IRP API credentials are stored <b>protected-at-rest</b> (never plaintext) and are structurally
/// absent from the canonical export — the pure company INSERT/SELECT never touches these columns, and no
/// <c>GstConfig</c> / DTO member carries a secret. The app holds no GSP/vendor credential and no portal password/DSC.
/// <para>
/// <b>Cross-platform protection (deferred hardening):</b> to keep the Linux/macOS/Windows CI build green, the at-rest
/// protection uses only in-BCL AES-CBC (no Windows-only DPAPI / <c>ProtectedData</c> dependency). The AES key is derived
/// from a fixed application pepper — this is <b>obfuscation-grade placeholder protection</b> for the DEFERRED live path
/// (the offline default stores <b>no</b> credential at all). The intended production hardening is a per-OS keystore
/// (Windows DPAPI <c>CurrentUser</c>, macOS Keychain, Linux Secret Service), swapped in when the live NIC path is built;
/// the interface + column layout are already in place so that swap needs no schema change.
/// </para>
/// </summary>
public sealed class SqliteNicCredentialStore : INicCredentialStore, IDisposable
{
    // A fixed application pepper the AES key is derived from. NOTE: obfuscation-grade placeholder for the deferred live
    // path (see class remarks); NOT a substitute for a real OS keystore. It protects nothing in the offline default,
    // which stores no credential. De-branded (ER-11).
    private static readonly byte[] KeyMaterial =
        SHA256.HashData(Encoding.UTF8.GetBytes("Apex.Solutions/nic-credential-at-rest/v1"));

    private readonly SqliteConnection _connection;

    /// <summary>Opens the company database at <paramref name="databasePath"/> for credential storage. Holds a single
    /// long-lived connection; dispose to release the file handle.</summary>
    public SqliteNicCredentialStore(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
            throw new ArgumentException("A database path is required.", nameof(databasePath));

        var connStr = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
        }.ToString();
        _connection = new SqliteConnection(connStr);
        _connection.Open();
    }

    /// <inheritdoc />
    public bool HasCredentials(string gstin)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT nic_client_id_enc FROM companies WHERE gstin = $g;";
        cmd.Parameters.AddWithValue("$g", gstin);
        using var r = cmd.ExecuteReader();
        return r.Read() && !r.IsDBNull(0);
    }

    /// <inheritdoc />
    public NicApiCredentials Get(string gstin)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT nic_client_id_enc, nic_client_secret_enc, nic_api_username_enc, nic_api_password_enc
            FROM companies WHERE gstin = $g;
            """;
        cmd.Parameters.AddWithValue("$g", gstin);
        using var r = cmd.ExecuteReader();
        if (!r.Read() || r.IsDBNull(0))
            throw new InvalidOperationException($"No NIC credential is stored for GSTIN '{gstin}'.");

        return new NicApiCredentials(
            Unprotect((byte[])r.GetValue(0)),
            Unprotect((byte[])r.GetValue(1)),
            Unprotect((byte[])r.GetValue(2)),
            Unprotect((byte[])r.GetValue(3)));
    }

    /// <inheritdoc />
    public void Store(string gstin, NicApiCredentials creds)
    {
        ArgumentNullException.ThrowIfNull(creds);
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            UPDATE companies SET
                nic_client_id_enc     = $cid,
                nic_client_secret_enc = $secret,
                nic_api_username_enc  = $user,
                nic_api_password_enc  = $pass
            WHERE gstin = $g;
            """;
        cmd.Parameters.AddWithValue("$cid", Protect(creds.ClientId));
        cmd.Parameters.AddWithValue("$secret", Protect(creds.ClientSecret));
        cmd.Parameters.AddWithValue("$user", Protect(creds.ApiUsername));
        cmd.Parameters.AddWithValue("$pass", Protect(creds.ApiPassword));
        cmd.Parameters.AddWithValue("$g", gstin);
        if (cmd.ExecuteNonQuery() == 0)
            throw new InvalidOperationException($"No company with GSTIN '{gstin}' to store NIC credentials against.");
    }

    /// <inheritdoc />
    public void Clear(string gstin)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            UPDATE companies SET
                nic_client_id_enc = NULL, nic_client_secret_enc = NULL,
                nic_api_username_enc = NULL, nic_api_password_enc = NULL
            WHERE gstin = $g;
            """;
        cmd.Parameters.AddWithValue("$g", gstin);
        cmd.ExecuteNonQuery();
    }

    // ------------------------------------------------------------------ at-rest protection (placeholder — see remarks)

    private static byte[] Protect(string plaintext)
    {
        using var aes = Aes.Create();
        aes.Key = KeyMaterial;
        aes.GenerateIV();
        using var enc = aes.CreateEncryptor();
        var body = Encoding.UTF8.GetBytes(plaintext);
        var cipher = enc.TransformFinalBlock(body, 0, body.Length);
        // Prepend the IV so the value is self-describing and never equals the plaintext bytes (round-trippable).
        var result = new byte[aes.IV.Length + cipher.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(cipher, 0, result, aes.IV.Length, cipher.Length);
        return result;
    }

    private static string Unprotect(byte[] protectedBytes)
    {
        using var aes = Aes.Create();
        aes.Key = KeyMaterial;
        var ivLen = aes.BlockSize / 8;
        var iv = new byte[ivLen];
        Buffer.BlockCopy(protectedBytes, 0, iv, 0, ivLen);
        aes.IV = iv;
        using var dec = aes.CreateDecryptor();
        var plain = dec.TransformFinalBlock(protectedBytes, ivLen, protectedBytes.Length - ivLen);
        return Encoding.UTF8.GetString(plain);
    }

    public void Dispose() => _connection.Dispose();
}
