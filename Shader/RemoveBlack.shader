Shader "UI/RemoveBlack"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Threshold ("Black Threshold", Range(0,1)) = 0.1
        _EdgeFade ("Edge Fade Width", Range(0,0.5)) = 0.1
        _TileWidth ("Tile Width", Float) = 1.0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float2 rawUV : TEXCOORD1;
                float4 color : COLOR;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _Threshold;
            float _EdgeFade;
            float _TileWidth;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.rawUV = v.uv;
                o.color = v.color;
                return o;
            }

            fixed4 SampleTile(sampler2D tex, float localU, float tileWidth, float uvY, float4 vertColor, float threshold)
            {
                float2 sampleUV = float2(localU / tileWidth, uvY); // 用实际的 uvY
                fixed4 col = tex2D(tex, sampleUV) * vertColor;
                float brightness = max(col.r, max(col.g, col.b));
                col.a = smoothstep(0, threshold, brightness);
                return col;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float tileLocalU = frac(i.uv.x);
                fixed4 finalCol = fixed4(0, 0, 0, 0);

                // 当前 tile
                if (tileLocalU < _TileWidth)
                {
                    fixed4 col = SampleTile(_MainTex, tileLocalU, _TileWidth, i.uv.y, i.color, _Threshold);
                    if (col.a > finalCol.a) finalCol = col;
                }

                // 前一个 tile 溢出部分
                float prevLocalU = tileLocalU + 1.0;
                if (prevLocalU < _TileWidth)
                {
                    fixed4 col = SampleTile(_MainTex, prevLocalU, _TileWidth, i.uv.y, i.color, _Threshold);
                    if (col.a > finalCol.a) finalCol = col;
                }

                // 左右边缘淡出
                float leftFade  = smoothstep(0.0, _EdgeFade, i.rawUV.x);
                float rightFade = smoothstep(1.0, 1.0 - _EdgeFade, i.rawUV.x);
                finalCol.a *= leftFade * rightFade;

                return finalCol;
            }
            ENDCG
        }
    }
}