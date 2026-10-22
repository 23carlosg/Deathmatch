Shader "Custom/LaserBeam"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (0.2, 0.9, 1.0, 1)
        _Suavidad ("Suavidad del borde", Range(0.5, 8)) = 2.5
        _Nucleo ("Finura del nucleo blanco", Range(1, 30)) = 10
        _Intensidad ("Intensidad", Range(0, 10)) = 4
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "Laser"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha One      // aditivo: brilla sobre el fondo
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Suavidad, _Nucleo, _Intensidad;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;     // color/alfa del LineRenderer
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
                // uv.y recorre el ANCHO del rayo: 0 en un borde, 1 en el otro
                float centro = 1.0 - abs(IN.uv.y * 2.0 - 1.0);   // 0 en los bordes, 1 en el medio

                float halo = pow(saturate(centro), _Suavidad);   // resplandor suave
                float nucleo = pow(saturate(centro), _Nucleo);   // linea fina y blanca al centro

                half3 col = lerp(_Color.rgb, half3(1, 1, 1), nucleo) * IN.color.rgb;
                return half4(col * _Intensidad, halo * IN.color.a);
            }
            ENDHLSL
        }
    }
}
