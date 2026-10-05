Shader "Custom/Portal"
{
    Properties
    {
        [HDR] _ColorExterior ("Color exterior", Color) = (0.0, 0.35, 0.9, 1)
        [HDR] _ColorCeleste  ("Color celeste", Color)  = (0.3, 0.9, 1.0, 1)
        [HDR] _ColorNucleo   ("Color núcleo", Color)   = (0.9, 1.0, 1.0, 1)
        _Brazos      ("Cantidad de brazos", Range(1, 10)) = 4
        _Torsion     ("Torsión de la espiral", Range(0, 20)) = 7
        _Velocidad   ("Velocidad", Range(-5, 5)) = 1.5
        _Nitidez     ("Nitidez de los brazos", Range(0.5, 6)) = 2
        _EscalaRuido ("Escala del ruido", Range(0.5, 8)) = 3
        _FuerzaRuido ("Fuerza del ruido", Range(0, 1)) = 0.35
        _TamanoNucleo("Tamaño del núcleo", Range(0.5, 8)) = 3.5
        _BordeSuave  ("Suavidad del borde", Range(0.01, 0.6)) = 0.25
        _Intensidad  ("Intensidad", Range(0, 5)) = 1.5
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "Portal"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha One      // aditivo: brilla sobre el fondo
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ColorExterior, _ColorCeleste, _ColorNucleo;
                float _Brazos, _Torsion, _Velocidad, _Nitidez;
                float _EscalaRuido, _FuerzaRuido, _TamanoNucleo, _BordeSuave, _Intensidad;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float valueNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = hash21(i);
                float b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1));
                float d = hash21(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float t = _Time.y * _Velocidad;
                float2 p = (IN.uv - 0.5) * 2.0;     // -1..1 con centro en (0,0)
                float r = length(p);
                float ang = atan2(p.y, p.x);

                // ruido rotado según el radio: da aspecto de energía/nube arremolinada
                float giro = r * _Torsion * 0.5 - t * 0.6;
                float s, c; sincos(giro, s, c);
                float2 pr = float2(c * p.x - s * p.y, s * p.x + c * p.y);
                float ruido = valueNoise(pr * _EscalaRuido + t * 0.2);

                // brazos en espiral que viajan hacia el centro
                float fase = ang * _Brazos + r * _Torsion - t * 2.0 + (ruido - 0.5) * _FuerzaRuido * 6.0;
                float brazos = pow(saturate(sin(fase) * 0.5 + 0.5), _Nitidez);

                float patron = saturate(brazos + ruido * _FuerzaRuido);

                // máscara circular con borde suave
                float mascara = smoothstep(1.0, 1.0 - _BordeSuave, r);

                // núcleo brillante
                float nucleo = exp(-r * _TamanoNucleo);

                // anillo de energía en el borde
                float anillo = smoothstep(0.08, 0.0, abs(r - (1.0 - _BordeSuave * 0.5))) * 0.6;

                half3 col = lerp(_ColorExterior.rgb, _ColorCeleste.rgb, patron);
                col = lerp(col, _ColorNucleo.rgb, saturate(nucleo));
                col += _ColorCeleste.rgb * anillo;

                float alfa = mascara * saturate(patron * 0.9 + nucleo + anillo);
                return half4(col * _Intensidad, alfa);
            }
            ENDHLSL
        }
    }
}