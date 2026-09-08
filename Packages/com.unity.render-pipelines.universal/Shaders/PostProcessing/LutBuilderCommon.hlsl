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

#endif // UNIVERSAL_POSTPROCESSING_LUTBUILDER_COMMON_INCLUDED
