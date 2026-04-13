using Google.Apis.Auth;

namespace Demo.GoogleProduct;

public sealed record GoogleWorkspaceUser(
    string Email,
    string DisplayName,
    string HostedDomain);

public sealed class GoogleWorkspaceSignInService
{
    private readonly string _expectedHostedDomain;

    public GoogleWorkspaceSignInService(string expectedHostedDomain)
    {
        _expectedHostedDomain = expectedHostedDomain;
    }

    public async Task<GoogleWorkspaceUser?> TryAuthenticateAsync(string idToken)
    {
        GoogleJsonWebSignature.Payload payload =
            await GoogleJsonWebSignature.ValidateAsync(
                idToken,
                new GoogleJsonWebSignature.ValidationSettings
                {
                    HostedDomain = _expectedHostedDomain
                });

        if (!payload.EmailVerified ||
            !string.Equals(payload.HostedDomain, _expectedHostedDomain, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string displayName = string.IsNullOrWhiteSpace(payload.Name)
            ? payload.Email
            : payload.Name;

        return new GoogleWorkspaceUser(
            payload.Email,
            displayName,
            payload.HostedDomain);
    }
}
