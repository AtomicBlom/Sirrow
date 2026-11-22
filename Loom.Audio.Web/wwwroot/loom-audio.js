// The browser half of Loom.Audio.Web's audio device (WebAudioDevice), imported as the 'loom-audio' JS module by
// WebAudio.RegisterAsync. The mixer runs in C#, once a frame, a little ahead of the speaker; it hands each frame's samples
// here, and they go to an AudioWorklet (loom-audio-worklet.js) that plays them on the browser's audio thread. The worklet
// counts what it has played back to us, so the C# side always knows how far ahead of the speaker it is.
//
// A browser lets a page make sound only after the player has clicked, tapped or pressed a key in it, so the context
// starts suspended and resumes on the first such input. Until it runs, audioFramesWanted answers -1 and the C# side
// keeps the mix's time without sending anything.

let context = null;
let node = null;
let written = 0;   // frames sent to the worklet
let played = 0;    // frames the worklet reports it has played
let failure = null;

// Opens the output at `sampleRate` (the AudioContext resamples to the hardware's rate where it differs) and returns the
// rate it runs at. The worklet loads asynchronously; frames are wanted once it has.
export function audioOpen(sampleRate) {
	context = new AudioContext({ sampleRate, latencyHint: 'interactive' });
	const workletUrl = new URL('./loom-audio-worklet.js', import.meta.url);
	context.audioWorklet.addModule(workletUrl).then(() => {
		node = new AudioWorkletNode(context, 'loom-audio', { numberOfInputs: 0, numberOfOutputs: 1, outputChannelCount: [2] });
		node.port.onmessage = e => { played += e.data; };
		node.connect(context.destination);
	}).catch(e => {
		failure = e;
		console.error('Loom audio: the AudioWorklet would not load, so the app is silent.', e);
	});
	const unlock = () => {
		if (context && context.state === 'suspended')
			context.resume();
	};
	for (const type of ['pointerdown', 'keydown', 'touchend'])
		window.addEventListener(type, unlock, { capture: true });
	return context.sampleRate;
}

// How many frames to send to bring the worklet's queue up to `leadFrames`, or -1 while nothing can be heard (the page
// not yet clicked, the worklet still loading, or the context paused).
export function audioFramesWanted(leadFrames) {
	if (!node || failure || context.state !== 'running')
		return -1;
	const queued = written - played;
	return Math.min(leadFrames, Math.max(0, leadFrames - queued));
}

// Sends interleaved stereo float samples, as the bytes of a C# Span<float> (a MemoryView valid only for this call).
export function audioWrite(view) {
	const chunk = new Float32Array(view.byteLength / 4);
	view.copyTo(new Uint8Array(chunk.buffer));
	// Counted before it goes: transferring the buffer to the worklet empties this view of it
	written += chunk.length / 2;
	node.port.postMessage(chunk, [chunk.buffer]);
}

export function audioPause() {
	context?.suspend();
}

export function audioResume() {
	context?.resume();
}

// What the output is doing, for a person or a headless check at the console: its state, its rate, and the frames sent
// and played (sent minus played is how far the mix runs ahead of the speaker).
globalThis.__loomAudio = () => JSON.stringify({
	state: context?.state ?? 'closed',
	sampleRate: context?.sampleRate ?? 0,
	baseLatency: context?.baseLatency ?? 0,
	outputLatency: context?.outputLatency ?? 0,
	worklet: node !== null,
	failure: failure ? String(failure) : null,
	written,
	played,
});

export function audioClose() {
	node?.disconnect();
	context?.close();
	node = null;
	context = null;
}
