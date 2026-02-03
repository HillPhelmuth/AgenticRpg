using AgenticRpg.Core.Agents;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using OpenAI;
using OpenAI.Audio;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
#pragma warning disable OPENAI001

namespace AgenticRpg.Core.Services;

public class TextToSpeechService
{
    private static readonly AgentConfiguration Configuration = AgentStaticConfiguration.Default;
    private const string WebContainerBaseUrl = "https://npcimages.z20.web.core.windows.net/";
    private static BlobServiceClient BlobServiceClient => new(Configuration.BlobStorageConnectionString);
    private static BlobContainerClient BlobContainerClient => BlobServiceClient.GetBlobContainerClient("$web");
    private const string AudioCachePrefix = "tts";
    private const string AudioExtension = ".pcm";
    private const string VoiceInstructions = """
                                             Tone: Sardonic, scathing, and weary—like a seasoned pro who has spent too many years in smoky comedy clubs.

                                             Emotion: Hostile amusement. The voice should sound like it is perpetually mocking the listener’s intelligence.

                                             Delivery: Sharp, staccato pacing with punchy emphasis. Include dismissive "pfft" sounds, short condescending chuckles, and dramatic pauses after insults to let the "sting" land.
                                             """;
    public static async IAsyncEnumerable<byte[]> GenerateSpeechAsync(string text, string? messageId = null, CancellationToken token = default)
    {
        if (!string.IsNullOrWhiteSpace(messageId))
        {
            var cachedBlobName = GetSpeechBlobName(messageId);
            var cachedBlobClient = BlobContainerClient.GetBlobClient(cachedBlobName);

            if (await cachedBlobClient.ExistsAsync(token))
            {
                await foreach (var chunk in DownloadBlobAsChunksAsync(cachedBlobClient).WithCancellation(token))
                {
                    yield return chunk;
                }

                yield break;
            }
        }

        var client = new OpenAIClient(Configuration.OpenAIApiKey).GetAudioClient("gpt-4o-mini-tts");
        // split text into 1000 char chunks if too long

        var maxChunkSize = 1000;
        var cachedBytes = !string.IsNullOrWhiteSpace(messageId) ? new MemoryStream() : null;
        for (int i = 0; i < text.Length; i += maxChunkSize)
        {
            var chunk = text.Substring(i, Math.Min(maxChunkSize, text.Length - i));
            var prompt = $"{chunk}";
            var response = await client.GenerateSpeechAsync(prompt, GeneratedSpeechVoice.Ballad, new SpeechGenerationOptions()
            {
                Instructions = VoiceInstructions,
                SpeedRatio = 1.1f,
                ResponseFormat = GeneratedSpeechFormat.Pcm
            }, token);
            var data = response.Value.ToArray();

            if (cachedBytes is not null)
            {
                await cachedBytes.WriteAsync(data, token);
            }

            yield return data;
        }

        if (cachedBytes is not null)
        {
            var blobName = GetSpeechBlobName(messageId!);
            var blobClient = BlobContainerClient.GetBlobClient(blobName);
            cachedBytes.Position = 0;
            await blobClient.UploadAsync(cachedBytes, new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders
                {
                    ContentType = "audio/L16",
                }
            }, token);
        }

    }

    private static string GetSpeechBlobName(string messageId)
        => $"{AudioCachePrefix}/{messageId}{AudioExtension}";

    private static async IAsyncEnumerable<byte[]> DownloadBlobAsChunksAsync(BlobClient blobClient)
    {
        const int bufferSize = 64 * 1024;

        await using var stream = await blobClient.OpenReadAsync();
        var buffer = new byte[bufferSize];

        while (true)
        {
            var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
            if (bytesRead <= 0)
            {
                yield break;
            }

            var chunk = new byte[bytesRead];
            Buffer.BlockCopy(buffer, 0, chunk, 0, bytesRead);
            yield return chunk;
        }
    }

    private static async Task<string> SaveAsUrl(string fileName, byte[] bytes)
    {
        var blobClient = BlobContainerClient.GetBlobClient(fileName);
        await blobClient.UploadAsync(new BinaryData(bytes), overwrite: true);
        return $"{WebContainerBaseUrl}/{fileName}";
    }
}