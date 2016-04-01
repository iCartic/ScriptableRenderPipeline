using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

namespace UnityEditor.Experimental
{
    public class VFXPointOutputShaderGeneratorModule : VFXOutputShaderGeneratorModule
    {
        public override bool UpdateAttributes(Dictionary<VFXAttrib, int> attribs, ref int flags)
        {
            if (!UpdateFlag(attribs, CommonAttrib.Position, VFXContextDesc.Type.kTypeOutput))
                return false;

            UpdateFlag(attribs, CommonAttrib.Color, VFXContextDesc.Type.kTypeOutput);
            UpdateFlag(attribs, CommonAttrib.Alpha, VFXContextDesc.Type.kTypeOutput);
            return true;
        }

        public override void WritePreBlock(ShaderSourceBuilder builder, ShaderMetaData data)
        {
            builder.Write("float3 worldPos = ");
            builder.WriteAttrib(CommonAttrib.Position, data);
            builder.WriteLine(";");
            builder.WriteLine("o.pos = mul (UNITY_MATRIX_VP, float4(worldPos,1.0f));");
        }
    }

    public class VFXBillboardOutputShaderGeneratorModule : VFXOutputShaderGeneratorModule
    {
        public VFXBillboardOutputShaderGeneratorModule(VFXParamValue texture, VFXParamValue flipBookDim, bool orientAlongVelocity)
        {
            m_Texture = texture;
            m_FlipBookDim = flipBookDim;
            m_OrientAlongVelocity = orientAlongVelocity;
        }

        public override int[] GetSingleIndexBuffer(ShaderMetaData data) { return new int[0]; } // tmp

        public override bool UpdateAttributes(Dictionary<VFXAttrib, int> attribs, ref int flags)
        {
            if (!UpdateFlag(attribs, CommonAttrib.Position, VFXContextDesc.Type.kTypeOutput))
                return false;

            UpdateFlag(attribs, CommonAttrib.Color, VFXContextDesc.Type.kTypeOutput);
            UpdateFlag(attribs, CommonAttrib.Alpha, VFXContextDesc.Type.kTypeOutput);
            m_HasSize = UpdateFlag(attribs, CommonAttrib.Size, VFXContextDesc.Type.kTypeOutput);
            m_HasAngle = UpdateFlag(attribs, CommonAttrib.Angle, VFXContextDesc.Type.kTypeOutput);

            if (m_Texture.GetValue<Texture2D>() != null)
            {
                m_HasTexture = true;
                m_HasFlipBook = UpdateFlag(attribs, CommonAttrib.TexIndex, VFXContextDesc.Type.kTypeOutput);   
            }
            
            if (m_OrientAlongVelocity)
                m_OrientAlongVelocity = UpdateFlag(attribs, CommonAttrib.Velocity, VFXContextDesc.Type.kTypeOutput);
           
            return true;
        }

        public override void UpdateUniforms(HashSet<VFXParamValue> uniforms)
        {
            if (m_HasTexture)
            {
                uniforms.Add(m_Texture);
                if (m_HasFlipBook)
                    uniforms.Add(m_FlipBookDim);
            }
        }

        public override void WriteIndex(ShaderSourceBuilder builder, ShaderMetaData data) 
        {
            builder.WriteLine("uint index = (id >> 2) + instanceID * 16384;");
        }

        public override void WriteAdditionalVertexOutput(ShaderSourceBuilder builder, ShaderMetaData data)
        {
            if (m_HasFlipBook)
                builder.WriteLine("float3 offsets : TEXCOORD0; // u,v and index"); 
            else
                builder.WriteLine("float2 offsets : TEXCOORD0;");
        }

        private void WriteRotation(ShaderSourceBuilder builder, ShaderMetaData data)
        {
            builder.WriteLine("float2 sincosA;");
            builder.Write("sincos(radians(");
            builder.WriteAttrib(CommonAttrib.Angle, data);
            builder.Write("), sincosA.x, sincosA.y);");
            builder.WriteLine();
            builder.WriteLine("const float c = sincosA.y;");
            builder.WriteLine("const float s = sincosA.x;");
            builder.WriteLine("const float t = 1.0 - c;");
            builder.WriteLine("const float x = front.x;");
            builder.WriteLine("const float y = front.y;");
            builder.WriteLine("const float z = front.z;");
            builder.WriteLine();
            builder.WriteLine("float3x3 rot = float3x3(t * x * x + c, t * x * y - s * z, t * x * z + s * y,");
            builder.WriteLine("\t\t\t\t\tt * x * y + s * z, t * y * y + c, t * y * z - s * x,");
            builder.WriteLine("\t\t\t\t\tt * x * z - s * y, t * y * z + s * x, t * z * z + c);");
            builder.WriteLine();
        }

