using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Bakery.Infrastructure.Services.Backup;

/// <summary>
/// Small Google Drive REST client. OAuth is opened only by the explicit administrator
/// connect command; normal uploads reuse an encrypted refresh token silently.
/// </summary>
public sealed class GoogleDriveCloudBackupService : ICloudBackupService
{
    internal const string DriveFileScope = "https://www.googleapis.com/auth/drive.file";
    private const string BackupFolderName = "BakeryERP Backups";
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromMinutes(30) };
    private readonly IConfiguration _configuration;
    private readonly IPermissionService _permissionService;
    private readonly IAuditService _auditService;
    private readonly GoogleTokenStore _tokenStore;
    private readonly ILogger<GoogleDriveCloudBackupService> _logger;
    private readonly IBackupStatusNotifier _statusNotifier;
    private readonly SemaphoreSlim _tokenGate = new(1, 1);

    public GoogleDriveCloudBackupService(
        IConfiguration configuration,
        IPermissionService permissionService,
        IAuditService auditService,
        GoogleTokenStore tokenStore,
        IBackupStatusNotifier statusNotifier,
        ILogger<GoogleDriveCloudBackupService> logger)
    {
        _configuration = configuration;
        _permissionService = permissionService;
        _auditService = auditService;
        _tokenStore = tokenStore;
        _statusNotifier = statusNotifier;
        _logger = logger;
    }

    public Task<bool> IsConnectedAsync(CancellationToken cancellationToken = default)
    {
        var token = _tokenStore.Load();
        return Task.FromResult(token is not null && !string.IsNullOrWhiteSpace(token.RefreshToken));
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.BackupConnectGoogleDrive);
        var clientId = GetRequiredConfiguration("GoogleDrive:ClientId");
        var clientSecret = GetRequiredConfiguration("GoogleDrive:ClientSecret");
        var state = Base64Url(RandomNumberGenerator.GetBytes(32));
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(64));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var port = GetAvailableLoopbackPort();
        var redirectUri = $"http://127.0.0.1:{port}/";
        var authorizationUri = BuildAuthorizationUri(clientId, redirectUri, state, challenge);

        using var listener = new HttpListener();
        listener.Prefixes.Add(redirectUri);
        listener.Start();
        Process.Start(new ProcessStartInfo(authorizationUri.AbsoluteUri) { UseShellExecute = true });
        var context = await listener.GetContextAsync().WaitAsync(cancellationToken);
        var code = context.Request.QueryString["code"];
        var returnedState = context.Request.QueryString["state"];
        var error = context.Request.QueryString["error"];
        await WriteBrowserResponseAsync(
            context.Response,
            error is null && code is not null && string.Equals(state, returnedState, StringComparison.Ordinal)
                ? "تم ربط Google Drive بنجاح. يمكنك إغلاق هذه الصفحة."
                : "تعذر ربط Google Drive. يمكنك إغلاق هذه الصفحة والمحاولة مرة أخرى.",
            cancellationToken);
        if (error is not null || string.IsNullOrWhiteSpace(code) || !string.Equals(state, returnedState, StringComparison.Ordinal))
            throw new InvalidOperationException("لم يكتمل تفويض Google Drive.");

        using var tokenResponse = await HttpClient.PostAsync(
            "https://oauth2.googleapis.com/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["code"] = code,
                ["code_verifier"] = verifier,
                ["grant_type"] = "authorization_code",
                ["redirect_uri"] = redirectUri
            }), cancellationToken);
        tokenResponse.EnsureSuccessStatusCode();
        var tokenPayload = await tokenResponse.Content.ReadAsStringAsync(cancellationToken);
        var response = JsonSerializer.Deserialize<GoogleTokenResponse>(tokenPayload)
            ?? throw new InvalidOperationException("Google Drive returned an invalid token response.");
        if (string.IsNullOrWhiteSpace(response.AccessToken) || string.IsNullOrWhiteSpace(response.RefreshToken))
            throw new InvalidOperationException("Google Drive did not return a reusable authorization token.");
        _tokenStore.Save(new GoogleToken(
            response.AccessToken,
            response.RefreshToken,
            DateTime.UtcNow.AddSeconds(Math.Max(60, response.ExpiresIn))));
        await TryAuditAsync(AuditActionKeys.GoogleDriveConnected, cancellationToken);
        _statusNotifier.NotifyChanged();
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.BackupDisconnectGoogleDrive);
        var token = _tokenStore.Load();
        _tokenStore.Delete();
        if (token is not null)
        {
            try
            {
                using var response = await HttpClient.PostAsync(
                    "https://oauth2.googleapis.com/revoke",
                    new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["token"] = token.RefreshToken
                    }), cancellationToken);
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "Google token revocation was unavailable; local credentials were removed");
            }
        }
        await TryAuditAsync(AuditActionKeys.GoogleDriveDisconnected, cancellationToken);
        _statusNotifier.NotifyChanged();
    }

    public async Task<string> UploadAsync(
        string localArchivePath,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(localArchivePath)) throw new FileNotFoundException("Local backup file is missing.", localArchivePath);
        var accessToken = await GetAccessTokenAsync(cancellationToken);
        var folderId = await EnsureBackupFolderAsync(accessToken, cancellationToken);
        var metadata = JsonSerializer.Serialize(new
        {
            name = fileName,
            parents = new[] { folderId },
            mimeType = "application/zip"
        });
        using var startRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "https://www.googleapis.com/upload/drive/v3/files?uploadType=resumable&fields=id");
        startRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var fileInfo = new FileInfo(localArchivePath);
        startRequest.Headers.TryAddWithoutValidation("X-Upload-Content-Type", "application/zip");
        startRequest.Headers.TryAddWithoutValidation("X-Upload-Content-Length", fileInfo.Length.ToString());
        startRequest.Content = new StringContent(metadata, Encoding.UTF8, "application/json");
        using var startResponse = await HttpClient.SendAsync(startRequest, cancellationToken);
        startResponse.EnsureSuccessStatusCode();
        var uploadUri = startResponse.Headers.Location
            ?? throw new InvalidOperationException("Google Drive did not provide an upload session.");

        await using var stream = new FileStream(
            localArchivePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var uploadRequest = new HttpRequestMessage(HttpMethod.Put, uploadUri);
        uploadRequest.Content = new StreamContent(stream, 128 * 1024);
        uploadRequest.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        uploadRequest.Content.Headers.ContentLength = fileInfo.Length;
        using var uploadResponse = await HttpClient.SendAsync(
            uploadRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        uploadResponse.EnsureSuccessStatusCode();
        var responseBody = await uploadResponse.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(responseBody);
        return document.RootElement.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("Google Drive upload did not return a file identifier.");
    }

    public async Task DownloadAsync(
        string cloudFileId,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.BackupRestore);
        if (string.IsNullOrWhiteSpace(cloudFileId)) throw new ArgumentException("Cloud file ID is required.", nameof(cloudFileId));
        var accessToken = await GetAccessTokenAsync(cancellationToken);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://www.googleapis.com/drive/v3/files/{Uri.EscapeDataString(cloudFileId)}?alt=media");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destinationPath))!);
        var partialPath = destinationPath + ".partial";
        try
        {
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var destination = new FileStream(
                partialPath, FileMode.Create, FileAccess.Write, FileShare.None,
                128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await source.CopyToAsync(destination, cancellationToken);
            await destination.FlushAsync(cancellationToken);
            File.Move(partialPath, destinationPath, true);
        }
        finally
        {
            try { if (File.Exists(partialPath)) File.Delete(partialPath); } catch { }
        }
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        await _tokenGate.WaitAsync(cancellationToken);
        try
        {
            var token = _tokenStore.Load()
                ?? throw new InvalidOperationException("Google Drive is not connected.");
            if (token.ExpiresAtUtc > DateTime.UtcNow.AddMinutes(2)) return token.AccessToken;
            var clientId = GetRequiredConfiguration("GoogleDrive:ClientId");
            var clientSecret = GetRequiredConfiguration("GoogleDrive:ClientSecret");
            using var response = await HttpClient.PostAsync(
                "https://oauth2.googleapis.com/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = clientId,
                    ["client_secret"] = clientSecret,
                    ["refresh_token"] = token.RefreshToken,
                    ["grant_type"] = "refresh_token"
                }), cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException("Google Drive authorization requires reconnection.");
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var refreshed = JsonSerializer.Deserialize<GoogleTokenResponse>(json)
                ?? throw new InvalidOperationException("Google Drive returned an invalid refresh response.");
            if (string.IsNullOrWhiteSpace(refreshed.AccessToken))
                throw new InvalidOperationException("Google Drive authorization requires reconnection.");
            token = token with
            {
                AccessToken = refreshed.AccessToken,
                ExpiresAtUtc = DateTime.UtcNow.AddSeconds(Math.Max(60, refreshed.ExpiresIn))
            };
            _tokenStore.Save(token);
            return token.AccessToken;
        }
        finally
        {
            _tokenGate.Release();
        }
    }

    private async Task<string> EnsureBackupFolderAsync(string accessToken, CancellationToken cancellationToken)
    {
        var query = $"name = '{BackupFolderName.Replace("'", "\\'")}' and mimeType = 'application/vnd.google-apps.folder' and trashed = false";
        using var listRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "https://www.googleapis.com/drive/v3/files?spaces=drive&fields=files(id)&pageSize=1&q=" + Uri.EscapeDataString(query));
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var listResponse = await HttpClient.SendAsync(listRequest, cancellationToken);
        listResponse.EnsureSuccessStatusCode();
        using (var document = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync(cancellationToken)))
        {
            var files = document.RootElement.GetProperty("files");
            if (files.GetArrayLength() > 0)
                return files[0].GetProperty("id").GetString()!;
        }

        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "https://www.googleapis.com/drive/v3/files?fields=id");
        createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        createRequest.Content = new StringContent(
            JsonSerializer.Serialize(new { name = BackupFolderName, mimeType = "application/vnd.google-apps.folder" }),
            Encoding.UTF8,
            "application/json");
        using var createResponse = await HttpClient.SendAsync(createRequest, cancellationToken);
        createResponse.EnsureSuccessStatusCode();
        using var created = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync(cancellationToken));
        return created.RootElement.GetProperty("id").GetString()!;
    }

    private string GetRequiredConfiguration(string key)
    {
        var value = _configuration[key];
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("إعدادات Google Drive غير مكتملة. راجع ملف إعدادات التطبيق.");
        return value;
    }

    private async Task TryAuditAsync(string action, CancellationToken cancellationToken)
    {
        try
        {
            await _auditService.LogAsync(action, "GoogleDrive", null, null,
                JsonSerializer.Serialize(new { Operation = action, Result = "Succeeded" }), cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Unable to write Google Drive audit action {Action}", action);
        }
    }

    private static int GetAvailableLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task WriteBrowserResponseAsync(
        HttpListenerResponse response,
        string message,
        CancellationToken cancellationToken)
    {
        var html = $"<!doctype html><html lang=\"ar\" dir=\"rtl\"><meta charset=\"utf-8\"><title>BakeryERP</title><body style=\"font-family:Segoe UI;padding:40px\"><h2>{WebUtility.HtmlEncode(message)}</h2></body></html>";
        var bytes = Encoding.UTF8.GetBytes(html);
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, cancellationToken);
        response.Close();
    }

    private static string BuildQuery(IEnumerable<KeyValuePair<string, string>> values)
        => string.Join("&", values.Select(item => $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(item.Value)}"));

    internal static Uri BuildAuthorizationUri(
        string clientId,
        string redirectUri,
        string state,
        string codeChallenge)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(redirectUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(codeChallenge);

        return new Uri("https://accounts.google.com/o/oauth2/v2/auth?" + BuildQuery(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = DriveFileScope,
            ["access_type"] = "offline",
            ["prompt"] = "consent",
            ["state"] = state,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256"
        }));
    }

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed class GoogleTokenResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("refresh_token")]
        public string RefreshToken { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }
}

