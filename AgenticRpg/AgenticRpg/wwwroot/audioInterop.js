let audioContext = null;
let nextPlaybackTime = 0;
let configuredSampleRate = 0;

function ensureAudioContext(sampleRate)
{
	if (audioContext && configuredSampleRate === sampleRate)
	{
		return audioContext;
	}

	if (audioContext)
	{
		audioContext.close();
	}

	const AudioContextType = window.AudioContext || window.webkitAudioContext;
	audioContext = new AudioContextType({ sampleRate });
	configuredSampleRate = audioContext.sampleRate;
	nextPlaybackTime = 0;
	return audioContext;
}

async function resumeIfSuspended(context)
{
	if (context.state === "suspended")
	{
		await context.resume();
	}
}

function decodePcm(pcmBytes, channels)
{
	const frameCount = Math.floor(pcmBytes.length / (2 * channels));
	if (frameCount <= 0)
	{
		return { frameCount: 0, samples: null };
	}

	const sampleCount = frameCount * channels;
	const samples = new Int16Array(pcmBytes.buffer, pcmBytes.byteOffset, sampleCount);
	return { frameCount, samples };
}

export async function playPcm(pcmBytes, sampleRate = 24000, channels = 1)
{
	if (!pcmBytes || pcmBytes.length === 0)
	{
		return;
	}

	if (channels <= 0)
	{
		throw new Error("Channels must be greater than zero.");
	}

	const context = ensureAudioContext(sampleRate);
	await resumeIfSuspended(context);

	const { frameCount, samples } = decodePcm(pcmBytes, channels);
	if (frameCount === 0)
	{
		return;
	}

	const buffer = context.createBuffer(channels, frameCount, configuredSampleRate);
	for (let channelIndex = 0; channelIndex < channels; channelIndex += 1)
	{
		const channelData = buffer.getChannelData(channelIndex);
		let sampleIndex = channelIndex;
		for (let frameIndex = 0; frameIndex < frameCount; frameIndex += 1)
		{
			channelData[frameIndex] = samples[sampleIndex] / 32768;
			sampleIndex += channels;
		}
	}

	const source = context.createBufferSource();
	source.buffer = buffer;
	source.connect(context.destination);

	const startTime = Math.max(context.currentTime, nextPlaybackTime);
	source.start(startTime);
	nextPlaybackTime = startTime + buffer.duration;
}

export async function stop()
{
	nextPlaybackTime = 0;

	if (audioContext)
	{
		await audioContext.close();
		audioContext = null;
		configuredSampleRate = 0;
	}
}
