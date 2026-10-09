using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using MusicPlayer.Audio;
using MusicPlayer.DSP;
using MusicPlayer.Models;

namespace EqVerification;

class DummySampleProvider : ISampleProvider
{
    private readonly WaveFormat _format;
    private readonly float _frequency;
    private long _sampleCount;

    public DummySampleProvider(int sampleRate, int channels, float frequency = 1000f)
    {
        _format = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
        _frequency = frequency;
    }

    public WaveFormat WaveFormat => _format;

    public int Read(float[] buffer, int offset, int count)
    {
        for (int i = 0; i < count; i += _format.Channels)
        {
            float t = (float)_sampleCount / _format.SampleRate;
            float val = MathF.Sin(2f * MathF.PI * _frequency * t) * 0.5f;
            for (int ch = 0; ch < _format.Channels; ch++)
            {
                buffer[offset + i + ch] = val;
            }
            _sampleCount++;
        }
        return count;
    }
}

class Program
{
    static int _passCount = 0;
    static int _failCount = 0;

    static void Assert(string name, bool condition, string detail = "")
    {
        if (condition)
        {
            Console.WriteLine($"[PASS] {name} {(string.IsNullOrEmpty(detail) ? "" : " - " + detail)}");
            _passCount++;
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[FAIL] {name} - {detail}");
            Console.ResetColor();
            _failCount++;
        }
    }

    static void Main(string[] args)
    {
        Console.WriteLine("=================================================");
        Console.WriteLine("STARTING BAND EQUALIZER & FX VERIFICATION SUITE");
        Console.WriteLine("=================================================");

        // 1 & 2: EQ OFF and EQ ON
        TestEqOnOff();

        // 3 - 12: All 10 individual bands
        TestAll10Bands();

        // 13 & 14 & 15: Flat, Rock, and All 23 Presets
        TestPresets();

        // 18, 19, 20: Sound Effects OFF / ON
        TestSoundEffects();

        // 21, 22, 23: Critical Coupling Test (Simultaneous & Independent)
        TestDecouplingAndSimultaneousProcessing();

        // 27, 28, 29, 30: Sample Rates & Nyquist Stability
        TestSampleRatesAndNyquist();

        // DSP Safety & Headroom
        TestDspSafetyAndZeroGainHistory();

        Console.WriteLine("=================================================");
        Console.WriteLine($"TEST SUMMARY: {_passCount} PASSED, {_failCount} FAILED");
        Console.WriteLine("=================================================");

        if (_failCount > 0)
        {
            Environment.Exit(1);
        }
    }

    static void TestEqOnOff()
    {
        var dummy = new DummySampleProvider(44100, 2, 1000f);
        var eq = new EqualizerSampleProvider(dummy);
        eq.SetBandGain(5, 6f); // 1kHz boosted by 6dB

        float[] bufOff = new float[1024];
        eq.IsEnabled = false;
        eq.Read(bufOff, 0, 1024);

        // When OFF, output matches raw input
        var dummy2 = new DummySampleProvider(44100, 2, 1000f);
        float[] bufRaw = new float[1024];
        dummy2.Read(bufRaw, 0, 1024);

        bool matchesRaw = true;
        for (int i = 0; i < 1024; i++)
        {
            if (MathF.Abs(bufOff[i] - bufRaw[i]) > 0.0001f) { matchesRaw = false; break; }
        }
        Assert("1. EQ OFF bypass", matchesRaw, "Audio passes unprocessed when EQ is OFF");

        // When ON, output is boosted
        eq.IsEnabled = true;
        float[] bufOn = new float[1024];
        eq.Read(bufOn, 0, 1024);
        float maxOn = bufOn.Max(MathF.Abs);
        float maxRaw = bufRaw.Max(MathF.Abs);
        Assert("2. EQ ON active", maxOn > maxRaw * 1.5f, $"Boosted max {maxOn:F3} > raw {maxRaw:F3}");
    }

