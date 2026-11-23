// Sirrow's AudioWorklet: plays the interleaved stereo chunks the page sends (sirrow-audio.js), 128 frames a call on the
// browser's audio thread, and reports back how many it has played. Run dry (the page's frames stalled) it plays silence
// and counts none of it, so the page's count of what is queued stays true.
class SirrowAudioProcessor extends AudioWorkletProcessor {
	constructor() {
		super();
		this.chunks = [];
		this.offset = 0;     // frames of chunks[0] already played
		this.played = 0;     // frames played since the last report
		this.calls = 0;
		this.port.onmessage = e => this.chunks.push(e.data);
	}

	process(inputs, outputs) {
		const output = outputs[0];
		const left = output[0];
		const right = output[1] ?? output[0];
		const frames = left.length;
		let i = 0;
		while (i < frames && this.chunks.length > 0) {
			const chunk = this.chunks[0];
			const n = Math.min(frames - i, chunk.length / 2 - this.offset);
			for (let f = 0; f < n; f++) {
				left[i + f] = chunk[(this.offset + f) * 2];
				right[i + f] = chunk[(this.offset + f) * 2 + 1];
			}
			i += n;
			this.offset += n;
			this.played += n;
			if (this.offset * 2 >= chunk.length) {
				this.chunks.shift();
				this.offset = 0;
			}
		}
		for (; i < frames; i++) {
			left[i] = 0;
			right[i] = 0;
		}
		// Every fourth call (about 11 ms at 48 kHz): often enough to keep the page's count close, rarely enough to cost nothing
		if (++this.calls >= 4) {
			this.port.postMessage(this.played);
			this.played = 0;
			this.calls = 0;
		}
		return true;
	}
}

registerProcessor('sirrow-audio', SirrowAudioProcessor);
