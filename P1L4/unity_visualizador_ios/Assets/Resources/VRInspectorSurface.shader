Shader "MCOC/VR Inspector Surface"
{
    Properties { _MainTex("Image",2D)="white" {} _Color("Color",Color)=(1,1,1,1) }
    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Opaque" }
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            sampler2D _MainTex;fixed4 _Color;
            v2f vert(appdata v){v2f o;UNITY_SETUP_INSTANCE_ID(v);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);o.pos=UnityObjectToClipPos(v.vertex);o.uv=v.uv;return o;}
            fixed4 frag(v2f i):SV_Target{UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);return fixed4((tex2D(_MainTex,i.uv)*_Color).rgb,1);}
            ENDCG
        }
    }
}
