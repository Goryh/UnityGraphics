using System;
using UnityEngine;

namespace UnityEditor.Rendering.Universal
{
    /// <summary>
    /// Resamples a .cube onto the internal LUT space: a lattice of Unity LogC coordinates, storing Unity LogC
    /// encoded colors. Everything is computed in double precision, the result is only rounded when it is stored
    /// in the half float texture.
    /// </summary>
    internal static class CubeLutBaker
    {
        internal const int k_MinSize = 17;
        internal const int k_MaxSize = 65;

        // Floor of the linear values going through a log2, far below anything visible.
        const double k_LogFloor = 1.0 / (1 << 24);

        // Steps used to make the extrapolated curve monotonic, between the top of the range and the color.
        const int k_MonotonicSteps = 32;

        internal struct Settings
        {
            public CubeLutEncoding inputEncoding;
            public CubeLutEncoding outputEncoding;
            public int size;
            public CubeLutExtrapolation extrapolation;
            public int polynomialOrder;
            public int keyPointCount;
            public float fitRangeStops;
        }

        struct Double3
        {
            public double x, y, z;

            public Double3(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }

            public double this[int i]
            {
                get => i == 0 ? x : i == 1 ? y : z;
                set { if (i == 0) x = value; else if (i == 1) y = value; else z = value; }
            }

            public double Max() => Math.Max(x, Math.Max(y, z));

            public static Double3 operator *(Double3 a, double b) => new Double3(a.x * b, a.y * b, a.z * b);
        }

        public static Color[] Bake(CubeLutFile cube, in Settings settings)
        {
            var baker = new Baker(cube, settings);

            int n = settings.size;
            var colors = new Color[n * n * n];

            for (int b = 0; b < n; b++)
            for (int g = 0; g < n; g++)
            for (int r = 0; r < n; r++)
            {
                // Same lattice as the run time sampling: entry i sits at LogC value i / (n - 1).
                var linear = new Double3(
                    Math.Max(0.0, Encodings.Decode(CubeLutEncoding.UnityLogC, r / (n - 1.0))),
                    Math.Max(0.0, Encodings.Decode(CubeLutEncoding.UnityLogC, g / (n - 1.0))),
                    Math.Max(0.0, Encodings.Decode(CubeLutEncoding.UnityLogC, b / (n - 1.0))));

                var output = baker.Evaluate(linear);

                colors[r + g * n + b * n * n] = new Color(
                    (float)Encodings.Encode(CubeLutEncoding.UnityLogC, output.x),
                    (float)Encodings.Encode(CubeLutEncoding.UnityLogC, output.y),
                    (float)Encodings.Encode(CubeLutEncoding.UnityLogC, output.z),
                    1f);
            }

            return colors;
        }

        sealed class Baker
        {
            readonly CubeLutFile m_Cube;
            readonly Settings m_Settings;

            // Top of the input range, in linear. The ray extrapolation needs a single value for the three
            // channels, so it is the smallest of them.
            readonly double m_RangeMax;

            // Offsets of the key points below the top of the range, in stops (log2 of the linear ratio). The
            // first one is 0: the top of the range, the value the fit is constrained to go through.
            readonly double[] m_KeyPointStops;

            // Scratch storage of the fit.
            readonly double[] m_KeyPointValues;
            readonly double[] m_Coefficients;
            readonly double[,] m_Normal;

            public Baker(CubeLutFile cube, in Settings settings)
            {
                m_Cube = cube;
                m_Settings = settings;

                m_RangeMax = double.MaxValue;
                for (int c = 0; c < 3; c++)
                    m_RangeMax = Math.Min(m_RangeMax, Encodings.Decode(settings.inputEncoding, cube.domainMax[c]));

                int k = settings.keyPointCount;
                m_KeyPointStops = new double[k];
                for (int i = 0; i < k; i++)
                    m_KeyPointStops[i] = -settings.fitRangeStops * i / (k - 1.0);

                m_KeyPointValues = new double[k];
                m_Coefficients = new double[settings.polynomialOrder];
                m_Normal = new double[settings.polynomialOrder, settings.polynomialOrder + 1];
            }

