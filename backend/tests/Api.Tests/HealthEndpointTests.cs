using System.Net;
using System.Net.Http.Json;

namespace Api.Tests;

[Collection(ApiCollection.Name)]
public class HealthEndpointTests(ApiFactory factory)
{
    [Fact]
    public async Task Get_Health_Returns200_WithHealthyStatus()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Equal("healthy", body?["status"]);
    }

    [Fact]
    public async Task Responses_Include_RequestId_Header()
    {
        var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("X-Request-ID", "trace-123");

        var response = await client.SendAsync(request);

        Assert.Equal("trace-123", response.Headers.GetValues("X-Request-ID").Single());
    }
}
