// The arena's see-through-the-wall shader. Each arena is ONE mesh (walls, floor and hill share a
// single renderer), so a fighter hidden inside a wall cannot be revealed by fading a material -
// the whole arena would ghost. Instead this punches a soft dithered hole along each camera->fighter
// sight line: fragments near that line dissolve, everything else renders as before.
// WallCutout swaps the arena onto this shader only while a fighter is actually occluded, feeds the
// two sight lines in (_CutB0/_CutB1, w = per-fighter strength), and swaps the original material
// back when the view clears. Lighting is a close stand-in for URP Lit on these matte arenas:
// albedo x (SH ambient + shadowed main light + additional lights), with the normal map.
Shader "Crucible/ArenaCutout"
{
    Properties
    {
        _BaseMap("Base Map", 2D) = "white" {}
        _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        _BumpMap("Normal Map", 2D) = "bump" {}
        _CutB0("Cut 0: fighter end (xyz) + strength (w)", Vector) = (0, 0, 0, 0)
        _CutB1("Cut 1: fighter end (xyz) + strength (w)", Vector) = (0, 0, 0, 0)
        _CutRadius("Cut radius (world units)", Float) = 2.2
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);  SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap);  SAMPLER(sampler_BumpMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                float4 _CutB0;
                float4 _CutB1;
                float _CutRadius;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float4 tangentWS  : TEXCOORD2;   // xyz = tangent, w = sign
                float2 uv         : TEXCOORD3;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs pos = GetVertexPositionInputs(v.positionOS.xyz);
                VertexNormalInputs nrm = GetVertexNormalInputs(v.normalOS, v.tangentOS);
                o.positionCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.normalWS = nrm.normalWS;
                o.tangentWS = float4(nrm.tangentWS, v.tangentOS.w);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }

            // 0 on the sight line, rising to 1 at the hole's soft rim. The segment runs from the
            // camera to just past the fighter, so only geometry between the two dissolves.
            float CutFactor(float3 p, float4 cut)
            {
                if (cut.w <= 0.001) return 1.0;
                float3 a = _WorldSpaceCameraPos;
                float3 ab = cut.xyz - a;
                float t = saturate(dot(p - a, ab) / max(dot(ab, ab), 1e-4));
                float d = distance(p, a + ab * t);
                float hole = smoothstep(_CutRadius * 0.5, _CutRadius, d);
                return lerp(1.0, hole, cut.w);
            }

            half4 frag(Varyings i) : SV_Target
            {
                // The cutout first, so hidden fragments cost nothing further.
                float cut = min(CutFactor(i.positionWS, _CutB0), CutFactor(i.positionWS, _CutB1));
                if (cut < 0.999)
                {
                    float noise = InterleavedGradientNoise(i.positionCS.xy, 0);
                    if (cut < noise) discard;
                }

                half4 baseTex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                half3 albedo = baseTex.rgb * _BaseColor.rgb;

                half3 normalTS = UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, i.uv));
                float sgn = i.tangentWS.w * GetOddNegativeScale();
                float3 bitangent = sgn * cross(i.normalWS, i.tangentWS.xyz);
                float3 n = normalize(TransformTangentToWorld(normalTS,
                               half3x3(i.tangentWS.xyz, bitangent, i.normalWS)));

                half3 color = albedo * SampleSH(n);

                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                color += albedo * mainLight.color * mainLight.shadowAttenuation
                                * saturate(dot(n, mainLight.direction));

                #if defined(_ADDITIONAL_LIGHTS)
                uint count = GetAdditionalLightsCount();
                for (uint li = 0u; li < count; li++)
                {
                    Light l = GetAdditionalLight(li, i.positionWS);
                    color += albedo * l.color * l.distanceAttenuation * saturate(dot(n, l.direction));
                }
                #endif

                return half4(color, 1.0);
            }
            ENDHLSL
        }

        // Keeps the walls casting their shadows while the cutout material is swapped in; without
        // this pass the sun shadows pop off for the duration of the fade.
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDir = normalize(_LightPosition - positionWS);
                #else
                float3 lightDir = _LightDirection;
                #endif
                o.positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDir));
                #if UNITY_REVERSED_Z
                o.positionCS.z = min(o.positionCS.z, o.positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #else
                o.positionCS.z = max(o.positionCS.z, o.positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #endif
                return o;
            }

            half4 frag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }

        // Depth for anything URP pre-passes (SSAO, depth texture users).
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformWorldToHClip(TransformObjectToWorld(v.positionOS.xyz));
                return o;
            }

            half4 frag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