        public override void WritePreBlock(ShaderSourceBuilder builder, ShaderMetaData data)
        {
            if (m_HasSize)
            {
                builder.Write("float2 size = ");
                builder.WriteAttrib(CommonAttrib.Size, data);
                builder.WriteLine(" * 0.5f;");
            }
            else
                builder.WriteLine("const float2 size = float2(0.005,0.005);");

            builder.WriteLine("o.offsets.x = 2.0 * float(id & 1) - 1.0;");
            builder.WriteLine("o.offsets.y = 2.0 * float((id & 2) >> 1) - 1.0;");
            builder.WriteLine();

            builder.Write("float3 worldPos = ");
            builder.WriteAttrib(CommonAttrib.Position, data);
            builder.WriteLine(";");
            builder.WriteLine();

            if (m_OrientAlongVelocity)
            {
                builder.WriteLine("float3 front = UnityWorldSpaceViewDir(worldPos);");
                builder.Write("float3 up = normalize(");
                builder.WriteAttrib(CommonAttrib.Velocity, data);
                builder.WriteLine(");");
                builder.WriteLine("float3 side = normalize(cross(front,up));"); 
  
                if (m_HasAngle)
                    builder.WriteLine("front = cross(up,side);");
            }
            else
            {
                if (m_HasAngle)
                    builder.WriteLine("float3 front = UNITY_MATRIX_V[2].xyz;");

                builder.WriteLine("float3 side = UNITY_MATRIX_V[0].xyz;");
                builder.WriteLine("float3 up = UNITY_MATRIX_V[1].xyz;");
            }

            builder.WriteLine();

            if (m_HasAngle)
            {
                WriteRotation(builder, data);
                builder.WriteLine();
                builder.WriteLine("worldPos += mul(rot,side) * (o.offsets.x * size.x);");
                builder.WriteLine("worldPos += mul(rot,up) * (o.offsets.y * size.y);");
            }
            else
            {
                builder.WriteLine("worldPos += side * (o.offsets.x * size.x);");
                builder.WriteLine("worldPos += up * (o.offsets.y * size.y);");
            }

            if (m_HasTexture)
            {
                builder.WriteLine("o.offsets.xy = o.offsets.xy * 0.5 + 0.5;");
                if (m_HasFlipBook)
                {
                    builder.Write("o.offsets.z = ");
                    builder.WriteAttrib(CommonAttrib.TexIndex, data);
                    builder.WriteLine(";");
                }
            }

            builder.WriteLine();

            builder.WriteLine("o.pos = mul (UNITY_MATRIX_VP, float4(worldPos,1.0f));");
        }

        public override void WritePixelShader(VFXSystemModel system,ShaderSourceBuilder builder, ShaderMetaData data)
        {
            if (!m_HasTexture)
            {
                builder.WriteLine("float lsqr = dot(i.offsets, i.offsets);");
                builder.WriteLine("if (lsqr > 1.0)");
                builder.WriteLine("\tdiscard;");
                builder.WriteLine();
            }
            else if (m_HasFlipBook)
            {
                const bool INTERPOLATE = true; // TODO Add a toggle on block

                builder.Write("float2 dim = ");
                builder.Write(data.outputParamToName[m_FlipBookDim]);
                builder.WriteLine(";");
                builder.WriteLine("float2 invDim = 1.0 / dim; // TODO InvDim should be computed on CPU");

                if (!INTERPOLATE)
                {
                    builder.WriteLine("float index = round(i.offsets.z);");
                    builder.WriteLine("float2 tile = float2(fmod(index,dim.x),dim.y - 1.0 - floor(index * invDim.x));");
                    builder.WriteLine("float2 uv = (tile + i.offsets.xy) * invDim; // TODO InvDim should be computed on CPU");                
                    builder.Write("color *= tex2D(");
                    builder.Write(data.outputParamToName[m_Texture]);
                    builder.WriteLine(",uv);");
                }
                else
                {      
                    builder.WriteLine("float ratio = frac(i.offsets.z);");
                    builder.WriteLine();
                    builder.WriteLine("float index1 = i.offsets.z - ratio;");
                    builder.WriteLine("float2 tile1 = float2(fmod(index1,dim.x),dim.y - 1.0 - floor(index1 * invDim.x));");
                    builder.WriteLine("float2 uv1 = (tile1 + i.offsets.xy) * invDim;");
                    builder.Write("float4 col1 = tex2D(");
                    builder.Write(data.outputParamToName[m_Texture]);
                    builder.WriteLine(",uv1);");
                    builder.WriteLine();
                    builder.WriteLine("float index2 = index1 + 1;");
                    builder.WriteLine("float2 tile2 = float2(fmod(index2,dim.x),dim.y - 1.0 - floor(index2 * invDim.x));");
                    builder.WriteLine("float2 uv2 = (tile2 + i.offsets.xy) * invDim;");
                    builder.Write("float4 col2 = tex2D(");
                    builder.Write(data.outputParamToName[m_Texture]);
                    builder.WriteLine(",uv2);");
                    builder.WriteLine();
                    builder.WriteLine("color *= lerp(col1,col2,ratio);");
                }
            }
            else
            {
                builder.Write("color *= tex2D(");
                builder.Write(data.outputParamToName[m_Texture]);
                builder.WriteLine(",i.offsets);");
            }

            if (system.BlendingMode == BlendMode.kMasked)
                builder.WriteLine("if (color.a < 0.33333) discard;");
        }

