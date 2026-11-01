using System;
using System.Globalization;
using System.IO;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace UnityEditor.Rendering.Universal
{
    /// <summary>
    /// The transfer function a .cube LUT expects its input in, or encodes its output with.
    /// </summary>
    public enum CubeLutEncoding
    {
        /// <summary>sRGB piecewise curve. Covers linear [0;1] only, anything above is extrapolated.</summary>
        sRGB,
        /// <summary>Pure 2.4 gamma (BT.1886 display). Covers linear [0;1] only, anything above is extrapolated.</summary>
        Gamma24,
        /// <summary>The LogC curve URP uses for its internal HDR LUT (linear range ~[0;58.86]).</summary>
        UnityLogC,
        /// <summary>ARRI Alexa LogC3, EI 800 (linear range ~[0;55.08]).</summary>
        ArriLogC3,
        /// <summary>ACEScct (linear range ~[0;222.9]). Usually paired with the ACES AP1 gamut.</summary>
        ACEScct,
        /// <summary>Sony S-Log3 (linear range ~[0;38.42]). Usually paired with S-Gamut3 or S-Gamut3.Cine.</summary>
        SLog3,
        /// <summary>DaVinci Intermediate (linear range ~[0;100]). Usually paired with DaVinci Wide Gamut.</summary>
        DaVinciIntermediate,
        /// <summary>Panasonic V-Log (linear range ~[0;46.1]). Usually paired with V-Gamut.</summary>
        VLog,
    }

    /// <summary>
    /// The color primaries a .cube LUT expects its input in, or produces its output in. URP grades with Rec709
    /// primaries, colors are converted from and to them around the lookup.
    /// </summary>
    public enum CubeLutGamut
    {
        /// <summary>Rec709 / sRGB primaries, D65. No conversion.</summary>
        Rec709,
        /// <summary>Rec2020 primaries, D65.</summary>
        Rec2020,
        /// <summary>Display P3 primaries, D65.</summary>
        P3D65,
        /// <summary>ARRI Wide Gamut 3, D65 (paired with LogC3).</summary>
        ArriWideGamut3,
        /// <summary>Sony S-Gamut3, D65.</summary>
        SGamut3,
        /// <summary>Sony S-Gamut3.Cine, D65.</summary>
        SGamut3Cine,
        /// <summary>Panasonic V-Gamut, D65.</summary>
        VGamut,
        /// <summary>DaVinci Wide Gamut, D65.</summary>
        DaVinciWideGamut,
        /// <summary>ACES AP1 (ACEScg / ACEScct), ACES white point, Bradford adapted to D65.</summary>
        ACES_AP1,
    }

    /// <summary>
    /// How the colors above the input range of the .cube are reconstructed.
    /// </summary>
    public enum CubeLutExtrapolation
    {
        /// <summary>Fit every output channel on its own, so the highlights keep the hue and saturation trends of the LUT.</summary>
        PerChannel,
        /// <summary>Fit the brightest output channel only and scale the output color at the top of the range, keeping its hue.</summary>
        PreserveHue,
        /// <summary>
        /// Treat the .cube as an SDR grade and stretch it to the HDR range: the colors are compressed into the input
        /// range with a Reinhard shoulder above a knee, go through the .cube, and the result is expanded back with
        /// the exact inverse. A neutral .cube stays neutral over the whole HDR range, the highlights follow the
        /// shoulder of the .cube instead of a fit. Meant for display referred .cube files (sRGB, gamma 2.4).
        /// </summary>
        InverseTonemap,
    }

    /// <summary>
    /// Imports a .cube file (Adobe / Resolve 3D LUT) as a 3D texture usable by the <c>CubeLookup</c> volume component.
    ///
    /// The .cube is parsed and resampled once at import time onto the LUT space of the internal HDR grading LUT:
    /// the texture coordinates and the stored colors are both encoded with the Unity LogC curve, so the LUT builder
    /// samples it with the LogC value it already has and decodes the result with the curve it already uses.
    ///
    /// Display referred LUTs (sRGB, gamma 2.4) only cover linear [0;1] and log LUTs cover a range that can be
    /// smaller than the internal one. The colors above the input range are extrapolated along the ray going from
    /// black through the color: a polynomial is fit, in log2 / log2 space, on key points sampled in the top stops
    /// of the range, and evaluated past its end. Or, for SDR grades, the whole HDR range is compressed into the
    /// input range with an invertible tonemapper around the lookup. See <see cref="CubeLutBaker"/>.
    /// </summary>
    [ScriptedImporter(4, "cube")]
    public sealed class CubeLutImporter : ScriptedImporter
    {
        /// <summary>The transfer function the .cube expects its input in.</summary>
        [Tooltip("The transfer function the .cube expects its input in.")]
        public CubeLutEncoding inputEncoding = CubeLutEncoding.sRGB;

        /// <summary>The color primaries the .cube expects its input in.</summary>
        [Tooltip("The color primaries the .cube expects its input in.")]
        public CubeLutGamut inputGamut = CubeLutGamut.Rec709;

        /// <summary>The transfer function the .cube encodes its output with.</summary>
        [Tooltip("The transfer function the .cube encodes its output with.")]
        public CubeLutEncoding outputEncoding = CubeLutEncoding.sRGB;

        /// <summary>The color primaries the .cube produces its output in.</summary>
        [Tooltip("The color primaries the .cube produces its output in.")]
        public CubeLutGamut outputGamut = CubeLutGamut.Rec709;

        /// <summary>Size of the imported 3D texture. It is independent of the size of the .cube.</summary>
        [Tooltip("Size of the imported 3D texture. It is independent of the size of the .cube.")]
        [Range(CubeLutBaker.k_MinSize, CubeLutBaker.k_MaxSize)]
        public int size = 33;

        /// <summary>How the colors above the input range of the .cube are reconstructed.</summary>
        [Tooltip("How the colors above the input range of the .cube are reconstructed.")]
        public CubeLutExtrapolation extrapolation = CubeLutExtrapolation.PerChannel;

        /// <summary>Order of the polynomial fit on the key points.</summary>
        [Tooltip("Order of the polynomial fit on the key points.")]
        [Range(2, 4)]
        public int polynomialOrder = 3;

        /// <summary>Number of key points the polynomial is fit on, the top of the input range included.</summary>
        [Tooltip("Number of key points the polynomial is fit on, the top of the input range included.")]
        [Range(3, 16)]
        public int keyPointCount = 8;

        /// <summary>How many stops below the top of the input range the key points span.</summary>
        [Tooltip("How many stops below the top of the input range the key points span.")]
        [Range(0.5f, 8f)]
        public float fitRangeStops = 3f;

        /// <summary>
        /// Inverse Tonemap only: fraction of the input range left untouched by the tonemapper. The colors below it
        /// reach the .cube unchanged, the ones above are compressed into the rest of the range.
        /// </summary>
        [Tooltip("Inverse Tonemap only: fraction of the input range left untouched by the tonemapper. The colors below it reach the .cube unchanged, the ones above are compressed into the rest of the range.")]
        [Range(0.1f, 0.9f)]
        public float tonemapKnee = 0.5f;

        /// <summary>
        /// Inverse Tonemap only: where the top of the .cube is rebuilt, as a fraction of the range above the knee.
        /// An SDR grade often clips there, and a clipped output has nothing left to expand: it would jump to the
        /// top of the HDR range. From the point where the input or the output of the .cube enters this band, its
        /// brightness is replaced by a smooth curve continuing it to the top of the range. 1 disables it.
        /// </summary>
        [Tooltip("Inverse Tonemap only: where the top of the .cube is rebuilt, as a fraction of the range above the knee. From the point where the input or the output of the .cube enters this band, its brightness is replaced by a smooth curve continuing it to the top, instead of expanding clipped values. 1 disables it.")]
        [Range(0.5f, 1f)]
        public float highlightRebuildStart = 0.8f;

        /// <inheritdoc/>
        public override void OnImportAsset(AssetImportContext ctx)
        {
            CubeLutFile cube;
            try
            {
                cube = CubeLutFile.Parse(File.ReadAllText(ctx.assetPath));
            }
            catch (Exception e)
            {
                ctx.LogImportError($"Failed to parse the .cube file: {e.Message}");
                return;
            }

            var settings = new CubeLutBaker.Settings
            {
                inputEncoding = inputEncoding,
                outputEncoding = outputEncoding,
                inputGamut = inputGamut,
                outputGamut = outputGamut,
                size = Mathf.Clamp(size, CubeLutBaker.k_MinSize, CubeLutBaker.k_MaxSize),
                extrapolation = extrapolation,
                polynomialOrder = Mathf.Clamp(polynomialOrder, 2, 4),
                // The top of the range is the constraint of the fit, the others have to determine the coefficients.
                keyPointCount = Mathf.Max(keyPointCount, Mathf.Clamp(polynomialOrder, 2, 4) + 1),
                fitRangeStops = Mathf.Max(fitRangeStops, 0.5f),
                tonemapKnee = Mathf.Clamp(tonemapKnee, 0.1f, 0.9f),
                highlightRebuildStart = Mathf.Clamp(highlightRebuildStart, 0.5f, 1f),
            };

            var colors = CubeLutBaker.Bake(cube, settings);

            int n = settings.size;
            var texture = new Texture3D(n, n, n, GraphicsFormat.R16G16B16A16_SFloat, TextureCreationFlags.None)
            {
                name = Path.GetFileNameWithoutExtension(ctx.assetPath),
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 0,
            };
            texture.SetPixels(colors);
            texture.Apply(false, true);

            ctx.AddObjectToAsset("lut", texture);
            ctx.SetMainObject(texture);
        }
    }

    /// <summary>
    /// The content of a .cube file: a 3D table, red varying fastest, and the input domain it covers.
    /// </summary>
    internal sealed class CubeLutFile
    {
        public int size;
        public Vector3 domainMin = Vector3.zero;
        public Vector3 domainMax = Vector3.one;
        public Vector3[] data;

        public static CubeLutFile Parse(string text)
        {
            var cube = new CubeLutFile();
            int count = 0;

            using (var reader = new StringReader(text))
            {
                string line;
                int lineNumber = 0;

                while ((line = reader.ReadLine()) != null)
                {
                    lineNumber++;

                    int comment = line.IndexOf('#');
                    if (comment >= 0)
                        line = line.Substring(0, comment);

                    line = line.Trim();
                    if (line.Length == 0)
                        continue;

                    var tokens = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    string keyword = tokens[0].ToUpperInvariant();

                    if (TryParseFloat(tokens[0], out _))
                    {
                        if (cube.data == null)
                            throw new FormatException($"line {lineNumber}: table data before LUT_3D_SIZE.");
                        if (tokens.Length != 3)
                            throw new FormatException($"line {lineNumber}: expected 3 values.");
                        if (count >= cube.data.Length)
                            throw new FormatException($"line {lineNumber}: more entries than LUT_3D_SIZE^3.");

                        cube.data[count++] = ParseVector(tokens, 0, lineNumber);
                        continue;
                    }

                    switch (keyword)
                    {
                        case "TITLE":
                            break;

                        case "LUT_3D_SIZE":
                            if (cube.data != null)
                                throw new FormatException($"line {lineNumber}: LUT_3D_SIZE appears twice.");
                            if (tokens.Length != 2 || !int.TryParse(tokens[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out cube.size) || cube.size < 2 || cube.size > 256)
                                throw new FormatException($"line {lineNumber}: invalid LUT_3D_SIZE.");
                            cube.data = new Vector3[cube.size * cube.size * cube.size];
                            break;

                        case "LUT_1D_SIZE":
                            throw new FormatException("1D LUTs are not supported.");

                        case "DOMAIN_MIN":
                            cube.domainMin = ParseVector(tokens, 1, lineNumber);
                            break;

                        case "DOMAIN_MAX":
                            cube.domainMax = ParseVector(tokens, 1, lineNumber);
                            break;

                        // Resolve flavour of the domain, the same range for the three channels.
                        case "LUT_3D_INPUT_RANGE":
                            if (tokens.Length != 3 || !TryParseFloat(tokens[1], out float min) || !TryParseFloat(tokens[2], out float max))
                                throw new FormatException($"line {lineNumber}: invalid LUT_3D_INPUT_RANGE.");
                            cube.domainMin = new Vector3(min, min, min);
                            cube.domainMax = new Vector3(max, max, max);
                            break;

                        default:
                            // Unknown keywords are vendor extensions, they never change the table layout.
                            break;
                    }
                }
            }

            if (cube.data == null)
                throw new FormatException("missing LUT_3D_SIZE.");
            if (count != cube.data.Length)
                throw new FormatException($"expected {cube.data.Length} entries, found {count}.");
            for (int c = 0; c < 3; c++)
            {
                if (!(cube.domainMax[c] > cube.domainMin[c]))
                    throw new FormatException("DOMAIN_MAX must be greater than DOMAIN_MIN.");
            }

            return cube;
        }

        static bool TryParseFloat(string s, out float value)
        {
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        static Vector3 ParseVector(string[] tokens, int start, int lineNumber)
        {
            if (tokens.Length != start + 3
                || !TryParseFloat(tokens[start], out float x)
                || !TryParseFloat(tokens[start + 1], out float y)
                || !TryParseFloat(tokens[start + 2], out float z))
                throw new FormatException($"line {lineNumber}: expected 3 values.");

            return new Vector3(x, y, z);
        }
    }
}
