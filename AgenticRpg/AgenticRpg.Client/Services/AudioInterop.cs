using System;
using System.Threading.Tasks;
using Microsoft.JSInterop;

namespace AgenticRpg.Client.Services;

/// <summary>
/// Provides JavaScript interop helpers for playing PCM audio in the browser.
/// </summary>
public sealed class AudioInterop : IAsyncDisposable
{
    private readonly Lazy<Task<IJSObjectReference>> _moduleTask;

    /// <summary>
    /// Initializes a new instance of the <see cref="AudioInterop"/> class.
    /// </summary>
    /// <param name="jsRuntime">The JS runtime for module loading.</param>
    public AudioInterop(IJSRuntime jsRuntime)
    {
        // # Reason: Defer module loading until audio playback is requested to reduce startup work.
        _moduleTask = new(() => jsRuntime.InvokeAsync<IJSObjectReference>(
            "import", "./audioInterop.js").AsTask());
    }

    /// <summary>
    /// Plays a 16-bit little-endian PCM chunk.
    /// </summary>
    /// <param name="pcmData">The raw PCM bytes.</param>
    /// <param name="sampleRate">The sample rate in Hz (default: 24000).</param>
    /// <param name="channels">The number of interleaved channels (default: 1).</param>
    /// <returns>A task that completes when the chunk is queued for playback.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="sampleRate"/> or <paramref name="channels"/> is invalid.</exception>
    public async ValueTask PlayPcmAsync(byte[] pcmData, int sampleRate = 24000, int channels = 1)
    {
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        if (channels <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(channels));
        }

        if (pcmData.Length == 0)
        {
            return;
        }

        var module = await _moduleTask.Value;
        await module.InvokeVoidAsync("playPcm", pcmData, sampleRate, channels);
    }

    /// <summary>
    /// Stops playback and resets the internal audio context.
    /// </summary>
    /// <returns>A task that completes when the audio context is stopped.</returns>
    public async ValueTask StopAsync()
    {
        var module = await _moduleTask.Value;
        await module.InvokeVoidAsync("stop");
    }

    /// <summary>
    /// Disposes the JS module when it is no longer needed.
    /// </summary>
    /// <returns>A task that completes when resources are released.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_moduleTask.IsValueCreated)
        {
            var module = await _moduleTask.Value;
            await module.DisposeAsync();
        }
    }
}
