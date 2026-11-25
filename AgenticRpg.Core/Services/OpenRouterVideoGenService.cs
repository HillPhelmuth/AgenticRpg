using AgenticRpg.Core.Agents;
using AgenticRpg.Core.Models;
using AgenticRpg.Core.Models.Game;
using Azure.Storage.Blobs;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using static AgenticRpg.Core.Services.ImageGenService;
using ChatResponseFormat = Microsoft.Extensions.AI.ChatResponseFormat;

namespace AgenticRpg.Core.Services;

public class OpenRouterVideoGenService
{
    private const string VideoContainerUrl = "https://npcimages.z20.web.core.windows.net/";
    private static readonly JsonSerializerOptions JsonSerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly AgentConfiguration Configuration = AgentStaticConfiguration.Default;
    private static readonly HttpClient HttpClient = new();
    private static readonly BlobServiceClient BlobServiceClient = new(Configuration.BlobStorageConnectionString);
    private static readonly BlobContainerClient BlobContainerClient = BlobServiceClient.GetBlobContainerClient("$web");
    private static readonly ILogger<OpenRouterVideoGenService> _logger = Configuration.LoggerFactory.CreateLogger<OpenRouterVideoGenService>();
    private static readonly Uri VideoGenerationEndpoint = new(GetOpenRouterBaseUri(), "/api/alpha/videos");

    /// <summary>
    /// Submits an OpenRouter video generation job and persists the completed MP4 to blob storage.
    /// </summary>
    /// <param name="request">The OpenRouter video generation request payload.</param>
    /// <param name="campaignId">The campaign identifier used to organize uploaded videos.</param>
    /// <param name="filePrefix">The blob file name prefix to use for the uploaded MP4.</param>
    /// <param name="pollInterval">Optional polling interval for checking job completion.</param>
    /// <param name="timeout">Optional timeout for the entire generation lifecycle.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The blob URL of the persisted video.</returns>
    public static async Task<string> GenerateVideoAsync(
        OpenRouterVideoGenerationRequest request,
        string campaignId,
        string filePrefix = "OpenRouterVideo",
        TimeSpan? pollInterval = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var createdJob = await CreateVideoGenerationAsync(request, cancellationToken);
        var completedJob = await WaitForCompletionAsync(createdJob.Id, pollInterval, timeout, cancellationToken);
        var videoBytes = await DownloadVideoAsync(completedJob.Id, cancellationToken: cancellationToken);
        var blobName = BuildBlobName(campaignId, filePrefix);

        return await SaveAsUrl(blobName, videoBytes);
    }

    /// <summary>
    /// Submits an OpenRouter video generation job using the supplied prompt and common video options.
    /// </summary>
    /// <param name="prompt">The text prompt for the video.</param>
    /// <param name="campaignId">The campaign identifier used to organize uploaded videos.</param>
    /// <param name="model">The OpenRouter model slug.</param>
    /// <param name="durationSeconds">Optional video duration in seconds.</param>
    /// <param name="resolution">Optional resolution such as 720p or 1080p.</param>
    /// <param name="aspectRatio">Optional aspect ratio such as 16:9.</param>
    /// <param name="size">Optional explicit size such as 1280x720.</param>
    /// <param name="generateAudio">Whether audio should be co-generated when supported.</param>
    /// <param name="seed">Optional deterministic seed.</param>
    /// <param name="inputReferences">Optional image references for image-to-video generation.</param>
    /// <param name="pollInterval">Optional polling interval for checking job completion.</param>
    /// <param name="timeout">Optional timeout for the entire generation lifecycle.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The blob URL of the persisted video.</returns>
    public static Task<string> GenerateVideoAsync(
        string prompt,
        string campaignId,
        string model = "google/veo-3.1",
        int? durationSeconds = null,
        string? resolution = null,
        string? aspectRatio = null,
        string? size = null,
        bool? generateAudio = null,
        int? seed = null,
        IReadOnlyList<OpenRouterVideoInputReference>? inputReferences = null,
        int pollInterval = 10,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var request = new OpenRouterVideoGenerationRequest
        {
            Model = model,
            Prompt = prompt,
            Duration = durationSeconds,
            Resolution = resolution,
            AspectRatio = aspectRatio,
            Size = size,
            GenerateAudio = generateAudio,
            Seed = seed,
            InputReferences = inputReferences?.ToArray()
        };

        return GenerateVideoAsync(request, campaignId, pollInterval: TimeSpan.FromSeconds(pollInterval), timeout: timeout, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Creates an OpenRouter video generation job and returns the initial job metadata.
    /// </summary>
    /// <param name="request">The OpenRouter video generation request payload.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The initial job metadata returned by OpenRouter.</returns>
    public static async Task<OpenRouterVideoJob> CreateVideoGenerationAsync(
        OpenRouterVideoGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);

        using var httpRequest = CreateRequest(HttpMethod.Post, VideoGenerationEndpoint);
        httpRequest.Content = JsonContent.Create(request, options: JsonSerializerOptions);

        _logger.LogInformation("Submitting OpenRouter video generation request for model {Model}", request.Model);
        using var response = await HttpClient.SendAsync(httpRequest, cancellationToken);

        return await ReadJsonResponseAsync<OpenRouterVideoJob>(response, cancellationToken);
    }

    /// <summary>
    /// Fetches the latest status for an OpenRouter video generation job.
    /// </summary>
    /// <param name="jobId">The OpenRouter video job identifier.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The latest job metadata returned by OpenRouter.</returns>
    public static async Task<OpenRouterVideoJob> GetVideoGenerationAsync(
        string jobId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);

        using var request = CreateRequest(HttpMethod.Get, BuildJobUri(jobId));
        using var response = await HttpClient.SendAsync(request, cancellationToken);

        return await ReadJsonResponseAsync<OpenRouterVideoJob>(response, cancellationToken);
    }

