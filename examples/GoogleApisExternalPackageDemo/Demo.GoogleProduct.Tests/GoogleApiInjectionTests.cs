using Demo.GoogleProduct;
using Google.Apis.Auth;
using InjectorPP.Net;

namespace Demo.GoogleProduct.Tests;

public class GoogleApiInjectionTests
{
    [Fact]
    public async Task CanFakeGoogleJsonWebSignatureValidation()
    {
        using var injector = new Injector();
        injector.WhenCalled(
                typeof(GoogleJsonWebSignature).GetMethod(
                    nameof(GoogleJsonWebSignature.ValidateAsync),
                    new[]
                    {
                        typeof(string),
                        typeof(GoogleJsonWebSignature.ValidationSettings)
                    })!)
            .WillReturn(Task.FromResult(new GoogleJsonWebSignature.Payload
            {
                Email = "fake-user@example.com"
            }));

        var inspector = new GoogleTokenInspector();

        string? email = await inspector.GetEmailAsync("not-a-real-jwt");

        Assert.Equal("fake-user@example.com", email);
    }
}
