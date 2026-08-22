using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Cynara.Domain.Capabilities;
using Cynara.Domain.Hospitals;
using Cynara.Infrastructure.Modules.Identity;
using Cynara.Infrastructure.Persistence;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using OpenIddict.Abstractions;

namespace Cynara.Api.Tests.Support;

/// <summary>
/// WebApplicationFactory for the real-authentication suites. Disables the
/// F1 header seam so the host validates genuine OpenIddict tokens, and exposes
/// helpers to seed users, memberships, hospital workspaces, capability
/// assignments, and OpenIddict clients and to mint real tokens. The host
/// environment defaults to Development and can be overridden (for example to
/// Production) so environment-gated authorization behavior can be tested.
/// </summary>
internal sealed class IdentityAuthWebApplicationFactory(
    TestDatabaseSettings database,
    bool grantAllCapabilities = true,
    string? environment = null,
    TestOpenIddictCertificates? openIddictCertificates = null)
    : CynaraWebApplicationFactory(
        database,
        bootstrapOptions: null,
        emulateRenderProxy: false,
        grantAllCapabilities: grantAllCapabilities,
        useRealAuthentication: true,
        environment: environment,
        openIddictCertificates: openIddictCertificates)
{
    /// <summary>Confidential test client used by the auth suites.</summary>
    public const string ClientId = "cynara-test";

    public const string ClientSecret = "cynara-test-secret";

    public const string RedirectUri = "http://localhost:5173/en/login";

    public async Task<IdentityUser<Guid>> CreateUserAsync(
        string email,
        string password)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        UserManager<IdentityUser<Guid>> users = scope.ServiceProvider
            .GetRequiredService<UserManager<IdentityUser<Guid>>>();
        IdentityUser<Guid>? existing = await users.FindByNameAsync(email)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        IdentityUser<Guid> user = new()
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
        };
        IdentityResult result = await users.CreateAsync(user, password)
            .ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "Test user creation failed: "
                + string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        return user;
    }

    public async Task<Hospital> EnsureHospitalAsync(
        string code,
        string name,
        CancellationToken cancellationToken = default)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        CynaraDbContext dbContext = scope.ServiceProvider
            .GetRequiredService<CynaraDbContext>();
        Hospital? existing = await dbContext.Hospitals
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Code == code, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        Hospital hospital = new()
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = name,
            Status = HospitalStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _ = dbContext.Hospitals.Add(hospital);
        _ = await dbContext.SaveChangesAsync(cancellationToken)
            .ConfigureAwait(false);
        return hospital;
    }

    public async Task SeedMembershipAsync(
        IdentityUser<Guid> user,
        Guid hospitalId,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        CynaraIdentityDbContext identity = scope.ServiceProvider
            .GetRequiredService<CynaraIdentityDbContext>();
        bool exists = await identity.Memberships
            .AsNoTracking()
            .AnyAsync(
                item => item.UserId == user.Id
                    && item.HospitalId == hospitalId,
                cancellationToken)
            .ConfigureAwait(false);
        if (exists)
        {
            return;
        }

        identity.Memberships.Add(new Membership
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            HospitalId = hospitalId,
            ActorId = actorId,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _ = await identity.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SeedCapabilityAsync(
        Guid hospitalId,
        string actorId,
        string capability,
        CancellationToken cancellationToken = default)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        CynaraDbContext dbContext = scope.ServiceProvider
            .GetRequiredService<CynaraDbContext>();
        bool exists = await dbContext.CapabilityAssignments
            .AsNoTracking()
            .AnyAsync(
                item => item.HospitalId == hospitalId
                    && item.ActorId == actorId
                    && item.Capability == capability,
                cancellationToken)
            .ConfigureAwait(false);
        if (exists)
        {
            return;
        }

        dbContext.CapabilityAssignments.Add(new CapabilityAssignment
        {
            Id = Guid.NewGuid(),
            HospitalId = hospitalId,
            ActorId = actorId,
            Capability = capability,
            AssignedAt = DateTimeOffset.UtcNow,
            AssignedBy = "test-seed",
        });
        _ = await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Seeds a platform-scope capability grant for the actor. Platform rows
    /// authorize in every hospital context; the issuing hospital is stamped
    /// for traceability only and never narrows the grant.
    /// </summary>
    public async Task SeedPlatformCapabilityAsync(
        Guid hospitalId,
        string actorId,
        string capability,
        CancellationToken cancellationToken = default)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        CynaraDbContext dbContext = scope.ServiceProvider
            .GetRequiredService<CynaraDbContext>();
        bool exists = await dbContext.CapabilityAssignments
            .AsNoTracking()
            .AnyAsync(
                item => item.ActorId == actorId
                    && item.Capability == capability
                    && item.Scope == CapabilityScopes.Platform,
                cancellationToken)
            .ConfigureAwait(false);
        if (exists)
        {
            return;
        }

        dbContext.CapabilityAssignments.Add(new CapabilityAssignment
        {
            Id = Guid.NewGuid(),
            HospitalId = hospitalId,
            ActorId = actorId,
            Capability = capability,
            Scope = CapabilityScopes.Platform,
            AssignedAt = DateTimeOffset.UtcNow,
            AssignedBy = "test-seed",
        });
        _ = await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RegisterClientAsync(
        string? clientId = ClientId,
        string? clientSecret = ClientSecret,
        string? redirectUri = RedirectUri,
        bool includePassword = true,
        CancellationToken cancellationToken = default)
    {
        clientId ??= ClientId;
        clientSecret ??= ClientSecret;
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        IOpenIddictApplicationManager applications = scope.ServiceProvider
            .GetRequiredService<IOpenIddictApplicationManager>();
        object? existing = await applications
            .FindByClientIdAsync(clientId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return;
        }

        var permissions = new List<string>
        {
            OpenIddictConstants.Permissions.Endpoints.Authorization,
            OpenIddictConstants.Permissions.Endpoints.Token,
            OpenIddictConstants.Permissions.Endpoints.Revocation,
            OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
            OpenIddictConstants.Permissions.GrantTypes.RefreshToken,
            OpenIddictConstants.Permissions.GrantTypes.ClientCredentials,
            OpenIddictConstants.Permissions.ResponseTypes.Code,
            OpenIddictConstants.Permissions.Scopes.Email,
            OpenIddictConstants.Permissions.Scopes.Profile,
            "scp:openid",
            "scp:offline_access",
            "scp:cynara_api",
        };
        if (includePassword)
        {
            permissions.Add(OpenIddictConstants.Permissions.GrantTypes.Password);
        }

        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            ClientSecret = clientSecret,
            ConsentType = OpenIddictConstants.ConsentTypes.Implicit,
            DisplayName = "Cynara Test client",
            ClientType = OpenIddictConstants.ClientTypes.Confidential,
            RedirectUris = { new Uri(redirectUri!, UriKind.Absolute) },
        };
        foreach (string permission in permissions)
        {
            descriptor.Permissions.Add(permission);
        }

        _ = await applications.CreateAsync(
            descriptor,
            cancellationToken).ConfigureAwait(false);
    }

    public Task<AuthTokenResult> GetPasswordTokenAsync(
        string email,
        string password,
        string? clientId = ClientId,
        string? clientSecret = ClientSecret,
        CancellationToken cancellationToken = default)
    {
        return TokenFlowHelper.GetPasswordTokenAsync(
            this,
            clientId ?? ClientId,
            clientSecret ?? ClientSecret,
            email,
            password,
            cancellationToken);
    }

    public Task<AuthTokenResult> GetAuthorizationCodeTokenAsync(
        string email,
        string password,
        string? clientId = ClientId,
        string? clientSecret = ClientSecret,
        string? redirectUri = RedirectUri,
        CancellationToken cancellationToken = default)
    {
        return TokenFlowHelper.GetAuthorizationCodeTokenAsync(
            this,
            clientId ?? ClientId,
            clientSecret ?? ClientSecret,
            redirectUri ?? RedirectUri,
            email,
            password,
            cancellationToken);
    }
}

