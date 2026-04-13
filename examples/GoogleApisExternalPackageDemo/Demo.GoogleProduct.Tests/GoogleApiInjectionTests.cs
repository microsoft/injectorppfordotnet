using Demo.GoogleProduct;
using Google.Apis.Auth;
using InjectorPP.Net;
using System.Reflection;

namespace Demo.GoogleProduct.Tests;

public class GoogleApiInjectionTests
{
    [Fact]
    public async Task TryAuthenticateAsync_ReturnsWorkspaceUser_WhenGoogleTokenIsValid()
    {
        using var injector = new Injector();
        injector.WhenCalled(GetValidateAsyncMethod())
            .WillReturn(Task.FromResult(new GoogleJsonWebSignature.Payload
            {
                Email = "ada@contoso.com",
                EmailVerified = true,
                Name = "Ada Lovelace",
                HostedDomain = "contoso.com"
            }));

        var signInService = new GoogleWorkspaceSignInService("contoso.com");

        GoogleWorkspaceUser? user = await signInService.TryAuthenticateAsync("not-a-real-google-id-token");

        Assert.NotNull(user);
        Assert.Equal("ada@contoso.com", user.Email);
        Assert.Equal("Ada Lovelace", user.DisplayName);
        Assert.Equal("contoso.com", user.HostedDomain);
    }

    [Fact]
    public async Task TryAuthenticateAsync_ReturnsNull_WhenEmailIsNotVerified()
    {
        using var injector = new Injector();
        injector.WhenCalled(GetValidateAsyncMethod())
            .WillReturn(Task.FromResult(new GoogleJsonWebSignature.Payload
            {
                Email = "sam@contoso.com",
                EmailVerified = false,
                Name = "Sam Example",
                HostedDomain = "contoso.com"
            }));

        var signInService = new GoogleWorkspaceSignInService("contoso.com");

        GoogleWorkspaceUser? user = await signInService.TryAuthenticateAsync("not-a-real-google-id-token");

        Assert.Null(user);
    }

    private static MethodInfo GetValidateAsyncMethod()
    {
        return typeof(GoogleJsonWebSignature).GetMethod(
            nameof(GoogleJsonWebSignature.ValidateAsync),
            new[]
            {
                typeof(string),
                typeof(GoogleJsonWebSignature.ValidationSettings)
            })!;
    }
}
