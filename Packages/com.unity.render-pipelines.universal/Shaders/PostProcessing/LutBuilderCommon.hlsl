#ifndef UNIVERSAL_POSTPROCESSING_LUTBUILDER_COMMON_INCLUDED
#define UNIVERSAL_POSTPROCESSING_LUTBUILDER_COMMON_INCLUDED

// ----------------------------------------------------------------------------------
// Color grading LUT super sampling
//
// One LUT entry stores a single evaluation of the grading chain, and the run time reconstructs the
// values in between with a trilinear interpolation. That only matches the real grading chain where the
// chain is close to linear, which it is not: the curve lookups are piecewise, the shadows/midtones/
// highlights split uses smoothstep thresholds and the hue operations wrap around. Every one of those
// puts a kink somewhere in the [0;1] cube that the LUT cannot represent, and the result is banding or
// a visible break in a gradient.
//
// Rather than one sample per entry, evaluate the chain at several positions spread around the entry and
// average them. This is a box prefilter of the grading chain: the kinks get smeared over the cell they
// fall in instead of landing on (or being missed by) a single lattice point.

// Number of samples evaluated per LUT entry.
#define LUT_SUPER_SAMPLE_COUNT 4

// How far the super samples sit from the LUT entry, as a fraction of the distance between two entries.
// 0.5 would reach the neighbouring entries: keep it below that, the trilinear interpolation already
// filters across the whole cell and a wider prefilter only softens the grade.
#define LUT_SUPER_SAMPLE_SPREAD 0.25

// Directions of the super samples: the four vertices of a regular tetrahedron. They sum to zero, so the
// average stays unbiased and does not shift the LUT, and they spread evenly in the three dimensions
// instead of favouring the axes like an axis aligned pattern would.
static const float3 kLutSuperSampleDirections[LUT_SUPER_SAMPLE_COUNT] =
{
    float3( 1.0,  1.0,  1.0),
    float3( 1.0, -1.0, -1.0),
    float3(-1.0,  1.0, -1.0),
    float3(-1.0, -1.0,  1.0)
};

// Returns the LUT space position of the super sample 'index' around the entry at 'lutValue'.
// params = (lut_height, 0.5 / lut_width, 0.5 / lut_height, lut_height / lut_height - 1), so consecutive
// entries are 1 / (lut_height - 1) apart in LUT space.
// The position is clamped to the cube: the run time never reconstructs anything outside of it, so
// sampling there would only bias the entries sitting on the borders.
float3 GetLutSuperSampleValue(float3 lutValue, float4 params, int index)
{
    float entryStep = rcp(params.x - 1.0);
    return saturate(lutValue + kLutSuperSampleDirections[index] * (LUT_SUPER_SAMPLE_SPREAD * entryStep));
}

// ----------------------------------------------------------------------------------
// Full precision RGB <-> HSV
//
// The grading chain runs every color through RgbToHsv and back, so with neutral hue/saturation curves
// the pair has to be an exact identity. The core versions are not, for two reasons.
//
// 1. They are written in 'real', which is 'half' on mobile. HsvToRgb rebuilds the minimum channel as
//    V * (1 - S), a difference of two nearly equal numbers, so that channel picks up an *absolute*
//    error of roughly V * 2^-11 no matter how small it is. In the LUT V reaches ~58.9 (the top of the
//    LogC range), which puts up to ~1e-1 of error on a channel whose real value is near zero. This is
//    the dominant error on mobile and no choice of epsilon affects it.
// 2. The 'q.x + e' / '6 * d + e' denominators bias every result, not just the degenerate ones. With
//    the core's e = 1e-4 the darkest channel is lifted by about d * e / V, so a color with no blue in
//    it comes back with ~1e-4 of blue. Dropping e to 0 removes the bias but makes both divisions 0/0
//    on any neutral gray - including the (0,0,0) entry of the LUT, since LogCToLinear(0) is negative
//    and the chain clamps it to exactly zero - which yields NaN, and a NaN entry spreads to every
//    neighbouring cell through the run time trilinear interpolation.
//
// So: compute in float, and floor the divisors instead of biasing them.
//
// The floor sits far under any linear value the grading chain produces, so it never changes a real
// result, and far over FLT_MIN, so no division can overflow to an infinity - which would come back as
// a NaN the moment HsvToRgb multiplies it by a zero value.
#define LUT_HSV_DIVISOR_FLOOR 1e-20

float3 RgbToHsv_Precise(float3 c)
{
    const float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
    float4 p = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
    float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));

    // q.x is the maximum channel and d the spread, so abs(q.w - q.y) <= d: the hue quotient stays under
    // 1/6 for any non zero d, and for a gray, where d is zero, that numerator is exactly zero as well.
    float d = q.x - min(q.w, q.y);
    float hue = abs(q.z + (q.w - q.y) / max(6.0 * d, LUT_HSV_DIVISOR_FLOOR));

    // Only the magnitude is floored. A negative channel mixer or lift coefficient can push every channel
    // below zero, and such a color still round trips exactly as long as the sign of the value survives.
    float signedValue = (q.x < 0.0 ? -1.0 : 1.0) * max(abs(q.x), LUT_HSV_DIVISOR_FLOOR);
    float sat = d / signedValue;

    return float3(hue, sat, q.x);
}

float3 HsvToRgb_Precise(float3 c)
{
    const float4 K = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
    float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
    return c.z * lerp(K.xxx, saturate(p - K.xxx), c.y);
}

#endif // UNIVERSAL_POSTPROCESSING_LUTBUILDER_COMMON_INCLUDED