/// <summary>Parsed token response from <c>/connect/token</c>.</summary>
internal sealed record AuthTokenResult(
    string AccessToken,
    string RefreshToken,
    string IdToken,
    string AccessTokenType);

/// <summary>
/// Executes real OpenIddict token flows against a factory host: the
/// authorization-code + PKCE round trip and the Development-only password
/// grant. All returned tokens are genuine and validated by the host's
/// OpenIddict validation pipeline.
/// </summary>
internal static class TokenFlowHelper
{
    public static string CreatePkceVerifier()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(48);
        return Base64Url(bytes);
    }

    public static string CreatePkceChallenge(string verifier)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(verifier));
        return Base64Url(digest);
    }

    /// <summary>
    /// Detects the self-redirect OpenIddict issues when authorization-request
    /// caching is enabled: the first interaction never completes, it only
    /// hands the user agent an opaque request_uri transaction handle.
    /// </summary>
    public static bool IsOpaqueTransactionRedirect(
        HttpResponseMessage response,
        out string? requestUri)
    {
        if (response.StatusCode == HttpStatusCode.Redirect
            && response.Headers.Location is not null
            && QueryValue(response.Headers.Location, "code") is null)
        {
            requestUri = QueryValue(response.Headers.Location, "request_uri");
            return true;
        }

        requestUri = null;
        return false;
    }

    public static async Task<AuthTokenResult> GetPasswordTokenAsync(
        CynaraWebApplicationFactory factory,
        string clientId,
        string clientSecret,
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        using HttpClient client = factory.CreateClient();
        var body = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "password",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["username"] = username,
            ["password"] = password,
            ["scope"] = "openid profile email offline_access cynara_api",
        };
        return await ExchangeAsync(client, body, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<AuthTokenResult> GetAuthorizationCodeTokenAsync(
        CynaraWebApplicationFactory factory,
        string clientId,
        string clientSecret,
        string redirectUri,
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        const string state = "test-state";
        string verifier = CreatePkceVerifier();
        string challenge = CreatePkceChallenge(verifier);

        using var browser = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
            });
        browser.Timeout = TimeSpan.FromSeconds(120);

        var authorizeBody = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = "openid profile email offline_access cynara_api",
            ["state"] = state,
            ["nonce"] = "test-nonce",
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["email"] = username,
            ["password"] = password,
        };

        using FormUrlEncodedContent authorizeContent = new(authorizeBody);
        using HttpResponseMessage response = await browser
            .PostAsync("/connect/authorize", authorizeContent, cancellationToken)
            .ConfigureAwait(false);

        // The first interaction is intercepted by the authorization-request
        // cache: it returns an opaque transaction redirect instead of
        // completing. Complete the cached transaction with the credentials,
        // like a user agent submitting the login form.
        if (IsOpaqueTransactionRedirect(response, out string? requestUri))
        {
            response.Dispose();

            var credentialBody = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["client_id"] = clientId,
                ["request_uri"] = requestUri ?? string.Empty,
                ["email"] = username,
                ["password"] = password,
            };

            using FormUrlEncodedContent credentialContent = new(credentialBody);
            using HttpResponseMessage completed = await browser
                .PostAsync("/connect/authorize", credentialContent, cancellationToken)
                .ConfigureAwait(false);

            return await ExchangeAuthorizationCodeAsync(
                factory,
                completed,
                clientId,
                clientSecret,
                redirectUri,
                verifier,
                cancellationToken).ConfigureAwait(false);
        }

        return await ExchangeAuthorizationCodeAsync(
            factory,
            response,
            clientId,
            clientSecret,
            redirectUri,
            verifier,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<AuthTokenResult> ExchangeAuthorizationCodeAsync(
        CynaraWebApplicationFactory factory,
        HttpResponseMessage authorizeResponse,
        string clientId,
        string clientSecret,
        string redirectUri,
        string verifier,
        CancellationToken cancellationToken)
    {
        if (authorizeResponse.StatusCode != HttpStatusCode.Redirect)
        {
            string body = await authorizeResponse.Content.ReadAsStringAsync(
                cancellationToken).ConfigureAwait(false);
            string status = ((int)authorizeResponse.StatusCode).ToString(
                System.Globalization.CultureInfo.InvariantCulture);
            throw new InvalidOperationException(
                $"Authorize did not redirect; HTTP {status}: {body}");
        }

        Uri location = authorizeResponse.Headers.Location
            ?? throw new InvalidOperationException(
                "Authorize redirect missing Location.");
        string code = QueryValue(location, "code")
            ?? throw new InvalidOperationException(
                "Authorize redirect missing code.");

        var exchangeBody = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["code_verifier"] = verifier,
        };

        using HttpClient api = factory.CreateClient();
        return await ExchangeAsync(api, exchangeBody, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<AuthTokenResult> ExchangeAsync(
        HttpClient client,
        Dictionary<string, string> body,
        CancellationToken cancellationToken)
    {
        using FormUrlEncodedContent content = new(body);
        using HttpResponseMessage response = await client
            .PostAsync("/connect/token", content, cancellationToken)
            .ConfigureAwait(false);
        string text = await response.Content.ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            string status = ((int)response.StatusCode).ToString(
                System.Globalization.CultureInfo.InvariantCulture);
            throw new InvalidOperationException(
                $"Token endpoint HTTP {status}: {text}");
        }

        using var document = JsonDocument.Parse(text);
        JsonElement root = document.RootElement;
        return new AuthTokenResult(
            AccessToken: root.GetProperty("access_token").GetString()
                ?? throw new InvalidOperationException("access_token missing."),
            RefreshToken: root.TryGetProperty("refresh_token", out JsonElement refresh)
                ? refresh.GetString() ?? string.Empty
                : string.Empty,
            IdToken: root.TryGetProperty("id_token", out JsonElement id)
                ? id.GetString() ?? string.Empty
                : string.Empty,
            AccessTokenType: root.GetProperty("token_type").GetString()
                ?? "Bearer");
    }

    private static string Base64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string? QueryValue(Uri uri, string key)
    {
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers
            .ParseQuery(uri.Query);
        return query.TryGetValue(key, out Microsoft.Extensions.Primitives.StringValues value)
            ? value.ToString()
            : null;
    }
}
