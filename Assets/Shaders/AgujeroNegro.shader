Shader "Custom/AgujeroNegro"
{
    Properties
    {
        [Header(Disco de acrecion)]
        [HDR] _ColorInterior ("Color interior (caliente)", Color) = (1.0, 0.8, 0.45, 1)
        [HDR] _ColorExterior ("Color exterior (frio)", Color) = (0.9, 0.22, 0.04, 1)
        _RadioDisco ("Radio exterior del disco", Range(0.4, 1.0)) = 0.9
        _Inclinacion ("Inclinacion (0 = de canto, 1 = de frente)", Range(0.05, 1)) = 0.28
        _Velocidad ("Velocidad de giro", Range(-4, 4)) = 1
        _EscalaRuido ("Escala de las franjas", Range(1, 14)) = 5
        _Doppler ("Lado mas brillante (efecto Doppler)", Range(0, 0.9)) = 0.6

        [Header(Agujero)]
        _RadioHorizonte ("Radio del horizonte (zona negra)", Range(0.05, 0.5)) = 0.22
        _Distorsion ("Distorsion de la luz del fondo", Range(0, 3)) = 1.5
        [HDR] _ColorFoton ("Color del anillo de fotones", Color) = (1.0, 0.95, 0.85, 1)

        [Header(General)]
        _Intensidad ("Intensidad", Range(0, 10)) = 3
        _DistanciaSuave ("Desvanecer cerca de paredes (distancia)", Range(0.01, 5)) = 1.0
        _Resplandor ("Resplandor alrededor", Range(0, 1)) = 0.25
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "AgujeroNegro"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // Textura de lo que ya se dibujo (incluye el skybox): hay que activar
            // "Opaque Texture" en el URP Asset
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            // Profundidad de la escena: para apagar el efecto donde toca paredes
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            

            CBUFFER_START(UnityPerMaterial)
                half4 _ColorInterior, _ColorExterior, _ColorFoton;
                float _RadioDisco, _Inclinacion, _Velocidad, _EscalaRuido, _Doppler;
                float _RadioHorizonte, _Distorsion, _Intensidad, _DistanciaSuave, _Resplandor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float tamanoPantalla : TEXCOORD1;   // mitad del quad en UV de pantalla (eje Y)
            };

            // Billboard: el quad siempre mira a la camara, sin script. Usar un Quad de Unity (1x1).
            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                float3 centro = float3(unity_ObjectToWorld._m03, unity_ObjectToWorld._m13, unity_ObjectToWorld._m23);
                float escala = length(float3(unity_ObjectToWorld._m00, unity_ObjectToWorld._m10, unity_ObjectToWorld._m20));
                float3 derecha = UNITY_MATRIX_V[0].xyz;
                float3 arriba = UNITY_MATRIX_V[1].xyz;

                float3 mundo = centro + derecha * (IN.positionOS.x * escala) + arriba * (IN.positionOS.y * escala);
                OUT.positionCS = TransformWorldToHClip(mundo);
                OUT.uv = IN.uv;

                // Cuanto ocupa el quad en pantalla: sirve para que la distorsion tenga el mismo
                // aspecto de cerca y de lejos
                float dist = max(distance(_WorldSpaceCameraPos, centro), 0.01);
                OUT.tamanoPantalla = 0.5 * (0.5 * escala) * unity_CameraProjection._m11 / dist;
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
                float2 p = (IN.uv - 0.5) * 2.0;     // -1..1, centro en (0,0)
                float r = length(p);
                float t = _Time.y * _Velocidad;

                // el quad se recorta en un circulo suave para que no se vea el cuadrado
                float bordeQuad = smoothstep(1.0, 0.92, r);

                // ---------- LENTE GRAVITACIONAL ----------
                // La luz del fondo se curva hacia el agujero: se toma el color de un punto mas
                // cercano al centro. Cerca del horizonte la curvatura es enorme; hacia el borde
                // del quad se apaga, asi no hay costura con el resto de la escena.
                float deflexion = _Distorsion * (_RadioHorizonte * _RadioHorizonte) / (r * r + 0.02);
                deflexion = saturate(deflexion) * smoothstep(1.0, 0.3, r);

                float2 dirUV = p;
                #if UNITY_UV_STARTS_AT_TOP
                    dirUV.y = -dirUV.y;
                #endif
                float2 mitad = float2(IN.tamanoPantalla * _ScaledScreenParams.y / _ScaledScreenParams.x,
                                      IN.tamanoPantalla);
                float2 pantallaUV = IN.positionCS.xy / _ScaledScreenParams.xy;
                float2 uvLente = saturate(pantallaUV - dirUV * deflexion * mitad);
                half3 fondo = SampleSceneColor(uvLente);

                // ---------- HORIZONTE DE SUCESOS ----------
                float afuera = smoothstep(_RadioHorizonte * 0.97, _RadioHorizonte * 1.03, r);
                fondo *= afuera;                    // adentro del horizonte no sale luz: negro

                // ---------- DISCO DE ACRECION ----------
                // Un anillo achatado (inclinado) que gira mas rapido cerca del centro
                float incl = max(_Inclinacion, 0.05);
                float2 pd = float2(p.x, p.y / incl);
                float rd = length(pd);

                float interior = _RadioHorizonte * 1.15;
                float anillo = smoothstep(interior, _RadioHorizonte * 1.7, rd)
                             * smoothstep(_RadioDisco, _RadioDisco * 0.55, rd);

                // franjas de gas arremolinado (se muestrea en 2D para que no haya costura angular)
                float ang = atan2(pd.y, pd.x);
                float giro = ang + t * 2.0 / pow(max(rd, 0.05), 1.5);
                float2 pr = rd * float2(cos(giro), sin(giro));
                float n = valueNoise(pr * _EscalaRuido) * 0.65 + valueNoise(pr * _EscalaRuido * 2.7 + 7.3) * 0.35;
                float franjas = lerp(0.3, 1.0, n);

                // un lado del disco viene hacia nosotros y se ve mas brillante
                float doppler = max(0.0, 1.0 + _Doppler * (pd.x / max(rd, 0.001)));

                float rdNorm = saturate((rd - interior) / max(_RadioDisco - interior, 0.001));
                float brillo = pow(1.0 - rdNorm, 1.6) + 0.15;
                half3 colorDisco = lerp(_ColorInterior.rgb, _ColorExterior.rgb, pow(rdNorm, 0.7));

                // la mitad de atras del disco queda oculta detras del agujero; la de adelante no
                float visible = max(afuera, smoothstep(0.03, -0.03, p.y));
                half3 disco = colorDisco * anillo * franjas * doppler * brillo * visible;

                // luz del disco de atras, doblada por la gravedad por encima y por debajo del agujero
                float arco = smoothstep(_RadioHorizonte * 1.05, _RadioHorizonte * 1.2, r)
                           * smoothstep(_RadioHorizonte * 1.9, _RadioHorizonte * 1.25, r);
                float polos = pow(abs(p.y) / max(r, 0.001), 3.0);
                disco += colorDisco * arco * polos * lerp(0.5, 1.0, n) * 0.8;

                // ---------- ANILLO DE FOTONES ----------
                float x = (r - _RadioHorizonte * 1.08) * 38.0;
                half3 foton = _ColorFoton.rgb * exp(-x * x);

                // ---------- RESPLANDOR SUAVE ALREDEDOR ----------
                half3 resplandor = _ColorExterior.rgb * exp(-max(r - _RadioHorizonte, 0.0) * 7.0) * _Resplandor * afuera;

                half3 col = fondo + (disco + foton + resplandor) * _Intensidad;
                // ---------- DESVANECER CERCA DE PAREDES ----------
                // Compara la profundidad de este pixel con la de lo que hay detras: cuanto mas
                // cerca de una superficie (pared, piso), mas se apaga.
                float profEscena = LinearEyeDepth(SampleSceneDepth(pantallaUV), _ZBufferParams);
                float profPixel = IN.positionCS.w;
                float suave = saturate((profEscena - profPixel) / max(_DistanciaSuave, 0.001));

                return half4(col, bordeQuad * suave);
            }
            ENDHLSL
        }
    }
}
