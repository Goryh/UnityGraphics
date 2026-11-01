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

        // Inverse Tonemap highlight rebuild: steps used to find where the output of the .cube enters the rebuilt
        // band, then bisection iterations refining that point.
        const int k_RebuildSearchSteps = 32;
        const int k_RebuildBisections = 10;

        // Tone curve removal: largest gain applied to a color, so a .cube crushing its blacks to zero can't blow
        // up the colors around them.
        const double k_MaxToneCurveGain = 1 << 16;

        // Bisection iterations inverting the Neutral tonemapper, enough to reach double precision on its range.
        const int k_InverseNeutralIterations = 64;

        internal struct Settings
        {
            public CubeLutEncoding inputEncoding;
            public CubeLutEncoding outputEncoding;
            public CubeLutGamut inputGamut;
            public CubeLutGamut outputGamut;
            public int size;
            public CubeLutExtrapolation extrapolation;
            public int polynomialOrder;
            public int keyPointCount;
            public float fitRangeStops;
            public float tonemapKnee;
            public float highlightRebuildStart;
            public float toneCurveStrength;
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

            // Rec709 luminance.
            public double Luminance() => 0.2126 * x + 0.7152 * y + 0.0722 * z;

            public static Double3 operator *(Double3 a, double b) => new Double3(a.x * b, a.y * b, a.z * b);

            // Row major 3x3 matrix times this vector.
            public Double3 Transform(double[,] m) => new Double3(
                m[0, 0] * x + m[0, 1] * y + m[0, 2] * z,
                m[1, 0] * x + m[1, 1] * y + m[1, 2] * z,
                m[2, 0] * x + m[2, 1] * y + m[2, 2] * z);
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

                // The ray extrapolation and the input range work in the input gamut of the .cube. Every gamut
                // supported contains Rec709, so the converted color stays positive.
                var output = baker.Evaluate(linear.Transform(baker.inputFromRec709));

                if (settings.toneCurveStrength < 1f)
                    output = baker.RemoveToneCurve(linear, output);

                // Rec709 can't hold every color of a wider output gamut: those come out negative, clamp them
                // like the LUT builder does with its result.
                output = new Double3(Math.Max(0.0, output.x), Math.Max(0.0, output.y), Math.Max(0.0, output.z));

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

            public readonly double[,] inputFromRec709;
            readonly double[,] m_Rec709FromOutput;

            // Inverse Tonemap: knee of the shoulder, in linear, and the largest output it is allowed to expand. The
            // latter is the top of the internal LUT range once compressed, so the expanded output stays inside it.
            readonly double m_Knee;
            readonly double m_MaxCompressedOutput;

            // Inverse Tonemap: bottom of the rebuilt band at the top of the range, in compressed linear.
            readonly double m_RebuildStart;

            public Baker(CubeLutFile cube, in Settings settings)
            {
                m_Cube = cube;
                m_Settings = settings;

                inputFromRec709 = Gamuts.Conversion(CubeLutGamut.Rec709, settings.inputGamut);
                m_Rec709FromOutput = Gamuts.Conversion(settings.outputGamut, CubeLutGamut.Rec709);

                m_RangeMax = double.MaxValue;
                for (int c = 0; c < 3; c++)
                    m_RangeMax = Math.Min(m_RangeMax, Encodings.Decode(settings.inputEncoding, cube.domainMax[c]));

                int k = settings.keyPointCount;
                m_KeyPointStops = new double[k];
                for (int i = 0; i < k; i++)
                    m_KeyPointStops[i] = -settings.fitRangeStops * i / (k - 1.0);

                m_Knee = settings.tonemapKnee * m_RangeMax;
                m_MaxCompressedOutput = Tonemap(Encodings.Decode(CubeLutEncoding.UnityLogC, 1.0));
                m_RebuildStart = m_Knee + settings.highlightRebuildStart * (m_RangeMax - m_Knee);

                m_KeyPointValues = new double[k];
                m_Coefficients = new double[settings.polynomialOrder];
                m_Normal = new double[settings.polynomialOrder, settings.polynomialOrder + 1];
            }

            // Replaces the tone curve of the .cube in 'output', its result for the Rec709 color 'linear', with the
            // inverse of the URP Neutral tonemapper, which is applied after it.
            //
            // The tone curve G is the luminance the .cube produces for a gray input of luminance y. A gray has to
            // come out of the .cube at N^-1(G(y)), so that the Neutral tonemapper N brings it back to G(y): in SDR the
            // grays end up exactly where the .cube puts them, without being tonemapped twice. The color is scaled
            // by N^-1(G(y)) / G(y): every color gets the same gain as the gray of the same input luminance, keeping
            // the tint, hue and saturation changes of the .cube. The strength blends the gain in log space.
            //
            // A .cube clipping at 1 maps to the input Neutral brings to 1 (~4.04): the clipped highlights stay
            // clipped, as the .cube intends them in SDR.
            public Double3 RemoveToneCurve(in Double3 linear, in Double3 output)
            {
                double y = linear.Luminance();
                double toneCurve = Evaluate(new Double3(y, y, y).Transform(inputFromRec709)).Luminance();
                double target = Neutral.Inverse(toneCurve, Encodings.Decode(CubeLutEncoding.UnityLogC, 1.0));
                double gain = Math.Min(target / Math.Max(toneCurve, k_LogFloor), k_MaxToneCurveGain);

                return output * Math.Pow(gain, 1.0 - m_Settings.toneCurveStrength);
            }

            // Linear in the input gamut in, linear Rec709 out.
            public Double3 Evaluate(in Double3 linear)
            {
                if (m_Settings.extrapolation == CubeLutExtrapolation.InverseTonemap)
                    return EvaluateInverseTonemap(linear);

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

            // T^-1(LUT(T(x))). Both ways scale the color by the ratio of the tonemapped and the original brightest
            // channel, so the compression keeps the hue going into the .cube and the expansion keeps the hue that
            // comes out of it. The expansion is done in the output gamut, where the .cube produced the color.
            Double3 EvaluateInverseTonemap(in Double3 linear)
            {
                double max = linear.Max();
                var output = max > m_Knee ? SampleRebuilt(linear * (1.0 / max), Tonemap(max)) : SampleOutput(linear);

                // The inverse goes to infinity at the top of the range, where an SDR grade often clips: clamp to the
                // top of the internal range rather than expand past it.
                double outputMax = output.Max();
                if (outputMax > m_Knee)
                    output = output * (InverseTonemap(Math.Min(outputMax, m_MaxCompressedOutput)) / outputMax);

                return output.Transform(m_Rec709FromOutput);
            }

            // The .cube along the ray 'direction' (brightest channel 1) at compressed brightness 'x', with its top
            // rebuilt. The rebuild starts at 'start', the lower of the bottom of the band and the point where the
            // brightest channel of the output enters it. Above 'start' the output keeps the color of the .cube but
            // its brightness follows a monotone cubic Hermite going from the .cube at 'start' (value and slope, so
            // the curve stays C1) to the top of the range, which the expansion maps to the top of the HDR range.
            // A neutral .cube gives the identity Hermite, so it stays neutral.
            Double3 SampleRebuilt(in Double3 direction, double x)
            {
                var output = SampleOutput(direction * x);
                if (m_RebuildStart >= m_RangeMax)
                    return output;

                double start = FindRebuildStart(direction);
                if (x <= start)
                    return output;

                // Slope of the .cube at the start, backward difference over a small part of the shoulder.
                double h = Math.Min(start - m_Knee, (m_RangeMax - m_Knee) / 256.0);
                double y0 = SampleOutput(direction * start).Max();
                double slope0 = h > 0.0 ? Math.Max(0.0, (y0 - SampleOutput(direction * (start - h)).Max()) / h) : 1.0;

                double brightness = MonotoneHermite(x, start, y0, slope0, m_RangeMax, m_RangeMax, 1.0);

                double outputMax = output.Max();
                return outputMax > 0.0 ? output * (brightness / outputMax) : direction * brightness;
            }

            double FindRebuildStart(in Double3 direction)
            {
                // The input enters the band at m_RebuildStart, the output can enter it earlier.
                double lo = m_Knee;
                double hi = m_RebuildStart;
                if (SampleOutput(direction * hi).Max() < m_RebuildStart)
                    return hi;

                for (int i = 1; i <= k_RebuildSearchSteps; i++)
                {
                    double x = m_Knee + (m_RebuildStart - m_Knee) * i / k_RebuildSearchSteps;
                    if (SampleOutput(direction * x).Max() >= m_RebuildStart)
                    {
                        hi = x;
                        break;
                    }
                    lo = x;
                }

                for (int i = 0; i < k_RebuildBisections; i++)
                {
                    double mid = 0.5 * (lo + hi);
                    if (SampleOutput(direction * mid).Max() >= m_RebuildStart)
                        hi = mid;
                    else
                        lo = mid;
                }

                return lo;
            }

            // Cubic Hermite from (x0, y0, m0) to (x1, y1, m1). The slopes are limited to three times the secant
            // (Fritsch-Carlson), which keeps the curve monotonic: it never overshoots the top of the range.
            static double MonotoneHermite(double x, double x0, double y0, double m0, double x1, double y1, double m1)
            {
                double dx = x1 - x0;
                double secant = (y1 - y0) / dx;
                if (secant <= 0.0)
                    return y0;

                m0 = Math.Min(m0, 3.0 * secant);
                m1 = Math.Min(m1, 3.0 * secant);

                double t = Math.Min(Math.Max((x - x0) / dx, 0.0), 1.0);
                double t2 = t * t, t3 = t2 * t;
                return (2.0 * t3 - 3.0 * t2 + 1.0) * y0
                     + (t3 - 2.0 * t2 + t) * dx * m0
                     + (-2.0 * t3 + 3.0 * t2) * y1
                     + (t3 - t2) * dx * m1;
            }

            // Identity up to the knee, then a Reinhard shoulder towards the top of the input range. The shoulder
            // starts with a slope of 1, so the curve is C1 at the knee.
            double Tonemap(double x)
            {
                if (x <= m_Knee)
                    return x;

                double shoulder = m_RangeMax - m_Knee;
                double t = x - m_Knee;
                return m_Knee + shoulder * t / (shoulder + t);
            }

            double InverseTonemap(double y)
            {
                if (y <= m_Knee)
                    return y;

                double shoulder = m_RangeMax - m_Knee;
                double t = y - m_Knee;
                return m_Knee + shoulder * t / (shoulder - t);
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

            // Linear in the input gamut in, linear Rec709 out, within the input range of the .cube.
            Double3 Sample(in Double3 linear)
            {
                return SampleOutput(linear).Transform(m_Rec709FromOutput);
            }

            // Linear in the input gamut in, linear in the output gamut out, within the input range of the .cube.
            Double3 SampleOutput(in Double3 linear)
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

        // The URP Neutral tonemapper, as NeutralTonemap in Color.hlsl. It has no clamp: it reaches 1 around 4.04 (the
        // white scale is applied to its input as well as its output) and keeps rising towards ~1.226, so it can be
        // inverted over the whole display range.
        internal static class Neutral
        {
            const double k_A = 0.2, k_B = 0.29, k_C = 0.24, k_D = 0.272, k_E = 0.02, k_F = 0.3, k_WhiteLevel = 5.3;
            static readonly double k_WhiteScale = 1.0 / Curve(k_WhiteLevel);

            static double Curve(double x) => (x * (k_A * x + k_C * k_B) + k_D * k_E) / (x * (k_A * x + k_B) + k_D * k_F) - k_E / k_F;

            public static double Tonemap(double x) => Curve(Math.Max(x, 0.0) * k_WhiteScale) * k_WhiteScale;

            // The input in [0, max] the tonemapper maps to 'y'. The curve is monotonic, so it's a bisection.
            public static double Inverse(double y, double max)
            {
                if (y <= 0.0)
                    return 0.0;
                if (y >= Tonemap(max))
                    return max;

                double lo = 0.0, hi = max;
                for (int i = 0; i < k_InverseNeutralIterations; i++)
                {
                    double mid = 0.5 * (lo + hi);
                    if (Tonemap(mid) < y)
                        lo = mid;
                    else
                        hi = mid;
                }

                return 0.5 * (lo + hi);
            }
        }

        internal static class Encodings
        {
            // Unity LogC, the fast variant without the linear segment, as LinearToLogC / LogCToLinear in Color.hlsl.
            const double k_UnityLogCA = 5.555556, k_UnityLogCB = 0.047996, k_UnityLogCC = 0.244161, k_UnityLogCD = 0.386036;

            // ARRI LogC3, EI 800.
            const double k_LogC3Cut = 0.010591, k_LogC3A = 5.555556, k_LogC3B = 0.052272, k_LogC3C = 0.247190,
                k_LogC3D = 0.385537, k_LogC3E = 5.367655, k_LogC3F = 0.092809;

            // DaVinci Intermediate.
            const double k_DaVinciA = 0.0075, k_DaVinciB = 7.0, k_DaVinciC = 0.07329248, k_DaVinciM = 10.44426855,
                k_DaVinciLinCut = 0.00262409, k_DaVinciLogCut = 0.02740668;

            // Panasonic V-Log.
            const double k_VLogCut = 0.01, k_VLogB = 0.00873, k_VLogC = 0.241514, k_VLogD = 0.598206;

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

                    case CubeLutEncoding.DaVinciIntermediate:
                        return x <= k_DaVinciLinCut ? x * k_DaVinciM : (Math.Log(x + k_DaVinciA, 2.0) + k_DaVinciB) * k_DaVinciC;

                    case CubeLutEncoding.VLog:
                        return x < k_VLogCut ? 5.6 * x + 0.125 : k_VLogC * Math.Log10(x + k_VLogB) + k_VLogD;

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

                    case CubeLutEncoding.DaVinciIntermediate:
                        return v <= k_DaVinciLogCut ? v / k_DaVinciM : Math.Pow(2.0, v / k_DaVinciC - k_DaVinciB) - k_DaVinciA;

                    case CubeLutEncoding.VLog:
                        return v < 0.181 ? (v - 0.125) / 5.6 : Math.Pow(10.0, (v - k_VLogD) / k_VLogC) - k_VLogB;

                    case CubeLutEncoding.SLog3:
                        return v >= 171.2102946929 / 1023.0
                            ? Math.Pow(10.0, (v * 1023.0 - 420.0) / 261.5) * (0.18 + 0.01) - 0.01
                            : (v * 1023.0 - 95.0) * 0.01125 / (171.2102946929 - 95.0);

                    default:
                        throw new ArgumentOutOfRangeException(nameof(encoding));
                }
            }
        }
        internal static class Gamuts
        {
            static readonly double[] k_D65 = { 0.3127, 0.3290 };
            static readonly double[] k_AcesWhite = { 0.32168, 0.33767 };

            // xy chromaticities of the red, green and blue primaries, then of the white point.
            static double[][] Chromaticities(CubeLutGamut gamut)
            {
                switch (gamut)
                {
                    case CubeLutGamut.Rec709: return new[] { new[] { 0.640, 0.330 }, new[] { 0.300, 0.600 }, new[] { 0.150, 0.060 }, k_D65 };
                    case CubeLutGamut.Rec2020: return new[] { new[] { 0.708, 0.292 }, new[] { 0.170, 0.797 }, new[] { 0.131, 0.046 }, k_D65 };
                    case CubeLutGamut.P3D65: return new[] { new[] { 0.680, 0.320 }, new[] { 0.265, 0.690 }, new[] { 0.150, 0.060 }, k_D65 };
                    case CubeLutGamut.ArriWideGamut3: return new[] { new[] { 0.6840, 0.3130 }, new[] { 0.2210, 0.8480 }, new[] { 0.0861, -0.1020 }, k_D65 };
                    case CubeLutGamut.SGamut3: return new[] { new[] { 0.730, 0.280 }, new[] { 0.140, 0.855 }, new[] { 0.100, -0.050 }, k_D65 };
                    case CubeLutGamut.SGamut3Cine: return new[] { new[] { 0.766, 0.275 }, new[] { 0.225, 0.800 }, new[] { 0.089, -0.087 }, k_D65 };
                    case CubeLutGamut.VGamut: return new[] { new[] { 0.730, 0.280 }, new[] { 0.165, 0.840 }, new[] { 0.100, -0.030 }, k_D65 };
                    case CubeLutGamut.DaVinciWideGamut: return new[] { new[] { 0.8000, 0.3130 }, new[] { 0.1682, 0.9877 }, new[] { 0.0790, -0.1155 }, k_D65 };
                    case CubeLutGamut.ACES_AP1: return new[] { new[] { 0.713, 0.293 }, new[] { 0.165, 0.830 }, new[] { 0.128, 0.044 }, k_AcesWhite };
                    default: throw new ArgumentOutOfRangeException(nameof(gamut));
                }
            }

            // Linear RGB of 'from' to linear RGB of 'to', through XYZ. A white point other than D65 is Bradford
            // adapted, so the white of every gamut maps to the white of Rec709.
            public static double[,] Conversion(CubeLutGamut from, CubeLutGamut to)
            {
                if (from == to)
                    return Identity();

                var xyzFromSource = Multiply(Adaptation(Chromaticities(from)[3]), RgbToXyz(from));
                var xyzFromTarget = Multiply(Adaptation(Chromaticities(to)[3]), RgbToXyz(to));
                return Multiply(Invert(xyzFromTarget), xyzFromSource);
            }

            static double[,] RgbToXyz(CubeLutGamut gamut)
            {
                var c = Chromaticities(gamut);
                var m = new double[3, 3];
                for (int i = 0; i < 3; i++)
                {
                    var p = XyToXyz(c[i]);
                    for (int row = 0; row < 3; row++)
                        m[row, i] = p[row];
                }

                // Scale the primaries so that they add up to the white point (Y = 1).
                var white = XyToXyz(c[3]);
                var inv = Invert(m);
                for (int i = 0; i < 3; i++)
                {
                    double s = inv[i, 0] * white[0] + inv[i, 1] * white[1] + inv[i, 2] * white[2];
                    for (int row = 0; row < 3; row++)
                        m[row, i] *= s;
                }

                return m;
            }

            // Bradford chromatic adaptation from 'white' to D65.
            static double[,] Adaptation(double[] white)
            {
                if (white[0] == k_D65[0] && white[1] == k_D65[1])
                    return Identity();

                var bradford = new double[,] { { 0.8951, 0.2664, -0.1614 }, { -0.7502, 1.7135, 0.0367 }, { 0.0389, -0.0685, 1.0296 } };
                var src = XyToXyz(white);
                var dst = XyToXyz(k_D65);
                var scale = new double[3, 3];
                for (int i = 0; i < 3; i++)
                {
                    double s = bradford[i, 0] * src[0] + bradford[i, 1] * src[1] + bradford[i, 2] * src[2];
                    double d = bradford[i, 0] * dst[0] + bradford[i, 1] * dst[1] + bradford[i, 2] * dst[2];
                    scale[i, i] = d / s;
                }

                return Multiply(Invert(bradford), Multiply(scale, bradford));
            }

            static double[] XyToXyz(double[] xy) => new[] { xy[0] / xy[1], 1.0, (1.0 - xy[0] - xy[1]) / xy[1] };

            static double[,] Identity() => new double[,] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };

            static double[,] Multiply(double[,] a, double[,] b)
            {
                var r = new double[3, 3];
                for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    r[i, j] = a[i, 0] * b[0, j] + a[i, 1] * b[1, j] + a[i, 2] * b[2, j];
                return r;
            }

            static double[,] Invert(double[,] m)
            {
                double det = m[0, 0] * (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1])
                           - m[0, 1] * (m[1, 0] * m[2, 2] - m[1, 2] * m[2, 0])
                           + m[0, 2] * (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]);
                var r = new double[3, 3];
                r[0, 0] = (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1]) / det;
                r[0, 1] = (m[0, 2] * m[2, 1] - m[0, 1] * m[2, 2]) / det;
                r[0, 2] = (m[0, 1] * m[1, 2] - m[0, 2] * m[1, 1]) / det;
                r[1, 0] = (m[1, 2] * m[2, 0] - m[1, 0] * m[2, 2]) / det;
                r[1, 1] = (m[0, 0] * m[2, 2] - m[0, 2] * m[2, 0]) / det;
                r[1, 2] = (m[0, 2] * m[1, 0] - m[0, 0] * m[1, 2]) / det;
                r[2, 0] = (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]) / det;
                r[2, 1] = (m[0, 1] * m[2, 0] - m[0, 0] * m[2, 1]) / det;
                r[2, 2] = (m[0, 0] * m[1, 1] - m[0, 1] * m[1, 0]) / det;
                return r;
            }
        }
    }
}
