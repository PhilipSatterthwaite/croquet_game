// Grass blades: real geometry, lit once when the mesh is built and carried in
// the vertex colours.
//
// Unlit on purpose. Every other thing on this court is shaded by hand against
// Shapes.Light -- balls, posts, pegs, the turf under them -- and a lawn lit by
// a different mechanism from the things standing on it reads as a collage
// however good each half is. Baking the same lighting into the vertices keeps
// one sun over the whole game and costs nothing per frame.
//
// ZWrite is the point of having a shader at all rather than using a sprite
// material: blades stand up out of the ground towards the camera and cross in
// front of each other, and without a depth buffer the one drawn last wins
// regardless of which is nearer. From directly overhead that is survivable;
// from any other angle it falls apart, and the whole reason for building real
// geometry is that it should not.
Shader "Croquet/Blade"
{
    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "Blade"

            // A renderer only draws passes it recognises, and it fails SILENTLY
            // when it does not: the mesh is built, placed, reported visible, and
            // never rendered, which looks exactly like geometry that failed to
            // generate. Both tags are here because the two renderers want
            // different ones -- Universal2D for the 2D renderer, and
            // UniversalForward for the ordinary one.
            Tags { "LightMode" = "UniversalForward" }

            // Two-sided: a blade is a ribbon and you see the back of it as
            // often as the front.
            Cull Off
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 colour : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                half4 colour : COLOR;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.colour = IN.colour;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                return half4(IN.colour.rgb, 1);
            }

            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Unlit"
}
