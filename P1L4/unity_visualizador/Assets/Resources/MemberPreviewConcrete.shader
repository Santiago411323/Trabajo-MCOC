Shader "MCOC/MemberPreviewConcrete"
{
    Properties { _Color ("Tint", Color) = (1,1,1,1) }
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
            struct Input { float4 vertex : POSITION; float4 color : COLOR; };
            struct Output { float4 position : SV_POSITION; float4 color : COLOR; };
            float4 _Color;
            Output vert(Input v) { Output o; o.position=UnityObjectToClipPos(v.vertex); o.color=v.color*_Color; return o; }
            fixed4 frag(Output i) : SV_Target { return i.color; }
            ENDCG
        }
    }
}