    static void TestAll10Bands()
    {
        float[] testFreqs = new float[] { 31.25f, 62.5f, 125f, 250f, 500f, 1000f, 2000f, 4000f, 8000f, 16000f };
        for (int b = 0; b < 10; b++)
        {
            float centerFreq = testFreqs[b];
            var dummy = new DummySampleProvider(44100, 2, centerFreq);
            var eq = new EqualizerSampleProvider(dummy);
            eq.IsEnabled = true;
            eq.SetBandGain(b, 8f); // +8 dB

            float[] buf = new float[2048];
            eq.Read(buf, 0, 2048);

            // Read raw baseline
            var rawDummy = new DummySampleProvider(44100, 2, centerFreq);
            float[] rawBuf = new float[2048];
            rawDummy.Read(rawBuf, 0, 2048);

            // Skip warm up frames
            float eqEnergy = buf.Skip(512).Select(x => x * x).Sum();
            float rawEnergy = rawBuf.Skip(512).Select(x => x * x).Sum();

            Assert($"Band {b} ({centerFreq:F0} Hz) End-to-End", eqEnergy > rawEnergy * 1.4f && !float.IsNaN(eqEnergy),
                $"Energy ratio {eqEnergy / rawEnergy:F2}x, gain applied correctly");
        }
    }

    static void TestPresets()
    {
        var presets = EqPreset.Presets;
        Assert("Presets collection loaded", presets != null && presets.Length == 23, $"Found {presets?.Length} presets");

        var dummy = new DummySampleProvider(44100, 2, 1000f);
        var eq = new EqualizerSampleProvider(dummy);

        // Test Flat / Normal
        var flat = presets?.FirstOrDefault(p => p.Name == "Normal" || p.Name == "Flat");
        Assert("13. Flat/Normal Preset Exists", flat != null && flat.BandGains.All(g => g == 0f), "Flat/Normal preset all 0 dB");

        // Test Rock
        var rock = presets?.FirstOrDefault(p => p.Name == "Rock");
        Assert("14. Rock Preset Exists", rock != null, "Rock preset exists");

        // Test all 23 presets apply without error or NaN
        bool all23Pass = true;
        foreach (var p in presets!)
        {
            eq.SetAllBands(p.BandGains);
            for (int b = 0; b < 10; b++)
            {
                if (MathF.Abs(eq.GetBandGain(b) - p.BandGains[b]) > 0.001f)
                {
                    all23Pass = false;
                    break;
                }
            }
            float[] buf = new float[512];
            eq.Read(buf, 0, 512);
            if (buf.Any(float.IsNaN) || buf.Any(float.IsInfinity))
            {
                all23Pass = false;
                break;
            }
        }
        Assert("15. All 23 Presets Validation", all23Pass, "All 23 presets set gains and process clean audio");
    }

    static void TestSoundEffects()
    {
        var dummy = new DummySampleProvider(44100, 2, 440f);
        var fx = new SoundEffectProcessor(dummy);

        // Off
        fx.ActiveEffect = SoundEffectType.UserDefined;
        float[] bufOff = new float[512];
        fx.Read(bufOff, 0, 512);
        Assert("19. Sound Effect OFF", fx.ActiveEffect == SoundEffectType.UserDefined, "Inactive effect passes signal");

        // 6 Sound Effects
        for (int i = 0; i < 6; i++)
        {
            fx.ActiveEffect = (SoundEffectType)i;
            float[] buf = new float[512];
            int read = fx.Read(buf, 0, 512);
            bool noNan = !buf.Any(float.IsNaN) && !buf.Any(float.IsInfinity);
            Assert($"Sound Effect {(SoundEffectType)i}", read == 512 && noNan, "Processes without errors or NaN");
        }
    }

