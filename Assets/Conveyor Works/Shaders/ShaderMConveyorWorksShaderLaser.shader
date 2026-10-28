Shader "ShaderMConveyorWorksShaderLaser"
{
    Properties
    {
        _MainTex ("Laser Texture", 2D) = "white" {}
        _Color ("Color Tint", Color) = (1,1,1,1)
        _EmissionColor ("Emission Color", Color) = (1, 0, 0, 1)
        _EmissionPower ("Emission Intensity", Float) = 5.0
        _ScrollSpeed ("Offset X Multiplier", Float) = 1.0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        LOD 200

        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _Color;
            float4 _EmissionColor;
            float _EmissionPower;
            float _ScrollSpeed;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                float scrollX = _MainTex_ST.z * _Time.y * _ScrollSpeed;
                o.uv = TRANSFORM_TEX(v.uv + float2(scrollX, 0), _MainTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 texCol = tex2D(_MainTex, i.uv) * _Color;
                texCol.rgb += _EmissionColor.rgb * _EmissionPower;
                return texCol;
            }
            ENDCG
        }
    }
}
