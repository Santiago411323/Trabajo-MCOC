Shader "MCOC/LrfdClouds"
{
    Properties { _Color("Cloud tint",Color)=(0.6,0.65,0.7,0.9) }
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
            float4 _Color;
            struct Input { float4 vertex:POSITION; float3 normal:NORMAL; };
            struct Output { float4 position:SV_POSITION; float3 normal:TEXCOORD0; float3 world:TEXCOORD1; };
            Output vert(Input v) { Output o;o.position=UnityObjectToClipPos(v.vertex);o.normal=UnityObjectToWorldNormal(v.normal);o.world=mul(unity_ObjectToWorld,v.vertex).xyz;return o; }
            fixed4 frag(Output i):SV_Target
            {
                float3 normal=normalize(i.normal);
                float light=0.6+0.4*saturate(dot(normal,normalize(float3(0.3,0.85,0.25))));
                float edge=pow(saturate(abs(dot(normal,normalize(_WorldSpaceCameraPos-i.world)))),0.35);
                return fixed4(_Color.rgb*light,_Color.a*edge);
            }
            ENDCG
        }
    }
}