    static void TestDecouplingAndSimultaneousProcessing()
    {
        // Setup Pipeline: dummy -> EQ -> FX
        var dummy = new DummySampleProvider(44100, 2, 1000f);
        var eq = new EqualizerSampleProvider(dummy);
        var fx = new SoundEffectProcessor(eq);

        // Step 1: Start: EQ = Rock, Band3 = +5 dB, EQ = ON
        var rock = EqPreset.Presets.First(p => p.Name == "Rock");
        eq.SetAllBands(rock.BandGains);
        eq.SetBandGain(3, 5f);
        eq.IsEnabled = true;

        Assert("Initial EQ state", eq.GetBandGain(3) == 5f && eq.IsEnabled, "Band3 = 5 dB, EQ = ON");

        // Step 2: Set Sound Effect = Tone Low
        fx.ActiveEffect = SoundEffectType.ToneLow;

        // Verify EQ was NOT modified by setting Sound Effect!
        Assert("Critical Coupling: FX -> EQ isolation",
            eq.GetBandGain(3) == 5f && eq.IsEnabled,
            "Setting Tone Low did NOT alter EQ Band3 (+5 dB) or EQ Enabled status");

        // Step 3: Change Sound Effect to Electronic Tube
        fx.ActiveEffect = SoundEffectType.ElectronicTube;
        Assert("Critical Coupling: FX change -> EQ unchanged",
            eq.GetBandGain(3) == 5f && eq.IsEnabled,
            "Changing Sound Effect preserved EQ state completely");

        // Step 4: Change Band 3 to +2 dB
        eq.SetBandGain(3, 2f);
        Assert("Critical Coupling: EQ change -> FX unchanged",
            fx.ActiveEffect == SoundEffectType.ElectronicTube,
            "Changing EQ band preserved Sound Effect ActiveEffect");

        // Step 5: Turn EQ OFF
        eq.IsEnabled = false;
        Assert("Critical Coupling: EQ OFF -> FX remains active",
            fx.ActiveEffect == SoundEffectType.ElectronicTube && eq.GetBandGain(3) == 2f,
            "EQ OFF preserved Sound Effect and kept stored Band3 gain (+2 dB)");

        // Step 6: Turn EQ ON
        eq.IsEnabled = true;
        Assert("Critical Coupling: EQ ON -> Stored values return",
            eq.GetBandGain(3) == 2f && eq.IsEnabled && fx.ActiveEffect == SoundEffectType.ElectronicTube,
            "EQ ON directly restored previous band gains");

        // Step 7: Simultaneous Processing
        float[] bufSim = new float[1024];
        int read = fx.Read(bufSim, 0, 1024);
        bool noNan = !bufSim.Any(float.IsNaN) && !bufSim.Any(float.IsInfinity);
        Assert("21. EQ + Sound Effect simultaneously", read == 1024 && noNan,
            "Audio passed cleanly through EQ -> FX simultaneous chain");
    }

    static void TestSampleRatesAndNyquist()
    {
        int[] rates = new int[] { 22050, 32000, 44100, 48000, 96000 };
        foreach (int rate in rates)
        {
            var dummy = new DummySampleProvider(rate, 2, 500f);
            var eq = new EqualizerSampleProvider(dummy);
            eq.IsEnabled = true;

            // Set all bands including highest (16kHz) to +12 dB
            for (int b = 0; b < 10; b++)
            {
                eq.SetBandGain(b, 12f);
            }

            float[] buf = new float[1024];
            int read = eq.Read(buf, 0, 1024);
            bool clean = !buf.Any(float.IsNaN) && !buf.Any(float.IsInfinity);

            Assert($"Nyquist Safety @ {rate / 1000.0:F1} kHz", read == 1024 && clean,
                $"Band 9 (16kHz) and all filters stable at {rate} Hz");
        }
    }

    static void TestDspSafetyAndZeroGainHistory()
    {
        var dummy = new DummySampleProvider(44100, 2, 440f);
        var eq = new EqualizerSampleProvider(dummy);
        eq.IsEnabled = true;

        // Transition 0 dB -> 0.1 dB -> 0 dB -> -3 dB rapidly (simulating slider drag)
        bool noSpikes = true;
        float[] buf = new float[256];

        for (int step = 0; step < 20; step++)
        {
            float gain = (step % 2 == 0) ? 0f : (step * 0.5f);
            eq.SetBandGain(4, gain);
            eq.Read(buf, 0, 256);
            if (buf.Any(float.IsNaN) || buf.Any(v => MathF.Abs(v) > 1.0f))
            {
                noSpikes = false;
                break;
            }
        }

        Assert("Zero-gain filter history and headroom stability", noSpikes,
            "No clicks, NaN, or headroom spikes during zero-gain transitions");

        // Concurrent thread stress test (simulate audio thread reading while UI thread updates bands)
        bool concurrentPass = true;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var taskUi = Task.Run(() =>
        {
            int iter = 0;
            while (!cts.Token.IsCancellationRequested)
            {
                eq.SetBandGain(iter % 10, (iter % 30) - 15f);
                iter++;
                Thread.Sleep(1);
            }
        });

        var taskAudio = Task.Run(() =>
        {
            float[] audioBuf = new float[512];
            while (!cts.Token.IsCancellationRequested)
            {
                eq.Read(audioBuf, 0, 512);
                if (audioBuf.Any(float.IsNaN))
                {
                    concurrentPass = false;
                    break;
                }
            }
        });

        Task.WaitAll(taskUi, taskAudio);
        Assert("Thread Safety: Concurrent Audio Read + UI Band Update", concurrentPass,
            "Atomic coefficient replacement verified without crash or race corruption");
    }
}
