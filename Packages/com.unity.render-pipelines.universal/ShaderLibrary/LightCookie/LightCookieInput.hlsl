#ifndef UNIVERSAL_LIGHT_COOKIE_INPUT_INCLUDED
#define UNIVERSAL_LIGHT_COOKIE_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/GlobalSamplers.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LightCookie/LightCookieTypes.hlsl"

// Textures
TEXTURE2D(_MainLightCookieTexture);

// Samplers
SAMPLER(sampler_MainLightCookieTexture);

// Buffers
// Only the main light supports cookies, additional lights don't.
// GLES3 causes a performance regression in some devices when using CBUFFER.
#ifndef LIGHT_SHADOWS_NO_CBUFFER
CBUFFER_START(LightCookies)
#endif
    float4x4 _MainLightWorldToLight;
    float _MainLightCookieTextureFormat;
#ifndef LIGHT_SHADOWS_NO_CBUFFER
CBUFFER_END
#endif

// Data Getters

bool IsMainLightCookieTextureRGBFormat()
{
    return _MainLightCookieTextureFormat == URP_LIGHT_COOKIE_FORMAT_RGB;
}

bool IsMainLightCookieTextureAlphaFormat()
{
    return _MainLightCookieTextureFormat == URP_LIGHT_COOKIE_FORMAT_ALPHA;
}

// Sampling

real4 SampleMainLightCookieTexture(float2 uv)
{
    return SAMPLE_TEXTURE2D(_MainLightCookieTexture, sampler_MainLightCookieTexture, uv);
}

// Helpers
bool IsMainLightCookieEnabled()
{
    return _MainLightCookieTextureFormat != URP_LIGHT_COOKIE_FORMAT_NONE;
}

#endif //UNIVERSAL_LIGHT_COOKIE_INPUT_INCLUDED
