using System.Net;
using System.Text;
using Chater.Models;
using Chater.Models.Enums;
using Chater.Services;

namespace Chater.Providers;

public sealed class ProviderConnectionTester(HttpClient? httpClient = null) : IProviderConnectionTester
{
    private readonly HttpClient _httpClient = httpClient ?? new HttpClient();

    public async Task<ProviderConnectionResult> TestAsync(ApiProvider provider, CancellationToken cancellationToken = default)
    {
        try
        {
            ProviderService.Validate(provider);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            using var request = CreateRequest(provider);
            using var response = await _httpClient.SendAsync(request, timeout.Token).ConfigureAwait(false);
            return response.IsSuccessStatusCode ? ProviderConnectionResult.Success() : MapStatusCode(response.StatusCode);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ProviderConnectionResult(false, "request_timeout", "连接测试超时。");
        }
        catch (HttpRequestException)
        {
            return new ProviderConnectionResult(false, "network_error", "无法连接到服务商端点。");
        }
        catch (ArgumentException exception)
        {
            return new ProviderConnectionResult(false, "invalid_configuration", exception.Message);
        }
    }

    private static HttpRequestMessage CreateRequest(ApiProvider provider)
    {
        if (provider.ProviderType == ProviderType.Anthropic)
        {
            var endpoint = CombineEndpoint(provider.Endpoint ?? "https://api.anthropic.com", "v1/messages");
            var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent($"{{\"model\":\"{provider.ModelId}\",\"max_tokens\":1,\"messages\":[{{\"role\":\"user\",\"content\":\"ping\"}}]}}", Encoding.UTF8, "application/json")
            };
            request.Headers.Add("x-api-key", provider.ApiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");
            return request;
        }

        var path = provider.ProviderType == ProviderType.Ollama ? "api/tags" : "models";
        var baseEndpoint = provider.Endpoint ?? "https://api.openai.com/v1";
        var openAiRequest = new HttpRequestMessage(HttpMethod.Get, CombineEndpoint(baseEndpoint, path));
        if (provider.ProviderType != ProviderType.Ollama)
        {
            openAiRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", provider.ApiKey);
        }

        return openAiRequest;
    }

    private static Uri CombineEndpoint(string baseEndpoint, string path) => new(new Uri(baseEndpoint.TrimEnd('/') + "/", UriKind.Absolute), path);

    private static ProviderConnectionResult MapStatusCode(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new(false, "authentication_failed", "认证失败，请检查 API Key。"),
        HttpStatusCode.TooManyRequests => new(false, "rate_limited", "服务商限流，请稍后重试。"),
        HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout => new(false, "service_unavailable", "服务商暂不可用，请稍后重试。"),
        _ => new(false, "unexpected_response", $"服务商返回 HTTP {(int)statusCode}。")
    };
}
