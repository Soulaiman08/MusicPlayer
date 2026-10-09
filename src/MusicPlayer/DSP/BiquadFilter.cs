namespace MusicPlayer.DSP;

public class BiquadCoefficients
{
    public float A0 = 1f, A1 = 0f, A2 = 0f, B1 = 0f, B2 = 0f;
}

public class BiquadFilter
{
    private volatile BiquadCoefficients _coeffs = new();
    private float _x1, _x2, _y1, _y2;
    
    public void SetFlat()
    {
        _coeffs = new BiquadCoefficients { A0 = 1f, A1 = 0f, A2 = 0f, B1 = 0f, B2 = 0f };
    }

    public void SetLowShelf(float sampleRate, float frequency, float gain, float slope = 0.5f)
    {
        float A = MathF.Pow(10f, gain / 40f);
        float w0 = 2f * MathF.PI * frequency / sampleRate;
        float cosw0 = MathF.Cos(w0);
        float sinw0 = MathF.Sin(w0);
        float alpha = sinw0 / 2f * MathF.Sqrt((A + 1f / A) * (1f / slope - 1f) + 2f);
        
        float b0 = A * ((A + 1f) - (A - 1f) * cosw0 + 2f * MathF.Sqrt(A) * alpha);
        float b1 = 2f * A * ((A - 1f) - (A + 1f) * cosw0);
        float b2 = A * ((A + 1f) - (A - 1f) * cosw0 - 2f * MathF.Sqrt(A) * alpha);
        float a0 = (A + 1f) + (A - 1f) * cosw0 + 2f * MathF.Sqrt(A) * alpha;
        float a1 = -2f * ((A - 1f) + (A + 1f) * cosw0);
        float a2 = (A + 1f) + (A - 1f) * cosw0 - 2f * MathF.Sqrt(A) * alpha;
        
        _coeffs = new BiquadCoefficients
        {
            A0 = b0 / a0, A1 = b1 / a0, A2 = b2 / a0,
            B1 = a1 / a0, B2 = a2 / a0
        };
    }
    
    public void SetPeaking(float sampleRate, float frequency, float gain, float q = 1.0f)
    {
        float A = MathF.Pow(10f, gain / 40f);
        float w0 = 2f * MathF.PI * frequency / sampleRate;
        float cosw0 = MathF.Cos(w0);
        float sinw0 = MathF.Sin(w0);
        float alpha = sinw0 / (2f * q);
        
        float b0 = 1f + alpha * A;
        float b1 = -2f * cosw0;
        float b2 = 1f - alpha * A;
        float a0 = 1f + alpha / A;
        float a1 = -2f * cosw0;
        float a2 = 1f - alpha / A;
        
        _coeffs = new BiquadCoefficients
        {
            A0 = b0 / a0, A1 = b1 / a0, A2 = b2 / a0,
            B1 = a1 / a0, B2 = a2 / a0
        };
    }

    public void SetLowPass(float sampleRate, float frequency, float q = 0.707f)
    {
        float w0 = 2f * MathF.PI * frequency / sampleRate;
        float cosw0 = MathF.Cos(w0);
        float sinw0 = MathF.Sin(w0);
        float alpha = sinw0 / (2f * q);

        float b0 = (1f - cosw0) / 2f;
        float b1 = 1f - cosw0;
        float b2 = (1f - cosw0) / 2f;
        float a0 = 1f + alpha;
        float a1 = -2f * cosw0;
        float a2 = 1f - alpha;

        _coeffs = new BiquadCoefficients
        {
            A0 = b0 / a0, A1 = b1 / a0, A2 = b2 / a0,
            B1 = a1 / a0, B2 = a2 / a0
        };
    }
    
    public void SetHighPass(float sampleRate, float frequency, float q = 0.707f)
    {
        float w0 = 2f * MathF.PI * frequency / sampleRate;
        float cosw0 = MathF.Cos(w0);
        float sinw0 = MathF.Sin(w0);
        float alpha = sinw0 / (2f * q);

        float b0 = (1f + cosw0) / 2f;
        float b1 = -(1f + cosw0);
        float b2 = (1f + cosw0) / 2f;
        float a0 = 1f + alpha;
        float a1 = -2f * cosw0;
        float a2 = 1f - alpha;

        _coeffs = new BiquadCoefficients
        {
            A0 = b0 / a0, A1 = b1 / a0, A2 = b2 / a0,
            B1 = a1 / a0, B2 = a2 / a0
        };
    }

    public void SetAllPass(float sampleRate, float frequency, float q = 0.707f)
    {
        float w0 = 2f * MathF.PI * frequency / sampleRate;
        float cosw0 = MathF.Cos(w0);
        float sinw0 = MathF.Sin(w0);
        float alpha = sinw0 / (2f * q);

        float b0 = 1f - alpha;
        float b1 = -2f * cosw0;
        float b2 = 1f + alpha;
        float a0 = 1f + alpha;
        float a1 = -2f * cosw0;
        float a2 = 1f - alpha;

        _coeffs = new BiquadCoefficients
        {
            A0 = b0 / a0, A1 = b1 / a0, A2 = b2 / a0,
            B1 = a1 / a0, B2 = a2 / a0
        };
    }

    public void SetHighShelf(float sampleRate, float frequency, float gain, float slope = 0.5f)
    {
        float A = MathF.Pow(10f, gain / 40f);
        float w0 = 2f * MathF.PI * frequency / sampleRate;
        float cosw0 = MathF.Cos(w0);
        float sinw0 = MathF.Sin(w0);
        float alpha = sinw0 / 2f * MathF.Sqrt((A + 1f / A) * (1f / slope - 1f) + 2f);
        
        float b0 = A * ((A + 1f) + (A - 1f) * cosw0 + 2f * MathF.Sqrt(A) * alpha);
        float b1 = -2f * A * ((A - 1f) + (A + 1f) * cosw0);
        float b2 = A * ((A + 1f) + (A - 1f) * cosw0 - 2f * MathF.Sqrt(A) * alpha);
        float a0 = (A + 1f) - (A - 1f) * cosw0 + 2f * MathF.Sqrt(A) * alpha;
        float a1 = 2f * ((A - 1f) - (A + 1f) * cosw0);
        float a2 = (A + 1f) - (A - 1f) * cosw0 - 2f * MathF.Sqrt(A) * alpha;
        
        _coeffs = new BiquadCoefficients
        {
            A0 = b0 / a0, A1 = b1 / a0, A2 = b2 / a0,
            B1 = a1 / a0, B2 = a2 / a0
        };
    }
    
    public float Process(float input)
    {
        var c = _coeffs;
        float output = c.A0 * input + c.A1 * _x1 + c.A2 * _x2 - c.B1 * _y1 - c.B2 * _y2;
        _x2 = _x1; _x1 = input;
        _y2 = _y1; _y1 = output;
        return output;
    }
    
    public void Reset()
    {
        _x1 = _x2 = _y1 = _y2 = 0f;
    }
}