            // Linear in, linear out.
            public Double3 Evaluate(in Double3 linear)
            {
                double max = linear.Max();
                if (max <= m_RangeMax)
                    return Sample(linear);

                // Past the top of the range: follow the ray from black through the color. Its direction is
                // normalized to the top of the range, so the key points are colors of the same chromaticity.
                var direction = linear * (m_RangeMax / max);
                double stops = Math.Log(max / m_RangeMax, 2.0);

                var keyPoints = new Double3[m_KeyPointStops.Length];
                for (int i = 0; i < keyPoints.Length; i++)
                    keyPoints[i] = Sample(direction * Math.Pow(2.0, m_KeyPointStops[i]));

                var top = keyPoints[0];

                if (m_Settings.extrapolation == CubeLutExtrapolation.PreserveHue)
                {
                    for (int i = 0; i < keyPoints.Length; i++)
                        m_KeyPointValues[i] = Math.Log(Math.Max(keyPoints[i].Max(), k_LogFloor), 2.0);

                    double scale = Math.Pow(2.0, Extrapolate(stops) - m_KeyPointValues[0]);
                    return top * scale;
                }

                var result = new Double3();
                for (int c = 0; c < 3; c++)
                {
                    for (int i = 0; i < keyPoints.Length; i++)
                        m_KeyPointValues[i] = Math.Log(Math.Max(keyPoints[i][c], k_LogFloor), 2.0);

                    // A channel at the floor at the top of the range stays there: there's nothing to follow.
                    result[c] = top[c] <= k_LogFloor ? top[c] : Math.Pow(2.0, Extrapolate(stops));
                }

                return result;
            }

            // Fits y(x) = y0 + a1 x + ... + an x^n on (m_KeyPointStops, m_KeyPointValues), with y0 the value at
            // the top of the range so the extrapolation is continuous with the LUT, and evaluates it at 'x' > 0.
            double Extrapolate(double x)
            {
                int order = m_Settings.polynomialOrder;
                double y0 = m_KeyPointValues[0];

                // Normal equations of the least squares fit of the coefficients a1..an.
                Array.Clear(m_Normal, 0, m_Normal.Length);
                for (int i = 1; i < m_KeyPointStops.Length; i++)
                {
                    double xi = m_KeyPointStops[i];
                    double dy = m_KeyPointValues[i] - y0;

                    for (int row = 0; row < order; row++)
                    {
                        double xr = Math.Pow(xi, row + 1);
                        for (int col = 0; col < order; col++)
                            m_Normal[row, col] += xr * Math.Pow(xi, col + 1);
                        m_Normal[row, order] += xr * dy;
                    }
                }

                Solve(m_Normal, m_Coefficients);

                // A higher order polynomial can turn back down past its fit range, which would fold the
                // highlights over. Keep the running maximum between the top of the range and 'x' instead.
                double y = y0;
                for (int s = 1; s <= k_MonotonicSteps; s++)
                    y = Math.Max(y, EvaluatePolynomial(y0, x * s / k_MonotonicSteps));

                // It can also grow much faster than the trend of the LUT. Past the range, the output is never
                // allowed to grow faster than the input or than the slope of the fit at the top of the range.
                double maxSlope = Math.Max(1.0, m_Coefficients[0]);
                return Math.Min(y, y0 + maxSlope * x);
            }

            double EvaluatePolynomial(double y0, double x)
            {
                double y = y0;
                double xn = x;
                for (int i = 0; i < m_Coefficients.Length; i++, xn *= x)
                    y += m_Coefficients[i] * xn;
                return y;
            }

            // Linear in, linear out, within the input range of the .cube.
            Double3 Sample(in Double3 linear)
            {
                var cube = m_Cube;
                var encoded = new Double3();
                for (int c = 0; c < 3; c++)
                {
                    double v = Encodings.Encode(m_Settings.inputEncoding, Math.Max(0.0, linear[c]));
                    encoded[c] = (v - cube.domainMin[c]) / (cube.domainMax[c] - cube.domainMin[c]);
                }

                var output = SampleTrilinear(encoded);
                for (int c = 0; c < 3; c++)
                    output[c] = Encodings.Decode(m_Settings.outputEncoding, output[c]);

                return output;
            }

            Double3 SampleTrilinear(in Double3 uvw)
            {
                int n = m_Cube.size;
                var i0 = new int[3];
                var f = new double[3];
                for (int c = 0; c < 3; c++)
                {
                    double p = Math.Min(Math.Max(uvw[c], 0.0), 1.0) * (n - 1);
                    i0[c] = Math.Min((int)p, n - 2);
                    f[c] = p - i0[c];
                }

                var result = new Double3();
                for (int corner = 0; corner < 8; corner++)
                {
                    int dr = corner & 1, dg = (corner >> 1) & 1, db = (corner >> 2) & 1;
                    double w = (dr != 0 ? f[0] : 1.0 - f[0])
                             * (dg != 0 ? f[1] : 1.0 - f[1])
                             * (db != 0 ? f[2] : 1.0 - f[2]);
                    if (w == 0.0)
                        continue;

                    var v = m_Cube.data[(i0[0] + dr) + (i0[1] + dg) * n + (i0[2] + db) * n * n];
                    result.x += v.x * w;
                    result.y += v.y * w;
                    result.z += v.z * w;
                }

                return result;
            }

