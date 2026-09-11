using UnityEngine;

/// <summary>
/// Zero-Dependency Procedural Audio Synthesizer for BoxBot Action Mode.
/// Synthesizes high-fidelity sci-fi sound effects directly in memory at runtime via AudioClip.Create,
/// eliminating external WAV dependencies and providing tabletop AR spatialized playback.
/// </summary>
public class BoxBotAudioSynthesizer : MonoBehaviour
{
    private const int SAMPLE_RATE = 44100;

    [Header("=== Multi-Node AR Audio Sources ===")]
    [SerializeField] private AudioSource chestAudioSource;
    [SerializeField] private AudioSource armAudioSource;

    // In-memory synthesized clips
    private AudioClip servoGlideClip;
    private AudioClip chargeWhineClip;
    private AudioClip repulsorBlastClip;
    private AudioClip unibeamSustainClip;
    private AudioClip steamVentClip;

    private bool isInitialized = false;

    private void Awake()
    {
        InitializeAudio();
    }

    public void InitializeAudio()
    {
        if (isInitialized) return;

        // Auto-configure AudioSources if not set
        if (chestAudioSource == null)
        {
            Transform core = transform.Find("Torso/ReactorCore") ?? transform.Find("Torso") ?? transform;
            chestAudioSource = core.GetComponent<AudioSource>();
            if (chestAudioSource == null) chestAudioSource = core.gameObject.AddComponent<AudioSource>();
        }

        if (armAudioSource == null)
        {
            Transform arm = transform.Find("RightArmPivot") ?? transform;
            armAudioSource = arm.GetComponent<AudioSource>();
            if (armAudioSource == null) armAudioSource = arm.gameObject.AddComponent<AudioSource>();
        }

        ConfigureAudioSource(chestAudioSource, 0.06f, 2.5f, 45f);
        ConfigureAudioSource(armAudioSource, 0.05f, 2.0f, 40f);

        // Synthesize clips into RAM
        servoGlideClip = SynthesizeServoGlide(0.55f);
        chargeWhineClip = SynthesizeChargeWhine(0.85f);
        repulsorBlastClip = SynthesizeRepulsorBlast(0.45f);
        unibeamSustainClip = SynthesizeUnibeamSustain(1.80f);
        steamVentClip = SynthesizeSteamVent(0.65f);

        isInitialized = true;
    }

    private void ConfigureAudioSource(AudioSource src, float minD, float maxD, float spread)
    {
        if (src == null) return;
        src.spatialBlend = 1.0f; // 100% 3D spatialization
        src.minDistance = minD;   // 6cm close tabletop inspection
        src.maxDistance = maxD;   // 2.5m room distance
        src.spread = spread;
        src.dopplerLevel = 0.15f; // Clamped to prevent camera hand-shake pitch wobbles
        src.rolloffMode = AudioRolloffMode.Logarithmic;
        src.playOnAwake = false;
    }

    #region Procedural Waveform Synthesis

    // Phase 1: High-torque mechanical servo glide
    private AudioClip SynthesizeServoGlide(float duration)
    {
        int samples = Mathf.CeilToInt(SAMPLE_RATE * duration);
        float[] data = new float[samples];
        float phase1 = 0f, subPhase = 0f;

        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / SAMPLE_RATE;
            float progress = t / duration;

            float freq = Mathf.Lerp(220f, 540f, Mathf.Sin(progress * Mathf.PI * 0.9f));
            phase1 += (2f * Mathf.PI * freq) / SAMPLE_RATE;
            subPhase += (2f * Mathf.PI * (freq * 0.5f)) / SAMPLE_RATE;

            float saw = (Mathf.Sin(phase1) + 0.4f * Mathf.Sin(phase1 * 2.01f)) * 0.6f;
            float sub = Mathf.Sin(subPhase) * 0.4f;

            float env = 1f;
            if (t < 0.03f) env = t / 0.03f;
            else if (t > duration - 0.10f) env = (duration - t) / 0.10f;

            data[i] = (saw + sub) * env * 0.65f;
        }

