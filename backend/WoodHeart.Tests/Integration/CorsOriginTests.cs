using WoodHeart.Presentation.Extensions;

namespace WoodHeart.Tests.Integration;

/// <summary>
/// Which origins the Development CORS policy accepts.
/// </summary>
/// <remarks>
/// <para>
/// The loopback rule exists because a fixed allowlist of
/// <c>http://localhost:4200</c> broke sign-in the moment the Angular app was
/// started from Visual Studio, which picks its own port. The symptom was
/// especially unhelpful: server-rendered pages worked, because Node fetches the
/// API with no Origin header and CORS never applies, so the site looked healthy
/// while every request the browser made for itself was refused.
/// </para>
/// <para>
/// The tests below are mostly about the <i>other</i> half: a rule that ignores
/// the port must not also ignore the host.
/// </para>
/// </remarks>
public class CorsOriginTests
{
    [Theory]
    [InlineData("http://localhost:4200")]
    [InlineData("http://localhost:53641")]
    [InlineData("http://localhost:5199")]
    [InlineData("http://127.0.0.1:4200")]
    [InlineData("https://localhost:7199")]
    public void Accepts_this_machine_on_any_port(string origin) =>
        // The port is ignored deliberately — a developer should not have to
        // reconfigure the API because their IDE chose a different one.
        CorsExtension.IsLoopback(origin).ShouldBeTrue(origin);

    [Theory]
    [InlineData("http://localhost.evil.example")]
    [InlineData("http://notlocalhost")]
    [InlineData("http://localhost.attacker.co.uk:4200")]
    [InlineData("https://mylocalhost.com")]
    public void Refuses_a_host_that_merely_contains_the_word(string origin) =>
        // The obvious implementation is `origin.Contains("localhost")`, and it
        // accepts every one of these. With AllowCredentials that would let any
        // of them read an authenticated response.
        CorsExtension.IsLoopback(origin).ShouldBeFalse(origin);

    [Theory]
    [InlineData("http://woodheart.com.bd")]
    [InlineData("https://woodheart.com.bd")]
    [InlineData("http://192.168.1.50:4200")]
    public void Refuses_anything_that_is_not_this_machine(string origin) =>
        CorsExtension.IsLoopback(origin).ShouldBeFalse(origin);

    [Theory]
    [InlineData("")]
    [InlineData("localhost:4200")]
    [InlineData("not a url")]
    [InlineData("file:///C:/index.html")]
    public void Refuses_anything_that_is_not_an_http_origin(string origin) =>
        // A relative or malformed value must be a refusal, not an exception:
        // this runs on every preflight, and throwing here would turn a bad
        // header into a 500 on requests that have not started yet.
        CorsExtension.IsLoopback(origin).ShouldBeFalse(origin);
}
