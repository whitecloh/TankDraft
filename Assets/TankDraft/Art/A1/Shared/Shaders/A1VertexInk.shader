Shader "TankDraft/A1/VertexInk"
{
    Properties
    {
        _TeamColor ("Team roof cap", Color) = (0.04,0.38,0.86,1)
        _HitFlash ("Armor hit flash", Range(0,1)) = 0
        _ShadeStrength ("Cel shadow strength", Range(0,1)) = 0.26
        _LightDirection ("Art light direction", Vector) = (-0.45,0.85,0.35,0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Name "A1Ink"
            Cull Back ZWrite On ZTest LEqual
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            float _ShadeStrength;
            float4 _LightDirection;
            UNITY_INSTANCING_BUFFER_START(Team)
                UNITY_DEFINE_INSTANCED_PROP(float4, _TeamColor)
                UNITY_DEFINE_INSTANCED_PROP(float, _HitFlash)
            UNITY_INSTANCING_BUFFER_END(Team)
            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 normal : TEXCOORD0;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v,o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.color = v.color;
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                // Alpha is a team mask, never transparency. Inverted ink shells
                // are already in the mesh, with black RGB and alpha = 1.
                float3 team = UNITY_ACCESS_INSTANCED_PROP(Team, _TeamColor).rgb;
                float3 color = lerp(team, i.color.rgb, saturate(i.color.a));
                float armor = i.color.a * step(0.04, max(i.color.r,max(i.color.g,i.color.b)));
                color = lerp(color, float3(1,1,1), armor * UNITY_ACCESS_INSTANCED_PROP(Team,_HitFlash) * 0.8);
                float light = dot(normalize(i.normal), normalize(_LightDirection.xyz));
                float band = light > 0.55 ? 1.0 : (light > 0.0 ? 0.5 : 0.0);
                return fixed4(color * lerp(1.0 - _ShadeStrength, 1.0, band), 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