            // Gaussian elimination with partial pivoting of an n x (n + 1) augmented matrix. A singular system
            // (all the key points equal) leaves the coefficients at zero: a flat extrapolation.
            static void Solve(double[,] m, double[] x)
            {
                int n = x.Length;
                for (int col = 0; col < n; col++)
                {
                    int pivot = col;
                    for (int row = col + 1; row < n; row++)
                    {
                        if (Math.Abs(m[row, col]) > Math.Abs(m[pivot, col]))
                            pivot = row;
                    }

                    if (Math.Abs(m[pivot, col]) < 1e-12)
                    {
                        Array.Clear(x, 0, n);
                        return;
                    }

                    if (pivot != col)
                    {
                        for (int k = col; k <= n; k++)
                            (m[col, k], m[pivot, k]) = (m[pivot, k], m[col, k]);
                    }

                    for (int row = col + 1; row < n; row++)
                    {
                        double factor = m[row, col] / m[col, col];
                        for (int k = col; k <= n; k++)
                            m[row, k] -= factor * m[col, k];
                    }
                }

                for (int row = n - 1; row >= 0; row--)
                {
                    double sum = m[row, n];
                    for (int k = row + 1; k < n; k++)
                        sum -= m[row, k] * x[k];
                    x[row] = sum / m[row, row];
                }
            }
        }

        internal static class Encodings
        {
            // Unity LogC, the fast variant without the linear segment, as LinearToLogC / LogCToLinear in Color.hlsl.
            const double k_UnityLogCA = 5.555556, k_UnityLogCB = 0.047996, k_UnityLogCC = 0.244161, k_UnityLogCD = 0.386036;

            // ARRI LogC3, EI 800.
            const double k_LogC3Cut = 0.010591, k_LogC3A = 5.555556, k_LogC3B = 0.052272, k_LogC3C = 0.247190,
                k_LogC3D = 0.385537, k_LogC3E = 5.367655, k_LogC3F = 0.092809;

            public static double Encode(CubeLutEncoding encoding, double x)
            {
                switch (encoding)
                {
                    case CubeLutEncoding.sRGB:
                        x = Math.Max(x, 0.0);
                        return x <= 0.0031308 ? x * 12.92 : 1.055 * Math.Pow(x, 1.0 / 2.4) - 0.055;

                    case CubeLutEncoding.Gamma24:
                        return Math.Pow(Math.Max(x, 0.0), 1.0 / 2.4);

                    case CubeLutEncoding.UnityLogC:
                        return k_UnityLogCC * Math.Log10(Math.Max(k_UnityLogCA * x + k_UnityLogCB, 1e-30)) + k_UnityLogCD;

                    case CubeLutEncoding.ArriLogC3:
                        return x > k_LogC3Cut ? k_LogC3C * Math.Log10(k_LogC3A * x + k_LogC3B) + k_LogC3D : k_LogC3E * x + k_LogC3F;

                    case CubeLutEncoding.ACEScct:
                        return x <= 0.0078125 ? 10.5402377416545 * x + 0.0729055341958355 : (Math.Log(x, 2.0) + 9.72) / 17.52;

                    case CubeLutEncoding.SLog3:
                        return x >= 0.01125
                            ? (420.0 + Math.Log10((x + 0.01) / (0.18 + 0.01)) * 261.5) / 1023.0
                            : (x * (171.2102946929 - 95.0) / 0.01125 + 95.0) / 1023.0;

                    default:
                        throw new ArgumentOutOfRangeException(nameof(encoding));
                }
            }

            public static double Decode(CubeLutEncoding encoding, double v)
            {
                switch (encoding)
                {
                    case CubeLutEncoding.sRGB:
                        v = Math.Max(v, 0.0);
                        return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);

                    case CubeLutEncoding.Gamma24:
                        return Math.Pow(Math.Max(v, 0.0), 2.4);

                    case CubeLutEncoding.UnityLogC:
                        return (Math.Pow(10.0, (v - k_UnityLogCD) / k_UnityLogCC) - k_UnityLogCB) / k_UnityLogCA;

                    case CubeLutEncoding.ArriLogC3:
                        return v > k_LogC3E * k_LogC3Cut + k_LogC3F
                            ? (Math.Pow(10.0, (v - k_LogC3D) / k_LogC3C) - k_LogC3B) / k_LogC3A
                            : (v - k_LogC3F) / k_LogC3E;

                    case CubeLutEncoding.ACEScct:
                        return v <= 0.155251141552511
                            ? (v - 0.0729055341958355) / 10.5402377416545
                            : Math.Pow(2.0, v * 17.52 - 9.72);

                    case CubeLutEncoding.SLog3:
                        return v >= 171.2102946929 / 1023.0
                            ? Math.Pow(10.0, (v * 1023.0 - 420.0) / 261.5) * (0.18 + 0.01) - 0.01
                            : (v * 1023.0 - 95.0) * 0.01125 / (171.2102946929 - 95.0);

                    default:
                        throw new ArgumentOutOfRangeException(nameof(encoding));
                }
            }
        }
    }
}