        protected VFXParamValue m_Texture;
        protected VFXParamValue m_FlipBookDim;

        protected bool m_HasSize;
        protected bool m_HasAngle;
        protected bool m_HasFlipBook;
        protected bool m_HasTexture;
        protected bool m_OrientAlongVelocity;
    }

    public class VFXMorphSubUVOutputShaderGeneratorModule :  VFXBillboardOutputShaderGeneratorModule
    {
        public VFXParamValue m_morphTexture;
        public VFXParamValue m_morphIntensity;

        public VFXMorphSubUVOutputShaderGeneratorModule(VFXParamValue texture, VFXParamValue morphTexture, VFXParamValue morphIntensity ,VFXParamValue flipBookDim, bool orientAlongVelocity) : base(texture, flipBookDim, orientAlongVelocity)
        {
            m_morphTexture = morphTexture;
            m_morphIntensity = morphIntensity;
        }

        public override void UpdateUniforms(HashSet<VFXParamValue> uniforms)
        {
            base.UpdateUniforms(uniforms);
            uniforms.Add(m_morphTexture);
            uniforms.Add(m_morphIntensity);
        }

        public override void WritePixelShader(VFXSystemModel system,ShaderSourceBuilder builder, ShaderMetaData data)
        {
            if (!m_HasTexture)
            {
                builder.WriteLine("float lsqr = dot(i.offsets, i.offsets);");
                builder.WriteLine("if (lsqr > 1.0)");
                builder.WriteLine("\tdiscard;");
                builder.WriteLine();
            }
            else if (m_HasFlipBook)
            {
                builder.Write("float morphIntensity = ");
                builder.Write(data.outputParamToName[m_morphIntensity]);
                builder.WriteLine(";");

                builder.Write("sampler2D morphSampler = ");
                builder.Write(data.outputParamToName[m_morphTexture]);
                builder.WriteLine(";");

                builder.Write("sampler2D colorSampler = ");
                builder.Write(data.outputParamToName[m_Texture]);
                builder.WriteLine(";");

                builder.Write("float2 dim = ");
                builder.Write(data.outputParamToName[m_FlipBookDim]);
                builder.WriteLine(";");
                builder.WriteLine("float2 invDim = 1.0 / dim; // TODO InvDim should be computed on CPU");

                builder.WriteLine("float numFrames = dim.x * dim.y;");
                builder.WriteLine("float t = i.offsets.z;");
                builder.WriteLine();
                builder.WriteLine("float2 frameSize = 1.0f/float2(dim.x,dim.y);");
                builder.WriteLine();
                builder.WriteLine("float blend = frac(t);");
                builder.WriteLine("float2 frameA = (i.offsets.xy + float2(floor(t) % dim.x, (dim.y-1)-floor(floor(t) / dim.x))) * frameSize;");
                builder.WriteLine("float2 frameB = (i.offsets.xy + float2(ceil(t) % dim.x, (dim.y-1)-floor(ceil(t) / dim.x))) * frameSize;");
                builder.WriteLine();
                builder.WriteLine("float2 morphA = tex2D(morphSampler, frameA).rg - 0.5f;");
                builder.WriteLine("float2 morphB = tex2D(morphSampler, frameB).rg - 0.5f;");
                builder.WriteLine();
                builder.WriteLine("morphA *= -morphIntensity * blend;");
                builder.WriteLine("morphB *= -morphIntensity * (blend - 1.0f);");
                builder.WriteLine();
                builder.WriteLine("float4 colorA = tex2D(colorSampler, frameA + morphA);");
                builder.WriteLine("float4 colorB = tex2D(colorSampler, frameB + morphB);");
                builder.WriteLine();
                builder.WriteLine("color *= lerp(colorA, colorB, blend);");

            }

            if (system.BlendingMode == BlendMode.kMasked)
                builder.WriteLine("if (color.a < 0.33333) discard;");
        }
    }
}
