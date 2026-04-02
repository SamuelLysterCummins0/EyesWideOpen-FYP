Shader "Custom/GoggleVision"
{
    Properties
    {
        _MainTex      ("Screen Texture", 2D)            = "white" {}
        _PurpleColor  ("Purple Tint",    Color)         = (0.45, 0.0, 0.65, 1.0)
        _Contrast     ("Contrast",       Range(0.5, 5)) = 2.8
        _Brightness   ("Brightness",     Range(-1, 1))  = -0.05
        _Saturation   ("Desaturation",   Range(0, 1))   = 1.0
    }

    SubShader
    {
        // Standard image-effect pass: no depth test, no backface cull, no z-write
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex   vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            fixed4    _PurpleColor;
            float     _Contrast;
            float     _Brightness;
            float     _Saturation;

            fixed4 frag(v2f_img i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv);

                // Luminance (perceptual weights)
                float lum = dot(col.rgb, fixed3(0.299, 0.587, 0.114));

                // Blend toward full greyscale based on _Saturation
                float3 grey = lerp(col.rgb, float3(lum, lum, lum), _Saturation);

                // Contrast + brightness remap — pushes dark areas to black,
                // keeps bright areas (emissive crack overlays) as bright spikes
                float boosted = saturate((lum + _Brightness) * _Contrast);

                // Tint with the purple colour
                fixed3 tinted = boosted * _PurpleColor.rgb;

                return fixed4(tinted, 1.0);
            }
            ENDCG
        }
    }
}
