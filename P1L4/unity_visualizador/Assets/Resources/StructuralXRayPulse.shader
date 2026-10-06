Shader "MCOC/StructuralXRayPulse"
{
    Properties { _Color("Color",Color)=(.2,.8,1,1) _Pulse("Pulse",Float)=0 _Dashed("Connection",Float)=0 _Stage("Stage",Float)=1 }
    SubShader
    {
        Tags { "Queue"="Transparent+45" "RenderType"="Transparent" }
        Pass
        {
            ZWrite Off ZTest Always Cull Off Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Color; float _Pulse,_Dashed,_Stage;
            struct Input { float4 vertex:POSITION;float2 uv:TEXCOORD0;fixed4 color:COLOR; };
            struct Output { float4 position:SV_POSITION;float2 uv:TEXCOORD0;fixed4 color:COLOR; };
            Output vert(Input v){Output o;o.position=UnityObjectToClipPos(v.vertex);o.uv=v.uv;o.color=v.color;return o;}
            fixed4 frag(Output i):SV_Target
            {
                float distance=abs(i.uv.x-_Pulse);float band=1-smoothstep(.015,.13,distance);
                float dashed=lerp(1,step(.35,frac(i.uv.x*18)),_Dashed);
                fixed4 color=i.color*_Color;color.rgb*=.72+band*.55;color.a*=dashed*_Stage*(.65+.35*band);return color;
            }
            ENDCG
        }
    }
}
