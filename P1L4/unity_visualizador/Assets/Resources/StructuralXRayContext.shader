Shader "MCOC/StructuralXRayContext"
{
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            ZWrite Off ZTest LEqual Cull Off Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct Input { float4 vertex:POSITION;fixed4 color:COLOR; };
            struct Output { float4 position:SV_POSITION;fixed4 color:COLOR; };
            Output vert(Input v){Output o;o.position=UnityObjectToClipPos(v.vertex);o.color=v.color;return o;}
            fixed4 frag(Output i):SV_Target{return i.color;}
            ENDCG
        }
    }
}
