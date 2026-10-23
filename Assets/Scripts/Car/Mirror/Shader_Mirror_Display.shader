Shader "DriverLicenseSimulator/Mirror Display"
{
	Properties
	{
		[MainTexture] _BaseMap("Camera Feed", 2D) = "white" {}
		[MainColor] _BaseColor("Color", Color) = (1, 1, 1, 1)
		[HideInInspector] _UsePlanarUV("Use Planar UV", Float) = 0
		[HideInInspector] _MirrorUvMin("UV Bounds Minimum", Vector) = (0, 0, 0, 0)
		[HideInInspector] _MirrorUvSize("UV Bounds Size", Vector) = (1, 1, 1, 0)
		[HideInInspector] _MirrorUvHorizontal("UV Horizontal Axis", Vector) = (1, 0, 0, 0)
		[HideInInspector] _MirrorUvVertical("UV Vertical Axis", Vector) = (0, 1, 0, 0)
	}
	SubShader
	{
		// Planar coordinates require the original mesh-local positions.
		Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" "DisableBatching" = "True" }
		Pass
		{
			Tags { "LightMode" = "SRPDefaultUnlit" }
			Cull Off
			ZWrite On
			HLSLPROGRAM
			#pragma vertex Vert
			#pragma fragment Frag
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			TEXTURE2D(_BaseMap);
			SAMPLER(sampler_BaseMap);
			CBUFFER_START(UnityPerMaterial)
				float4 _BaseMap_ST;
				half4 _BaseColor;
				float _UsePlanarUV;
				float4 _MirrorUvMin;
				float4 _MirrorUvSize;
				float4 _MirrorUvHorizontal;
				float4 _MirrorUvVertical;
			CBUFFER_END

			struct Attributes
			{
				float4 positionOS : POSITION;
				float2 uv : TEXCOORD0;
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				float2 uv : TEXCOORD0;
			};

			Varyings Vert(Attributes input)
			{
				Varyings output;
				output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
				float2 uv = input.uv;
				if (_UsePlanarUV > 0.5)
				{
					float3 relativePosition = input.positionOS.xyz - _MirrorUvMin.xyz;
					// Clamp degenerate bounds to prevent division by zero.
					const float MinimumUvExtent = 0.00001;
					uv.x = dot(relativePosition, _MirrorUvHorizontal.xyz) / max(dot(_MirrorUvSize.xyz, _MirrorUvHorizontal.xyz), MinimumUvExtent);
					uv.y = dot(relativePosition, _MirrorUvVertical.xyz) / max(dot(_MirrorUvSize.xyz, _MirrorUvVertical.xyz), MinimumUvExtent);
				}
				output.uv = uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
				return output;
			}

			half4 Frag(Varyings input) : SV_Target
			{
				return SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
			}
			ENDHLSL
		}
	}
}
