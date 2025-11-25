Shader "Custom/CameraAdjustment"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Brightness ("Brightness", Range(0.5, 2.0)) = 1.0
        _Contrast ("Contrast", Range(0.5, 2.0)) = 1.0
        _Gamma ("Gamma", Range(0.5, 2.0)) = 1.0
    }
    
    SubShader
    {
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Brightness;
            float _Contrast;
            float _Gamma;

            fixed4 frag (v2f_img i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv);
                
                // Apply brightness
                col.rgb *= _Brightness;
                
                // Apply contrast
                col.rgb = (col.rgb - 0.5) * _Contrast + 0.5;
                
                // Apply gamma
                col.rgb = pow(col.rgb, 1.0 / _Gamma);
                
                return col;
            }
            ENDCG
        }
    }
}