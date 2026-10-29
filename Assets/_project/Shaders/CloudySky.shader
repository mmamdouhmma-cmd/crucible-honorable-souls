// Partly cloudy late-afternoon sky for the story arenas (Musashi Chapter One first).
// A gradient sky with a sun disc and halo, and two layers of value-noise clouds projected onto a
// dome: blue-grey undersides, lit warm on the side facing the sun. Unity's Skybox/Procedural has
// no clouds, and an HDRI would be a downloaded asset; this is a few dozen ALU and no textures.
// The sun direction is a material property (_SunDir), written by the scene builder from the
// directional light, so the disc always sits where the shadows say the sun is.
Shader "Crucible/CloudySky"
{
    Properties
    {
        _ZenithColor ("Zenith", Color) = (0.36, 0.48, 0.66, 1)
        _HorizonColor ("Horizon", Color) = (0.86, 0.76, 0.62, 1)
        _GroundColor ("Below horizon", Color) = (0.34, 0.30, 0.26, 1)
        _HorizonBlend ("Horizon softness", Range(0.1, 2)) = 0.5
        _SunDir ("Sun direction (towards the sun)", Vector) = (0.5, 0.3, 0.6, 0)
        _SunColor ("Sun", Color) = (1.0, 0.82, 0.58, 1)
        _SunSize ("Sun disc size", Range(0.001, 0.1)) = 0.025
        _SunHalo ("Sun halo", Range(0, 2)) = 0.6
        _CloudLit ("Cloud, sunlit", Color) = (1.0, 0.90, 0.78, 1)
        _CloudShade ("Cloud, shaded", Color) = (0.44, 0.48, 0.56, 1)
        _CloudCoverage ("Cloud coverage", Range(0, 1)) = 0.48
        _CloudSoftness ("Cloud edge softness", Range(0.01, 0.5)) = 0.18
        _CloudScale ("Cloud scale", Float) = 1.6
        _CloudSpeed ("Cloud drift", Float) = 0.004
        _Exposure ("Exposure", Range(0, 3)) = 1.0
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            half4 _ZenithColor, _HorizonColor, _GroundColor, _SunColor, _CloudLit, _CloudShade;
            float4 _SunDir;
            float _HorizonBlend, _SunSize, _SunHalo, _CloudCoverage, _CloudSoftness, _CloudScale, _CloudSpeed, _Exposure;

            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

            float noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), u.x),
                            lerp(hash(i + float2(0, 1)), hash(i + float2(1, 1)), u.x), u.y);
            }

            float fbm(float2 p)
            {
                float v = 0.0, a = 0.5;
                for (int k = 0; k < 6; k++) { v += a * noise(p); p = p * 2.03 + float2(17.1, 9.3); a *= 0.5; }
                return v;
            }

            half4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float3 L = normalize(_SunDir.xyz);
                float h = d.y;

                // gradient: warm horizon to blue-grey zenith, a darker band below the horizon
                float up = pow(saturate(h), _HorizonBlend);
                float3 sky = lerp(_HorizonColor.rgb, _ZenithColor.rgb, up);
                sky = lerp(sky, _GroundColor.rgb, saturate(-h * 6.0));

                // sun: warm wash across the sky on the sun's side, then halo and disc
                float mu = dot(d, L);
                sky += _SunColor.rgb * (pow(saturate(mu), 4.0) * 0.18 * saturate(1.0 - up) + pow(saturate(mu), 48.0) * _SunHalo * 0.35);
                float disc = smoothstep(1.0 - _SunSize, 1.0 - _SunSize * 0.6, mu);
                float c = 0.0;

                // clouds on a dome above the horizon
                if (h > 0.0)
                {
                    float2 uv = d.xz / (h + 0.15) * _CloudScale + _Time.y * _CloudSpeed * float2(1.0, 0.35);
                    float n = fbm(uv);
                    float detail = fbm(uv * 3.1 + 5.2);
                    c = smoothstep(1.0 - _CloudCoverage, 1.0 - _CloudCoverage + _CloudSoftness, n * 0.8 + detail * 0.25);
                    c *= saturate(h * 5.0);                                  // thin out into the haze
                    // lighting: thick cores shade blue-grey, thin edges and the sun side glow warm
                    float thick = saturate((n - (1.0 - _CloudCoverage)) * 3.0);
                    float3 col = lerp(_CloudLit.rgb, _CloudShade.rgb, thick * 0.85);
                    col += _SunColor.rgb * pow(saturate(mu), 6.0) * (1.0 - thick) * 0.7;
                    col = lerp(col, _HorizonColor.rgb, saturate(1.0 - h * 3.0) * 0.6);   // aerial perspective
                    sky = lerp(sky, col, c * 0.92);
                }
                sky += _SunColor.rgb * disc * 4.0 * (1.0 - c);               // the disc is hidden by cloud
                return half4(sky * _Exposure, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
