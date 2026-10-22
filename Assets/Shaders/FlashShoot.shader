Shader "Custom/FlashShoot.shader"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (0.3, 0.9, 1.0, 1)
        [IntRange] _Rayos ("Cantidad de rayos", Range(2, 12)) = 6
        _LargoRayos ("Largo de los rayos", Range(0.1, 1)) = 0.8
        _FinuraRayos ("Finura de los rayos", Range(1, 16)) = 6
        _Nucleo ("Concentracion del nucleo", Range(1, 20)) = 7
        _Intensidad ("Intensidad", Range(0, 10)) = 3
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "Flash"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha One      // aditivo: suma luz sobre lo que hay detras
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Rayos, _LargoRayos, _FinuraRayos, _Nucleo, _Intensidad;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;     // color del Particle System (Color over Lifetime)
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
                float2 p = (IN.uv - 0.5) * 2.0;     // -1..1, centro en (0,0)
                float r = length(p);
                float ang = atan2(p.y, p.x);

                // rayos en forma de estrella que se afinan hacia la punta
                float rayos = pow(saturate(cos(ang * _Rayos) * 0.5 + 0.5), _FinuraRayos);
                rayos *= saturate(1.0 - r / _LargoRayos);

                // nucleo brillante en el centro
                float nucleo = exp(-r * _Nucleo);

                // corte suave en el borde del quad
                float mascara = smoothstep(1.0, 0.75, r);

                float forma = saturate(rayos + nucleo) * mascara;

                half3 col = lerp(_Color.rgb, half3(1, 1, 1), saturate(nucleo)) * IN.color.rgb;
                return half4(col * _Intensidad, forma * IN.color.a);
            }
            ENDHLSL
        }
    }
}
