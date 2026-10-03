// A short two-note chime made on the fly: no audio file to load, nothing to allow in the CSP.

let context: AudioContext | null = null

/** Browsers start audio only in answer to a click: called from one, it lets later chimes play. */
export function unlock(): void {
  if (typeof AudioContext === 'undefined') return
  try {
    context ??= new AudioContext()
    void context.resume().catch(() => {})
  } catch {
    context = null
  }
}

export function chime(): void {
  if (!context || context.state !== 'running') return
  const start = context.currentTime
  for (const [i, frequency] of [880, 1320].entries()) {
    const at = start + i * 0.12
    const tone = context.createOscillator()
    const volume = context.createGain()
    tone.frequency.value = frequency
    volume.gain.setValueAtTime(0.0001, at)
    volume.gain.exponentialRampToValueAtTime(0.15, at + 0.01)
    volume.gain.exponentialRampToValueAtTime(0.0001, at + 0.25)
    tone.connect(volume).connect(context.destination)
    tone.start(at)
    tone.stop(at + 0.3)
  }
}