public sealed class GoogleTokenStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("BakeryERP.GoogleDrive.v1");
    private readonly string _tokenPath;
    private readonly object _sync = new();

    public GoogleTokenStore(BackupPathProvider pathProvider)
    {
        _tokenPath = Path.Combine(pathProvider.ApplicationDataDirectory, "Secure", "google-drive-token.dat");
    }

    internal GoogleToken? Load()
    {
        if (!OperatingSystem.IsWindows()) return null;
        lock (_sync)
        {
            try
            {
                if (!File.Exists(_tokenPath)) return null;
                var encrypted = File.ReadAllBytes(_tokenPath);
                var clear = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
                return JsonSerializer.Deserialize<GoogleToken>(clear);
            }
            catch
            {
                return null;
            }
        }
    }

    internal void Save(GoogleToken token)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Google Drive credentials require Windows DPAPI.");
        lock (_sync)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_tokenPath)!);
            var clear = JsonSerializer.SerializeToUtf8Bytes(token);
            var encrypted = ProtectedData.Protect(clear, Entropy, DataProtectionScope.CurrentUser);
            var temporary = _tokenPath + ".tmp";
            File.WriteAllBytes(temporary, encrypted);
            File.Move(temporary, _tokenPath, true);
        }
    }

    public void Delete()
    {
        lock (_sync)
        {
            try { if (File.Exists(_tokenPath)) File.Delete(_tokenPath); } catch { }
        }
    }
}

internal sealed record GoogleToken(string AccessToken, string RefreshToken, DateTime ExpiresAtUtc);