    /// <summary>
    /// Polls OpenRouter until the requested job completes or reaches a terminal error state.
    /// </summary>
    /// <param name="jobId">The OpenRouter video job identifier.</param>
    /// <param name="pollInterval">Optional polling interval. OpenRouter recommends roughly 30 seconds.</param>
    /// <param name="timeout">Optional timeout for the polling loop.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The completed job metadata.</returns>
    public static async Task<OpenRouterVideoJob> WaitForCompletionAsync(
        string jobId,
        TimeSpan? pollInterval = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var effectivePollInterval = pollInterval ?? TimeSpan.FromSeconds(30);
        var timeoutAt = timeout is null ? (DateTimeOffset?)null : DateTimeOffset.UtcNow.Add(timeout.Value);
        var stopwatch = Stopwatch.StartNew();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (timeoutAt is not null && DateTimeOffset.UtcNow >= timeoutAt.Value)
            {
                throw new TimeoutException($"OpenRouter video generation timed out after {stopwatch.Elapsed:g} for job '{jobId}'.");
            }

            var job = await GetVideoGenerationAsync(jobId, cancellationToken);
            _logger.LogInformation("OpenRouter video job {JobId} status: {Status}. Elapsed: {Elapsed}", jobId, job.Status, stopwatch.Elapsed.ToString("g"));

            switch (job.Status)
            {
                case OpenRouterVideoStatus.Completed:
                    return job;
                case OpenRouterVideoStatus.Pending:
                case OpenRouterVideoStatus.InProgress:
                    await Task.Delay(effectivePollInterval, cancellationToken);
                    continue;
                case OpenRouterVideoStatus.Failed:
                case OpenRouterVideoStatus.Cancelled:
                case OpenRouterVideoStatus.Expired:
                    throw new InvalidOperationException($"OpenRouter video job '{jobId}' ended with status '{job.Status}'. Error: {job.Error ?? "Unknown error."}");
                default:
                    throw new InvalidOperationException($"OpenRouter video job '{jobId}' returned unexpected status '{job.Status}'.");
            }
        }
    }

    /// <summary>
    /// Downloads the generated MP4 bytes for a completed OpenRouter video job.
    /// </summary>
    /// <param name="jobId">The OpenRouter video job identifier.</param>
    /// <param name="index">The output video index to download.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The downloaded MP4 bytes.</returns>
    public static async Task<byte[]> DownloadVideoAsync(
        string jobId,
        int index = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        using var request = CreateRequest(HttpMethod.Get, BuildContentUri(jobId, index), "video/mp4");
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    public static async Task<string> GenerateCharacterIntroVideo(Character character, IntroPromptType promptType, string model = "bytedance/seedance-1-5-pro")
    {
        var promptTemplateFactory = new KernelPromptTemplateFactory();
        var args = new KernelArguments() { ["CHARACTER_NAME"] = character.Name, ["Race"] = character.Race.ToString(), ["Class"] = character.Class.ToString() };
        var kernel = Kernel.CreateBuilder().Build();
        var template = IntroPrompts.GetPrompt(promptType);
        var templateConfig = new PromptTemplateConfig(template);
        var instructions = await promptTemplateFactory.Create(templateConfig).RenderAsync(kernel, args);
        _logger.LogDebug("Rendered instructions for {CharacterName} Intro: {Instructions}", character.Name, instructions);
        // Get byte[] from character.ImageUrl;
        //using var client = new HttpClient();
        //var imageBytes = await client.GetByteArrayAsync(character.PortraitUrl!);
        //string imageMimeType = character.PortraitUrl.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg";
        return await GenerateVideoAsync(
            prompt: instructions,
            campaignId: character.CampaignId,
            model: model,
            durationSeconds: 10,
            inputReferences: [OpenRouterVideoInputReference.FromUrl(character.PortraitUrl!)],
            generateAudio: true,
            pollInterval: 10,
            timeout: TimeSpan.FromMinutes(10));
    }
    private static async Task<VideoInstructionsOutput> GeneratePromptFromGameState(GameState gameState,
        string? additionalInstructions = null)
    {
        var agent = new OpenAIClient(new ApiKeyCredential(Configuration.OpenAIApiKey))
            .GetChatClient("gpt-4.1")
            .AsAIAgent(new ChatClientAgentOptions()
            {
                Name = "Video Gen Instruction Agent",
                ChatOptions = new ChatOptions()
                {
                    Instructions = VideoGenService.VideoGenEventInstructions,
                    ResponseFormat = ChatResponseFormat.ForJsonSchema<VideoInstructionsOutput>()
                }
            }, loggerFactory: Configuration.LoggerFactory);
        var prompt =
            $"""
             ## Player Party Characters
             {string.Join("\n\n", gameState.Characters.Select(x => x.AsBasicDataMarkdown()))}

             ## Latest Events
             {string.Join("\n\n", gameState.RecentNarratives.OrderByDescending(x => x.Timestamp).Select(x => $"Timestamp: {x.Timestamp:g}\n\n {x.Content}")).Take(10)}
             ## Additional Instructions 
             _If any additional instructions for the video generation, they are the most important and should play a very large role in the video prompt._

             **additional instructions:**
             {additionalInstructions}
             """;
        var response = await agent.RunAsync<VideoInstructionsOutput>(prompt);
        return response.Result;
    }
    /// <summary>
    /// Downloads a completed OpenRouter video and stores it in blob storage.
    /// </summary>
    /// <param name="jobId">The OpenRouter video job identifier.</param>
    /// <param name="campaignId">The campaign identifier used to organize uploaded videos.</param>
    /// <param name="filePrefix">The blob file name prefix to use for the uploaded MP4.</param>
    /// <param name="index">The output video index to download.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The blob URL of the persisted video.</returns>
    public static async Task<string> DownloadVideoToBlobAsync(
        string jobId,
        string campaignId,
        string filePrefix = "OpenRouterVideo",
        int index = 0,
        CancellationToken cancellationToken = default)
    {
        var bytes = await DownloadVideoAsync(jobId, index, cancellationToken);
        var blobName = BuildBlobName(campaignId, filePrefix);

        return await SaveAsUrl(blobName, bytes);
    }

    // Creates an authenticated request message for the OpenRouter alpha video endpoint.
    private static HttpRequestMessage CreateRequest(HttpMethod method, Uri uri, string acceptMediaType = "application/json")
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Configuration.OpenRouterApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(acceptMediaType));
        return request;
    }

    // Resolves the OpenRouter host from the configured chat endpoint so host overrides still work.
    private static Uri GetOpenRouterBaseUri()
    {
        var configuredEndpoint = string.IsNullOrWhiteSpace(Configuration.OpenRouterEndpoint)
            ? "https://openrouter.ai/api/v1"
            : Configuration.OpenRouterEndpoint;

        var endpointUri = new Uri(configuredEndpoint, UriKind.Absolute);
        return new Uri(endpointUri.GetLeftPart(UriPartial.Authority));
    }

    // Builds the polling URI for a specific OpenRouter video job.
    private static Uri BuildJobUri(string jobId)
    {
        return new($"{VideoGenerationEndpoint.ToString().TrimEnd('/')}/{jobId}");
    }

    // Builds the content download URI for a specific OpenRouter video output.
    private static Uri BuildContentUri(string jobId, int index)
    {
        return new($"{VideoGenerationEndpoint.ToString().TrimEnd('/')}/{jobId}/content?index={index}");
    }

    // Validates provider constraints that the alpha API documents explicitly.
    private static void ValidateRequest(OpenRouterVideoGenerationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Model);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Prompt);

        if (!string.IsNullOrWhiteSpace(request.Size) &&
            (!string.IsNullOrWhiteSpace(request.Resolution) || !string.IsNullOrWhiteSpace(request.AspectRatio)))
        {
            throw new ArgumentException("OpenRouter video requests cannot combine size with resolution or aspect_ratio.");
        }
    }

    // Reads a JSON response and throws with provider details when the request fails.
    private static async Task<T> ReadJsonResponseAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"OpenRouter video request failed with status {(int)response.StatusCode}: {content}");
        }

        var result = JsonSerializer.Deserialize<T>(content, JsonSerializerOptions);
        return result ?? throw new InvalidOperationException($"OpenRouter returned an empty or invalid JSON payload for {typeof(T).Name}.");
    }

    // Ensures a non-JSON endpoint completed successfully while preserving the response body in the error.
    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException($"OpenRouter video request failed with status {(int)response.StatusCode}: {content}");
    }

    // Uses the same campaign-scoped blob naming pattern as the existing video generation service.
    private static string BuildBlobName(string campaignId, string filePrefix)
    {
        var campaignIdWithUscore = ConvertNonAlphaNumericToUnderscore(campaignId);
        if (string.IsNullOrWhiteSpace(campaignIdWithUscore))
        {
            campaignIdWithUscore = "UnknownCampaign";
        }
        return @$"{campaignIdWithUscore}\{filePrefix}_{Guid.NewGuid():N}.mp4";
    }

    // Uploads the generated MP4 to blob storage and returns its public URL.
    private static async Task<string> SaveAsUrl(string fileName, byte[] bytes)
    {
        var blobClient = BlobContainerClient.GetBlobClient(fileName);
        await blobClient.UploadAsync(new BinaryData(bytes), overwrite: true);
        return $"{VideoContainerUrl}/{fileName}";
    }
}

