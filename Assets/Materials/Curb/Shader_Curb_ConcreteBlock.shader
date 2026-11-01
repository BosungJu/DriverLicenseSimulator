Shader "DriverLicenseSimulator/Curb Concrete Blocks"
{
	Properties
	{
		[MainColor] _BaseColor("Concrete Color", Color) = (0.55, 0.56, 0.57, 1)
		_JointColor("Joint Color", Color) = (0.25, 0.26, 0.27, 1)
		_BlockSize("Block Size (meters)", Vector) = (0.6, 0.15, 0, 0)
		_JointWidth("Joint Width (meters)", Range(0.002, 0.03)) = 0.008
		_BevelWidth("Edge Shade Width (meters)", Range(0.001, 0.02)) = 0.007
		_Variation("Block Color Variation", Range(0, 0.25)) = 0.06
		_Grain("Concrete Grain", Range(0, 0.15)) = 0.035
		_Smoothness("Smoothness", Range(0, 1)) = 0.18
	}
	SubShader
	{
		Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }
		Cull Back
		HLSLINCLUDE
		#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
		#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
		#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"

		CBUFFER_START(UnityPerMaterial)
			half4 _BaseColor;
			half4 _JointColor;
			float4 _BlockSize;
			float _JointWidth;
			float _BevelWidth;
			float _Variation;
			float _Grain;
			half _Smoothness;
		CBUFFER_END

		struct Attributes
		{
			float4 positionOS : POSITION;
			float3 normalOS : NORMAL;
			float2 uvMeters : TEXCOORD0;
			UNITY_VERTEX_INPUT_INSTANCE_ID
		};
		struct Varyings
		{
			float4 positionCS : SV_POSITION;
			float3 positionWS : TEXCOORD0;
			half3 normalWS : TEXCOORD1;
			half fogFactor : TEXCOORD2;
			float2 uvMeters : TEXCOORD3;
			UNITY_VERTEX_INPUT_INSTANCE_ID
			UNITY_VERTEX_OUTPUT_STEREO
		};

		Varyings Vert(Attributes input)
		{
			Varyings output = (Varyings)0;
			UNITY_SETUP_INSTANCE_ID(input);
			UNITY_TRANSFER_INSTANCE_ID(input, output);
			UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
			output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
			output.positionCS = TransformWorldToHClip(output.positionWS);
			output.normalWS = TransformObjectToWorldNormal(input.normalOS);
			output.uvMeters = input.uvMeters;
			output.fogFactor = ComputeFogFactor(output.positionCS.z);
			return output;
		}

		float Hash(float2 cell)
		{
			float3 mixed = frac(float3(cell.xyx) * 0.1031);
			mixed += dot(mixed, mixed.yzx + 33.33);
			return frac((mixed.x + mixed.y) * mixed.z);
		}

		half3 BlockColor(float2 position)
		{
			// Mesh UVs are meters along the curb and height above its local road surface.

			float2 size = max(_BlockSize.xy, float2(0.02, 0.02));
			float2 grid = position / size;
			grid.x += frac(floor(grid.y) * 0.5);
			float2 cell = floor(grid);
			float2 local = frac(grid);
			float2 edgeDistance = min(local, 1.0 - local) * size;
			float distance = min(edgeDistance.x, edgeDistance.y);
			float joint = min(_JointWidth * 0.5, min(size.x, size.y) * 0.2);
			float filterWidth = max(max(fwidth(position.x), fwidth(position.y)), 0.0002);
			float interior = smoothstep(joint - filterWidth, joint + filterWidth, distance);
			float bevel = smoothstep(joint, joint + _BevelWidth, distance);
			float variation = (Hash(cell) * 2.0 - 1.0) * _Variation;
			float grain = (Hash(floor(position * 220.0)) * 2.0 - 1.0) * _Grain * saturate(1.0 - filterWidth * 220.0);
			half3 concrete = _BaseColor.rgb * (1.0 + variation + grain) * lerp(0.84, 1.0, bevel);
			return lerp(_JointColor.rgb, concrete, interior);
		}
		ENDHLSL

		Pass
		{
			Name "ForwardLit"
			Tags { "LightMode" = "UniversalForwardOnly" }
			HLSLPROGRAM
			#pragma target 3.0
			#pragma vertex Vert
			#pragma fragment Frag
			#pragma multi_compile_instancing
			#pragma multi_compile_fog
			#pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
			#pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
			#pragma multi_compile _ _FORWARD_PLUS
			#pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
			#pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
			#pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

			half4 Frag(Varyings input) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
				InputData lighting = (InputData)0;
				lighting.positionWS = input.positionWS;
				lighting.positionCS = input.positionCS;
				lighting.normalWS = NormalizeNormalPerPixel(input.normalWS);
				lighting.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
				lighting.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
				lighting.fogCoord = input.fogFactor;
				lighting.bakedGI = SampleSH(lighting.normalWS);
				lighting.vertexLighting = VertexLighting(input.positionWS, lighting.normalWS);
				lighting.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
				lighting.shadowMask = half4(1, 1, 1, 1);
				SurfaceData surface = (SurfaceData)0;
				surface.albedo = BlockColor(input.uvMeters);
				surface.alpha = 1;
				surface.smoothness = _Smoothness;
				surface.occlusion = 1;
				surface.normalTS = half3(0, 0, 1);
				half4 color = UniversalFragmentPBR(lighting, surface);
				color.rgb = MixFog(color.rgb, input.fogFactor);
				return color;
			}
			ENDHLSL
		}

		Pass
		{
			Name "ShadowCaster"
			Tags { "LightMode" = "ShadowCaster" }
			ZWrite On
			ZTest LEqual
			ColorMask 0
			HLSLPROGRAM
			#pragma target 3.0
			#pragma vertex ShadowVert
			#pragma fragment DepthFrag
			#pragma multi_compile_instancing
			#pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
			float3 _LightDirection;
			float3 _LightPosition;
			Varyings ShadowVert(Attributes input)
			{
				Varyings output = Vert(input);
				UNITY_SETUP_INSTANCE_ID(input);
				float3 normal = TransformObjectToWorldNormal(input.normalOS);
				#if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
				float3 lightDirection = normalize(_LightPosition - output.positionWS);
				#else
				float3 lightDirection = _LightDirection;
				#endif
				output.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(output.positionWS, normal, lightDirection)));
				return output;
			}
			half4 DepthFrag(Varyings input) : SV_Target { return 0; }
			ENDHLSL
		}

		Pass
		{
			Name "DepthNormals"
			Tags { "LightMode" = "DepthNormalsOnly" }
			ZWrite On
			HLSLPROGRAM
			#pragma target 3.0
			#pragma vertex Vert
			#pragma fragment DepthNormalsFrag
			#pragma multi_compile_instancing
			#pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
			half4 DepthNormalsFrag(Varyings input) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
				half3 normal = NormalizeNormalPerPixel(input.normalWS);
				#if defined(_GBUFFER_NORMALS_OCT)
				float2 packedNormal = PackNormalOctQuadEncode(normal) * 0.5 + 0.5;
				return half4(PackFloat2To888(saturate(packedNormal)), 0);
				#else
				return half4(normal, 0);
				#endif
			}
			ENDHLSL
		}

		Pass
		{
			Name "DepthOnly"
			Tags { "LightMode" = "DepthOnly" }
			ZWrite On
			ColorMask 0
			HLSLPROGRAM
			#pragma target 3.0
			#pragma vertex Vert
			#pragma fragment DepthFrag
			#pragma multi_compile_instancing
			half4 DepthFrag(Varyings input) : SV_Target { return 0; }
			ENDHLSL
		}
	}
	Fallback "Hidden/Universal Render Pipeline/FallbackError"
}
