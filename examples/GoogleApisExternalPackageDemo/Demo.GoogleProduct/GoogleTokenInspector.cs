using Google.Apis.Auth;

namespace Demo.GoogleProduct;

public sealed class GoogleTokenInspector
{
    public async Task<string?> GetEmailAsync(string jwt)
    {
        GoogleJsonWebSignature.Payload payload =
            await GoogleJsonWebSignature.ValidateAsync(jwt, new GoogleJsonWebSignature.ValidationSettings());

        return payload.Email;
    }
}