/// <summary>
/// Represents an OpenRouter alpha video generation request.
/// </summary>
public class OpenRouterVideoGenerationRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; set; }

    [JsonPropertyName("prompt")]
    public required string Prompt { get; set; }

    [JsonPropertyName("duration")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Duration { get; set; }

    [JsonPropertyName("resolution")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Resolution { get; set; }

    [JsonPropertyName("aspect_ratio")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AspectRatio { get; set; }

    [JsonPropertyName("size")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Size { get; set; }

    [JsonPropertyName("input_references")]
    public IReadOnlyList<OpenRouterVideoInputReference>? InputReferences { get; set; }

    [JsonPropertyName("generate_audio")]
    public bool? GenerateAudio { get; set; } = true;

    [JsonPropertyName("seed")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Seed { get; set; }
}

/// <summary>
/// Represents an OpenRouter alpha video generation job.
/// </summary>
public class OpenRouterVideoJob
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    [JsonPropertyName("polling_url")]
    public string? PollingUrl { get; set; }

    [JsonPropertyName("status")]
    public required string Status { get; set; }

    [JsonPropertyName("unsigned_urls")]
    public IReadOnlyList<string>? UnsignedUrls { get; set; }

    [JsonPropertyName("usage")]
    public OpenRouterVideoUsage? Usage { get; set; }

    [JsonPropertyName("generation_id")]
    public string? GenerationId { get; set; }

    [JsonPropertyName("error")]
    public object? Error { get; set; }
}

/// <summary>
/// Represents a reference image payload for OpenRouter image-to-video requests.
/// </summary>
public class OpenRouterVideoInputReference
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "image_url";

    [JsonPropertyName("image_url")]
    public required OpenRouterImageUrlReference ImageUrl { get; set; }

    /// <summary>
    /// Creates an HTTPS image reference that OpenRouter can consume for image-to-video generation.
    /// </summary>
    /// <param name="url">The HTTPS or data URI image reference.</param>
    /// <returns>A populated input reference instance.</returns>
    public static OpenRouterVideoInputReference FromUrl(string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        return new OpenRouterVideoInputReference { ImageUrl = new OpenRouterImageUrlReference { Url = url } };
    }
}

/// <summary>
/// Represents the image_url content part structure for OpenRouter video inputs.
/// </summary>
public class OpenRouterImageUrlReference
{
    [JsonPropertyName("url")]
    public required string Url { get; set; }
}

/// <summary>
/// Represents OpenRouter billing usage returned for a completed video generation.
/// </summary>
public class OpenRouterVideoUsage
{
    [JsonPropertyName("cost")]
    public decimal? Cost { get; set; }

    [JsonPropertyName("is_byok")]
    public bool? IsByok { get; set; }
}

/// <summary>
/// Known terminal and transitional statuses returned by OpenRouter video generation jobs.
/// </summary>
public static class OpenRouterVideoStatus
{
    public const string Pending = "pending";
    public const string InProgress = "in_progress";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
    public const string Expired = "expired";
}