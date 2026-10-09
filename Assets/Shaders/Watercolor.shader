// Watercolor look: soft light/shade washes with lilac shadows, uneven pigment in world space,
// darker pigment at the edges and a fixed paper grain on screen.
Shader "Glow/Watercolor"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _MainTex ("Texture", 2D) = "white" {}
        _ShadeColor ("Shade", Color) = (0.68, 0.64, 0.9, 1)
        _LitColor ("Lit", Color) = (1.03, 1, 0.95, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "AutoLight.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color, _ShadeColor, _LitColor;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 wpos : TEXCOORD1;
                float3 normal : TEXCOORD2;
                float4 screen : TEXCOORD3;
                SHADOW_COORDS(4)
            };

            float hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float noise(float3 x)
            {
                float3 i = floor(x), f = frac(x);
                f = f * f * (3 - 2 * f);
                return lerp(
                    lerp(lerp(hash(i), hash(i + float3(1, 0, 0)), f.x), lerp(hash(i + float3(0, 1, 0)), hash(i + float3(1, 1, 0)), f.x), f.y),
                    lerp(lerp(hash(i + float3(0, 0, 1)), hash(i + float3(1, 0, 1)), f.x), lerp(hash(i + float3(0, 1, 1)), hash(i + float3(1, 1, 1)), f.x), f.y),
                    f.z);
            }

            v2f vert(appdata_base v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.screen = ComputeScreenPos(o.pos);
                TRANSFER_SHADOW(o);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 n = normalize(i.normal);
                float3 v = normalize(_WorldSpaceCameraPos - i.wpos);
                // Wrapped light with a soft-edged step: two washes (lit, shade) instead of a smooth gradient.
                float wrap = dot(n, _WorldSpaceLightPos0.xyz) * 0.5 + 0.5;
                float lit = smoothstep(0.45, 0.75, wrap) * SHADOW_ATTENUATION(i);
                float3 col = tex2D(_MainTex, i.uv).rgb * _Color.rgb * lerp(_ShadeColor.rgb, _LitColor.rgb, lit);
                // Uneven pigment, fixed to the world so it doesn't swim when the camera moves:
                // denser blotches turn cooler, thinner ones warmer.
                float pig = noise(i.wpos * 1.2) * 0.65 + noise(i.wpos * 4) * 0.35;
                col *= lerp(float3(0.88, 0.87, 0.97), float3(1.05, 1.03, 0.99), pig);
                // Pigment pools where a wash ends: darker toward the silhouette.
                col *= 1 - 0.18 * pow(1 - saturate(dot(n, v)), 3);
                // Paper showing through, plus a fine grain fixed on screen.
                col = lerp(col, float3(1, 0.98, 0.95), 0.1);
                float2 px = i.screen.xy / i.screen.w * _ScreenParams.xy;
                col *= 0.97 + 0.03 * hash(float3(floor(px / 2), 0));
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
    Fallback "Legacy Shaders/VertexLit" // shadow caster pass
}
