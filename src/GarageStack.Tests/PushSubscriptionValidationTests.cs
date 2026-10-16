using GarageStack.Api.Endpoints;

namespace GarageStack.Tests;

// The Worker POSTs every notification to a subscription's endpoint, so the API only stores one
// that points at a public push service, never at a host on the server's own network.
public class PushSubscriptionValidationTests
{
    private static string? Rule(string endpoint, string p256dh = "p256dh", string auth = "auth") =>
        PushEndpoints.ValidateSubscription(new PushSubscribeRequest(endpoint, p256dh, auth))?.Code;

    [Theory]
    [InlineData("https://fcm.googleapis.com/fcm/send/abc:APA91b")]
    [InlineData("https://updates.push.services.mozilla.com/wpush/v2/gAAAA")]
    [InlineData("https://web.push.apple.com/QGuQyavXutnMH")]
    [InlineData("https://wns2-am3p.notify.windows.com/w/?token=BQYAAA")]
    public void AcceptsTheBrowsersPushServices(string endpoint)
    {
        Assert.Null(Rule(endpoint));
    }

    [Theory]
    [InlineData("http://fcm.googleapis.com/fcm/send/abc")]
    [InlineData("https://192.168.1.30/hook")]
    [InlineData("https://10.0.0.1/hook")]
    [InlineData("https://[::1]/hook")]
    [InlineData("https://localhost/hook")]
    [InlineData("https://homeassistant.local/api/webhook/x")]
    [InlineData("https://router.lan/admin")]
    [InlineData("https://nas.home.arpa/x")]
    [InlineData("https://push.example:8443/x")]
    [InlineData("https://user:pass@push.example/x")]
    [InlineData("ftp://push.example/x")]
    [InlineData("not a url")]
    public void RefusesAnythingButAPublicHttpsName(string endpoint)
    {
        Assert.Equal("push.subscriptionInvalid", Rule(endpoint));
    }

    [Fact]
    public void RefusesAnEndpointTooLongForTheIndex()
    {
        Assert.Equal("push.subscriptionInvalid", Rule("https://push.example/" + new string('a', 2048)));
    }

    [Fact]
    public void RefusesOversizedKeys()
    {
        Assert.Equal("push.subscriptionInvalid", Rule("https://push.example/x", p256dh: new string('a', 257)));
    }

    [Theory]
    [InlineData("", "p256dh", "auth")]
    [InlineData("https://push.example/x", " ", "auth")]
    [InlineData("https://push.example/x", "p256dh", "")]
    public void NamesAMissingPartAsIncomplete(string endpoint, string p256dh, string auth)
    {
        Assert.Equal("push.subscriptionIncomplete", Rule(endpoint, p256dh, auth));
    }
}