        AudioClip clip = AudioClip.Create("Synth_ServoGlide", samples, 1, SAMPLE_RATE, false);
        clip.SetData(data, 0);
        return clip;
    }

    // Phase 2: Exponential turbine whine & capacitor thrum
    private AudioClip SynthesizeChargeWhine(float duration)
    {
        int samples = Mathf.CeilToInt(SAMPLE_RATE * duration);
        float[] data = new float[samples];
        float subPhase = 0f, turbinePhase = 0f, ionPhase = 0f;

        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / SAMPLE_RATE;
            float progress = t / duration;

            // Sub-bass rumble 38Hz -> 65Hz
            float subFreq = Mathf.Lerp(38f, 65f, progress);
            subPhase += (2f * Mathf.PI * subFreq) / SAMPLE_RATE;
            float tremolo = 0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * 8f * t);
            float sub = Mathf.Sin(subPhase) * tremolo;

            // Exponential turbine rise: 350Hz -> 2800Hz
            float turbineFreq = 350f * Mathf.Pow(2800f / 350f, Mathf.Pow(progress, 1.8f));
            float vibrato = Mathf.Sin(2f * Mathf.PI * 18f * t) * (progress * 35f);
            turbinePhase += (2f * Mathf.PI * (turbineFreq + vibrato)) / SAMPLE_RATE;
            float turbine = Mathf.Sin(turbinePhase) + 0.3f * Mathf.Sin(turbinePhase * 2f);

            // 120Hz rectified electrical hum
            ionPhase += (2f * Mathf.PI * 120f) / SAMPLE_RATE;
            float ion = Mathf.Sin(ionPhase) * 0.35f;

            // Exponential swell
            float swell = Mathf.Pow(progress, 1.4f);
            // Pre-fire tension vacuum dip (50ms before end)
            if (progress > 0.94f) swell *= 0.08f;

            data[i] = (sub * 0.45f + turbine * 0.45f + ion * 0.2f) * swell;
        }

        AudioClip clip = AudioClip.Create("Synth_ChargeWhine", samples, 1, SAMPLE_RATE, false);
        clip.SetData(data, 0);
        return clip;
    }

    // Phase 3: High-energy repulsor pulse blast (transient crack + steep pitch dive)
    private AudioClip SynthesizeRepulsorBlast(float duration)
    {
        int samples = Mathf.CeilToInt(SAMPLE_RATE * duration);
        float[] data = new float[samples];
        System.Random rand = new System.Random();
        float chirpPhase = 0f;

        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / SAMPLE_RATE;

            // Supersonic crack (0.5ms attack, 20ms decay)
            float crack = 0f;
            if (t < 0.035f)
            {
                float noise = (float)(rand.NextDouble() * 2.0 - 1.0);
                crack = noise * Mathf.Exp(-t / 0.006f) * 1.6f;
            }

            // Steep pitch drop: 1200Hz down to 85Hz in 60ms
            float chirpFreq = Mathf.Max(85f, 1200f * Mathf.Exp(-t * 40f));
            chirpPhase += (2f * Mathf.PI * chirpFreq) / SAMPLE_RATE;
            float chirp = Mathf.Sin(chirpPhase) * Mathf.Exp(-t * 6f);

            data[i] = Mathf.Clamp(crack * 0.8f + chirp * 0.7f, -1f, 1f);
        }

        AudioClip clip = AudioClip.Create("Synth_RepulsorBlast", samples, 1, SAMPLE_RATE, false);
        clip.SetData(data, 0);
        return clip;
    }

    // Phase 3: Sustained Arc Reactor Unibeam roar & singing harmonic core
    private AudioClip SynthesizeUnibeamSustain(float duration)
    {
        int samples = Mathf.CeilToInt(SAMPLE_RATE * duration);
        float[] data = new float[samples];
        System.Random rand = new System.Random();
        float roarFilter = 0f, laserPhase = 0f, subPhase = 0f;

        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / SAMPLE_RATE;

            // Roaring saturated plasma body
            float rawNoise = (float)(rand.NextDouble() * 2.0 - 1.0);
            roarFilter = Mathf.Lerp(roarFilter, rawNoise, 0.28f);
            float saturatedRoar = Mathf.Clamp(Mathf.Sin(roarFilter * 2.5f), -1f, 1f);

            // 880Hz + 1760Hz laser harmonics
            laserPhase += (2f * Mathf.PI * 880f) / SAMPLE_RATE;
            float laser = (Mathf.Sin(laserPhase) + 0.35f * Mathf.Sin(laserPhase * 2f));

            // 60Hz heavy reactor core rumble
            subPhase += (2f * Mathf.PI * 60f) / SAMPLE_RATE;
            float sub = Mathf.Sin(subPhase) * 0.5f;

            float env = 1f;
            if (t < 0.02f) env = t / 0.02f;
            else if (t > duration - 0.15f) env = (duration - t) / 0.15f;

            data[i] = Mathf.Clamp((saturatedRoar * 0.5f + laser * 0.35f + sub * 0.35f) * env, -1f, 1f);
        }

        AudioClip clip = AudioClip.Create("Synth_UnibeamSustain", samples, 1, SAMPLE_RATE, false);
        clip.SetData(data, 0);
        return clip;
    }

    // Phase 4: Hydraulic cooling release & steam hiss
    private AudioClip SynthesizeSteamVent(float duration)
    {
        int samples = Mathf.CeilToInt(SAMPLE_RATE * duration);
        float[] data = new float[samples];
        System.Random rand = new System.Random();
        float lp = 0f;

        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / SAMPLE_RATE;
            float progress = t / duration;

            // Dynamic filter sweep: 5500Hz -> 600Hz
            float alpha = Mathf.Lerp(0.35f, 0.03f, progress);
            float noise = (float)(rand.NextDouble() * 2.0 - 1.0);
            lp += alpha * (noise - lp);

            float env = Mathf.Exp(-progress * 3.2f);
            data[i] = lp * env * 0.65f;
        }

        AudioClip clip = AudioClip.Create("Synth_SteamVent", samples, 1, SAMPLE_RATE, false);
        clip.SetData(data, 0);
        return clip;
    }

    #endregion

    #region Public Playback Controls

    public void PlayServoGlide()
    {
        if (armAudioSource != null && servoGlideClip != null)
        {
            armAudioSource.pitch = Random.Range(0.95f, 1.05f);
            armAudioSource.PlayOneShot(servoGlideClip, 0.85f);
        }
    }

    public void PlayChargeWhine()
    {
        if (chestAudioSource != null && chargeWhineClip != null)
        {
            chestAudioSource.pitch = 1.0f;
            chestAudioSource.PlayOneShot(chargeWhineClip, 1.0f);
        }
    }

    public void PlayRepulsorBlast()
    {
        if (armAudioSource != null && repulsorBlastClip != null)
        {
            armAudioSource.pitch = Random.Range(0.96f, 1.04f);
            armAudioSource.PlayOneShot(repulsorBlastClip, 1.0f);
        }
        if (chestAudioSource != null && repulsorBlastClip != null)
        {
            chestAudioSource.PlayOneShot(repulsorBlastClip, 0.5f);
        }
    }

    public void StartUnibeamLoop()
    {
        if (chestAudioSource != null && unibeamSustainClip != null)
        {
            chestAudioSource.clip = unibeamSustainClip;
            chestAudioSource.loop = true;
            chestAudioSource.pitch = 1.0f;
            chestAudioSource.Play();
        }
    }

    public void StopUnibeamLoop()
    {
        if (chestAudioSource != null && chestAudioSource.isPlaying && chestAudioSource.clip == unibeamSustainClip)
        {
            chestAudioSource.Stop();
            chestAudioSource.loop = false;
        }
    }

    public void PlaySteamVent()
    {
        if (chestAudioSource != null && steamVentClip != null)
        {
            chestAudioSource.pitch = Random.Range(0.92f, 1.05f);
            chestAudioSource.PlayOneShot(steamVentClip, 0.75f);
        }
    }

    public void StopAllAudio()
    {
        if (chestAudioSource != null)
        {
            chestAudioSource.Stop();
            chestAudioSource.loop = false;
        }
        if (armAudioSource != null)
        {
            armAudioSource.Stop();
            armAudioSource.loop = false;
        }
    }

    #endregion
}
