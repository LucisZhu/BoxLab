using System;
using UnityEngine;

namespace BoxLab
{
    public enum WorkshopSound { None, UI, Step, Push, Blocked, Undo, Gate, Rotate, Teleport, Win, Fall, IceSlide }

    /// <summary>Small original synthesized cues; explicit calls only, with no scene-load or gameplay hooks.</summary>
    public sealed class WorkshopAudio : MonoBehaviour
    {
        public const int MaxVoices = 4;
        public const float DefaultVolume = .65f;
        const int SampleRate = 44100;
        const int SoundCount = (int)WorkshopSound.IceSlide + 1;
        static WorkshopAudio instance;
        static float volume = DefaultVolume;
        static bool muted;
        static readonly double[] lastAccepted = InitialTimes();
        readonly AudioSource[] voices = new AudioSource[MaxVoices];
        readonly int[] priorities = new int[MaxVoices];
        readonly double[] startedAt = new double[MaxVoices];
        readonly AudioClip[] clips = new AudioClip[SoundCount];

        public static float Volume { get { return volume; } }
        public static bool Muted { get { return muted; } set { Configure(volume, value); } }
        /// <summary>Accepted playback requests, useful for verification; not a check of the output audio device.</summary>
        public static int PlayedCount { get; private set; }
        public static WorkshopSound LastSound { get; private set; }
        public static int ActiveVoiceCount
        {
            get
            {
                if (!instance) return 0;
                int count = 0;
                foreach (var source in instance.voices) if (source && source.isPlaying) count++;
                return count;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetSession()
        {
            StopAll();
            if (instance) Destroy(instance.gameObject);
            instance = null; volume = DefaultVolume; muted = false;
            PlayedCount = 0; LastSound = WorkshopSound.None;
            ResetThrottle();
        }

        /// <summary>Safe before first play. Muting or setting volume to zero immediately stops all voices.</summary>
        public static void Configure(float requestedVolume, bool requestedMuted)
        {
            volume = float.IsNaN(requestedVolume) || float.IsInfinity(requestedVolume) ? 0 : Mathf.Clamp01(requestedVolume);
            muted = requestedMuted;
            if (muted || volume <= 0) { StopAll(); return; }
            if (instance) foreach (var source in instance.voices) if (source) source.volume = volume;
        }
        public static void SetVolume(float requestedVolume) { Configure(requestedVolume, muted); }

        /// <summary>Returns false when muted, throttled, outside Play mode, or all voices are more important.</summary>
        public static bool Play(WorkshopSound sound)
        {
            int kind = (int)sound;
            if (!Application.isPlaying || kind <= 0 || kind >= SoundCount || muted || volume <= 0) return false;
            double now = Time.realtimeSinceStartupAsDouble;
            if (now - lastAccepted[kind] < Cooldown(sound)) return false;
            if (!instance)
            {
                var host = new GameObject("BoxLab workshop audio (runtime only)");
                host.hideFlags = HideFlags.DontSave;
                instance = host.AddComponent<WorkshopAudio>();
                DontDestroyOnLoad(host);
                instance.InitializeVoices();
            }
            int priority = Priority(sound), selected = -1;
            for (int i = 0; i < MaxVoices; i++)
                if (!instance.voices[i].isPlaying) { selected = i; break; }
            if (selected < 0)
            {
                selected = 0;
                for (int i = 1; i < MaxVoices; i++)
                    if (instance.priorities[i] < instance.priorities[selected] ||
                        instance.priorities[i] == instance.priorities[selected] && instance.startedAt[i] < instance.startedAt[selected]) selected = i;
                if (priority < instance.priorities[selected]) return false;
            }
            if (!instance.clips[kind]) instance.clips[kind] = BuildClip(sound);
            var source = instance.voices[selected];
            source.Stop(); source.clip = instance.clips[kind]; source.volume = volume; source.Play();
            instance.priorities[selected] = priority; instance.startedAt[selected] = now;
            lastAccepted[kind] = now; LastSound = sound; PlayedCount++;
            return true;
        }

        public static void StopAll()
        {
            if (instance) foreach (var source in instance.voices) if (source) { source.Stop(); source.clip = null; }
            ResetThrottle();
        }
        void InitializeVoices()
        {
            for (int i = 0; i < MaxVoices; i++)
            {
                var source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false; source.loop = false; source.spatialBlend = 0;
                source.dopplerLevel = 0; source.volume = volume; source.priority = 96;
                voices[i] = source;
            }
        }
        void OnDestroy()
        {
            foreach (var source in voices) if (source) source.Stop();
            foreach (var clip in clips) if (clip) Destroy(clip);
            if (instance == this) instance = null;
        }
        static double[] InitialTimes()
        {
            var result = new double[SoundCount];
            for (int i = 0; i < result.Length; i++) result[i] = double.NegativeInfinity;
            return result;
        }
        static void ResetThrottle() { for (int i = 0; i < lastAccepted.Length; i++) lastAccepted[i] = double.NegativeInfinity; }
        static double Cooldown(WorkshopSound sound)
        {
            switch (sound)
            {
                case WorkshopSound.UI: return .07;
                case WorkshopSound.Step: return .065;
                case WorkshopSound.Push: return .10;
                case WorkshopSound.Blocked: return .18;
                case WorkshopSound.Undo: return .10;
                case WorkshopSound.Gate: case WorkshopSound.Rotate: return .12;
                case WorkshopSound.Teleport: return .20;
                case WorkshopSound.Win: return .65;
                case WorkshopSound.Fall: return .40;
                case WorkshopSound.IceSlide: return .15;
                default: return .10;
            }
        }
        static int Priority(WorkshopSound sound)
        {
            switch (sound)
            {
                case WorkshopSound.Win: case WorkshopSound.Fall: return 6;
                case WorkshopSound.Teleport: return 4;
                case WorkshopSound.Gate: case WorkshopSound.Rotate: case WorkshopSound.Undo: case WorkshopSound.UI: return 3;
                case WorkshopSound.Push: case WorkshopSound.IceSlide: return 2;
                default: return 1;
            }
        }

        internal static AudioClip BuildClip(WorkshopSound sound)
        {
            float seconds;
            switch (sound)
            {
                case WorkshopSound.UI: seconds = .075f; break;
                case WorkshopSound.Step: seconds = .08f; break;
                case WorkshopSound.Push: seconds = .17f; break;
                case WorkshopSound.Blocked: seconds = .15f; break;
                case WorkshopSound.Undo: seconds = .27f; break;
                case WorkshopSound.Gate: seconds = .22f; break;
                case WorkshopSound.Rotate: seconds = .18f; break;
                case WorkshopSound.Teleport: seconds = .39f; break;
                case WorkshopSound.Win: seconds = .90f; break;
                case WorkshopSound.Fall: seconds = .45f; break;
                case WorkshopSound.IceSlide: seconds = .23f; break;
                default: seconds = .05f; break;
            }
            var samples = new float[Mathf.CeilToInt(seconds * SampleRate)];
            // Private deterministic noise never touches UnityEngine.Random or any puzzle state.
            uint noiseState = 0x29A47B19u + (uint)sound * 7919u;
            float softNoise = 0, peak = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / (float)SampleRate;
                noiseState = unchecked(1664525u * noiseState + 1013904223u);
                float noise = ((noiseState >> 8) / 8388607.5f) - 1;
                softNoise += .12f * (noise - softNoise);
                float sample;
                switch (sound)
                {
                    case WorkshopSound.UI:
                        // The UI tap alone is louder; gameplay cue gains and master volume are unchanged.
                        sample = .48f * (Chirp(t, 1050, -3200) * .75f + softNoise * .20f) * Mathf.Exp(-48 * t); break;
                    case WorkshopSound.Step:
                        sample = .19f * (Tone(t, 155) * .38f + softNoise * 1.5f) * Mathf.Exp(-40 * t); break;
                    case WorkshopSound.Push:
                        sample = .32f * (Chirp(t, 145, -280) * .63f + Tone(t, 320) * .15f + softNoise * 1.3f) * Mathf.Exp(-24 * t); break;
                    case WorkshopSound.Blocked:
                        sample = .22f * (Knock(t, 125) + .45f * Knock(t - .057f, 105) + softNoise * .35f * Mathf.Exp(-35 * t)); break;
                    case WorkshopSound.Undo:
                        sample = .20f * (Bell(t, 660) + .72f * Bell(t - .09f, 440)); break;
                    case WorkshopSound.Gate:
                        sample = .30f * (Knock(t, 98) + .32f * Knock(t - .065f, 165) + softNoise * 1.0f * Mathf.Exp(-17 * t)); break;
                    case WorkshopSound.Rotate:
                        sample = .19f * (Knock(t, 235) * .55f + Bell(t - .025f, 830) * .7f); break;
                    case WorkshopSound.Teleport:
                        sample = .18f * (Chirp(t, 390, 1550) * .60f + Chirp(t, 585, 2300) * .22f +
                            Bell(t - .19f, 990) * .40f) * Mathf.Exp(-2.8f * t); break;
                    case WorkshopSound.Win:
                        sample = .16f * (Bell(t, 523.25f) + .83f * Bell(t - .13f, 659.25f) +
                            .78f * Bell(t - .26f, 783.99f) + .82f * Bell(t - .42f, 1046.5f)); break;
                    case WorkshopSound.Fall:
                        sample = .24f * (Chirp(t, 255, -440) * .68f + softNoise * .8f) * Mathf.Exp(-5.7f * t); break;
                    case WorkshopSound.IceSlide:
                        // A breathy downward glide, rather than another impact, follows real extra ice travel.
                        float sweep = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / seconds));
                        sample = .24f * (softNoise * 1.35f + Chirp(t, 1160, -2600) * .22f) * sweep * Mathf.Exp(-3 * t); break;
                    default: sample = 0; break;
                }
                // Short attack and smooth tail avoid clicks; bounded mono samples leave mixer headroom.
                float attack = Mathf.SmoothStep(0, 1, Mathf.Clamp01(t / .0035f));
                float release = Mathf.SmoothStep(0, 1, Mathf.Clamp01((seconds - t) / .028f));
                samples[i] = sample * attack * release;
                peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
            }
            // Four simultaneous voices cannot exceed .88 even at full master volume.
            // Whole-clip scaling preserves timbre instead of clipping transient peaks.
            if (peak > .22f) for (int i = 0; i < samples.Length; i++) samples[i] *= .22f / peak;
            var clip = AudioClip.Create("BoxLab original cue - " + sound, samples.Length, 1, SampleRate, false);
            clip.SetData(samples, 0); return clip;
        }
        static float Tone(float t, float frequency) { return Mathf.Sin(2 * Mathf.PI * frequency * t); }
        static float Chirp(float t, float frequency, float rate) { return Mathf.Sin(2 * Mathf.PI * (frequency * t + .5f * rate * t * t)); }
        static float Knock(float t, float frequency)
        { return t < 0 ? 0 : (Tone(t, frequency) + .17f * Tone(t, frequency * 2.63f)) * Mathf.Exp(-34 * t); }
        static float Bell(float t, float frequency)
        {
            if (t < 0) return 0;
            float attack = Mathf.Clamp01(t / .004f);
            return attack * (Tone(t, frequency) * Mathf.Exp(-7.5f * t) +
                .22f * Tone(t, frequency * 2.76f) * Mathf.Exp(-13 * t) + .08f * Tone(t, frequency * 4.1f) * Mathf.Exp(-20 * t));
        }
    }
}
