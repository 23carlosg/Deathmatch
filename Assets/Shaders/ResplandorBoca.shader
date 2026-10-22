Shader "Custom/MuzzleGlow"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (0.3, 0.9, 1.0, 1)
        _Suavidad ("Suavidad del borde", Range(0.5, 6)) = 2
        _Intensidad ("Intensidad", Range(0, 10)) = 3
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "Glow"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha One      // aditivo
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Suavidad, _Intensidad;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                OUT.color = IN.color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 p = (IN.uv - 0.5) * 2.0;
                float r = saturate(length(p));

                // circulo suave: brillante al centro, se apaga hacia el borde
                float brillo = pow(1.0 - r, _Suavidad);

                half3 col = lerp(_Color.rgb, half3(1, 1, 1), brillo * 0.6) * IN.color.rgb;
                return half4(col * _Intensidad, brillo * IN.color.a);
            }
            ENDHLSL
        }
    }
}
