using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Telemetry_Analytics_API.Services
{
    public sealed record AnalyticsAccessResult(bool Allowed, int StatusCode, int PlanTier, string Message);

    public interface IAnalyticsAccessService
    {
        Task<AnalyticsAccessResult> CheckAsync(string authorizationHeader, Guid agentId, CancellationToken cancellationToken);
    }

    public sealed class AnalyticsAccessService : IAnalyticsAccessService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string _authApiUrl;
        private readonly ILogger<AnalyticsAccessService> _logger;
        public AnalyticsAccessService(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<AnalyticsAccessService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _authApiUrl = configuration["AuthApiUrl"]?.TrimEnd('/')
                ?? throw new InvalidOperationException("AuthApiUrl is not configured");
        }

        public async Task<AnalyticsAccessResult> CheckAsync(string authorizationHeader, Guid agentId, CancellationToken cancellationToken)
        {
            if (!AuthenticationHeaderValue.TryParse(authorizationHeader, out var authorization) ||
                !string.Equals(authorization.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase))
            {
                return new(false, (int)HttpStatusCode.Unauthorized, 0, "A valid sign-in is required.");
            }

            try
            {
                using var client = _httpClientFactory.CreateClient();
                using var userResponse = await SendAsync(client, "/api/user/me", authorization, cancellationToken);
                if (userResponse.StatusCode == HttpStatusCode.Unauthorized)
                {
                    return new(false, (int)HttpStatusCode.Unauthorized, 0, "Your session is invalid or expired.");
                }
                if (!userResponse.IsSuccessStatusCode)
                {
                    return new(false, (int)HttpStatusCode.BadGateway, 0, "Could not verify the subscription with the auth service.");
                }

                await using var userStream = await userResponse.Content.ReadAsStreamAsync(cancellationToken);
                using var userJson = await JsonDocument.ParseAsync(userStream, cancellationToken: cancellationToken);
                int subscriptionLevel = ReadInt(userJson.RootElement, "subscriptionLevel");
                int planTier = subscriptionLevel + 1;
                if (planTier is not (1 or 2))
                {
                    return new(false, (int)HttpStatusCode.Forbidden, planTier, "An active analytics subscription is required.");
                }

                if (!await CanAccessAgentAsync(client, authorization, agentId, cancellationToken))
                {
                    return new(false, (int)HttpStatusCode.Forbidden, planTier, "You do not have access to this agent.");
                }

                return new(true, (int)HttpStatusCode.OK, planTier, string.Empty);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not verify analytics access for agent {AgentId}", agentId);
                return new(false, (int)HttpStatusCode.BadGateway, 0, "Could not verify your subscription or agent access. Check that the auth service is available.");
            }
        }

        private async Task<bool> CanAccessAgentAsync(HttpClient client, AuthenticationHeaderValue authorization, Guid agentId, CancellationToken cancellationToken)
        {
            using var mineResponse = await SendAsync(client, "/api/instance/my", authorization, cancellationToken);
            if (mineResponse.IsSuccessStatusCode)
            {
                await using var mineStream = await mineResponse.Content.ReadAsStreamAsync(cancellationToken);
                using var mineJson = await JsonDocument.ParseAsync(mineStream, cancellationToken: cancellationToken);
                if (ContainsId(mineJson.RootElement, agentId)) return true;
            }
            else if (!IsNotFoundOrForbidden(mineResponse.StatusCode))
            {
                return false;
            }

            using var workspacesResponse = await SendAsync(client, "/api/workspace/my", authorization, cancellationToken);
            if (!workspacesResponse.IsSuccessStatusCode) return false;

            await using var workspacesStream = await workspacesResponse.Content.ReadAsStreamAsync(cancellationToken);
            using var workspacesJson = await JsonDocument.ParseAsync(workspacesStream, cancellationToken: cancellationToken);
            foreach (JsonElement workspace in workspacesJson.RootElement.EnumerateArray())
            {
                Guid workspaceId = ReadGuid(workspace, "id");
                using var instancesResponse = await SendAsync(
                    client,
                    $"/api/instance/workspace={Uri.EscapeDataString(workspaceId.ToString())}",
                    authorization,
                    cancellationToken);
                if (!instancesResponse.IsSuccessStatusCode) continue;

                await using var instancesStream = await instancesResponse.Content.ReadAsStreamAsync(cancellationToken);
                using var instancesJson = await JsonDocument.ParseAsync(instancesStream, cancellationToken: cancellationToken);
                if (ContainsId(instancesJson.RootElement, agentId)) return true;
            }

            return false;
        }

        private async Task<HttpResponseMessage> SendAsync(HttpClient client, string path, AuthenticationHeaderValue authorization, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{_authApiUrl}{path}");
            request.Headers.Authorization = authorization;
            return await client.SendAsync(request, cancellationToken);
        }

        private static bool IsNotFoundOrForbidden(HttpStatusCode statusCode) =>
            statusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden;

        private static bool ContainsId(JsonElement array, Guid expectedId)
        {
            return array.ValueKind == JsonValueKind.Array && array.EnumerateArray().Any(item => ReadGuid(item, "id") == expectedId);
        }

        private static int ReadInt(JsonElement element, string propertyName)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out int number)) return number;
                    if (property.Value.ValueKind == JsonValueKind.String && int.TryParse(property.Value.GetString(), out number)) return number;
                }
            }
            throw new JsonException($"{propertyName} was not found in the auth API response.");
        }

        private static Guid ReadGuid(JsonElement element, string propertyName)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase) && property.Value.TryGetGuid(out Guid value))
                {
                    return value;
                }
            }
            return Guid.Empty;
        }
    }
}
