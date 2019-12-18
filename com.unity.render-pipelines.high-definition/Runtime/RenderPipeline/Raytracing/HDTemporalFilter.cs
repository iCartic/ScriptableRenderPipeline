using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace UnityEngine.Experimental.Rendering.HighDefinition
{
    class HDTemporalFilter
    {
        // Resources used for the denoiser
        ComputeShader m_TemporalFilterCS;

        // Required for fetching depth and normal buffers
        SharedRTManager m_SharedRTManager;
        HDRenderPipeline m_RenderPipeline;

        public HDTemporalFilter()
        {
        }

        public void Init(HDRenderPipelineRayTracingResources rpRTResources, SharedRTManager sharedRTManager, HDRenderPipeline renderPipeline)
        {   
            // Keep track of the resources
            m_TemporalFilterCS = rpRTResources.temporalFilterCS;

            // Keep track of the shared rt manager
            m_SharedRTManager = sharedRTManager;
            m_RenderPipeline = renderPipeline;
        }

        public void Release()
        {
        }

        public void DenoiseBuffer(CommandBuffer cmd, HDCameraInfo hdCamera,
            RTHandle noisySignal, RTHandle historySignal,
            RTHandle outputSignal, 
            bool singleChannel = true, int slotIndex = -1, float historyValidity = 1.0f)
        {
            // If we do not have a depth and normal history buffers, we can skip right away
            var historyDepthBuffer = hdCamera.GetCurrentFrameRT((int)HDCameraFrameHistoryType.Depth);
            var historyNormalBuffer = hdCamera.GetCurrentFrameRT((int)HDCameraFrameHistoryType.Normal);
            if (historyDepthBuffer == null || historyNormalBuffer == null)
            {
                HDUtils.BlitCameraTexture(cmd, noisySignal, historySignal);
                HDUtils.BlitCameraTexture(cmd, noisySignal, outputSignal);
                return;
            }

            // Fetch texture dimensions
            int texWidth = hdCamera.actualWidth;
            int texHeight = hdCamera.actualHeight;

            // Evaluate the dispatch parameters
            int areaTileSize = 8;
            int numTilesX = (texWidth + (areaTileSize - 1)) / areaTileSize;
            int numTilesY = (texHeight + (areaTileSize - 1)) / areaTileSize;

            // Request the intermediate buffer we need
            RTHandle validationBuffer = m_RenderPipeline.GetRayTracingBuffer(InternalRayTracingBuffers.R0);

            // First of all we need to validate the history to know where we can or cannot use the history signal
            int m_KernelFilter = m_TemporalFilterCS.FindKernel("ValidateHistory");
            var historyScale = new Vector2(hdCamera.actualWidth / (float)historySignal.rt.width, hdCamera.actualHeight / (float)historySignal.rt.height);
            cmd.SetComputeVectorParam(m_TemporalFilterCS, HDShaderIDs._RTHandleScaleHistory, historyScale);
            cmd.SetComputeTextureParam(m_TemporalFilterCS, m_KernelFilter, HDShaderIDs._DepthTexture, m_SharedRTManager.GetDepthStencilBuffer());
            cmd.SetComputeTextureParam(m_TemporalFilterCS, m_KernelFilter, HDShaderIDs._HistoryDepthTexture, historyDepthBuffer);
            cmd.SetComputeTextureParam(m_TemporalFilterCS, m_KernelFilter, HDShaderIDs._NormalBufferTexture, m_SharedRTManager.GetNormalBuffer());
            cmd.SetComputeTextureParam(m_TemporalFilterCS, m_KernelFilter, HDShaderIDs._HistoryNormalBufferTexture, historyNormalBuffer);
            cmd.SetComputeTextureParam(m_TemporalFilterCS, m_KernelFilter, HDShaderIDs._ValidationBufferRW, validationBuffer);
            cmd.SetComputeFloatParam(m_TemporalFilterCS, HDShaderIDs._HistoryValidity, historyValidity);
            cmd.SetComputeFloatParam(m_TemporalFilterCS, HDShaderIDs._PixelSpreadAngleTangent, HDRenderPipeline.GetPixelSpreadTangent(hdCamera.camera.fieldOfView, hdCamera.actualWidth, hdCamera.actualHeight));
            cmd.DispatchCompute(m_TemporalFilterCS, m_KernelFilter, numTilesX, numTilesY, hdCamera.viewCount);

            // Should we use the history array shader variants
            bool historyIsArray = slotIndex != -1;

            // Now that we have validated our history, let's accumulate
            m_KernelFilter = m_TemporalFilterCS.FindKernel(singleChannel ? (historyIsArray ? "TemporalAccumulationSingleArray" : "TemporalAccumulationSingle") : (historyIsArray ? "TemporalAccumulationColorArray" : "TemporalAccumulationColor"));
            cmd.SetComputeTextureParam(m_TemporalFilterCS, m_KernelFilter, HDShaderIDs._DenoiseInputTexture, noisySignal);
            cmd.SetComputeTextureParam(m_TemporalFilterCS, m_KernelFilter, HDShaderIDs._HistoryBuffer, historySignal);
            cmd.SetComputeTextureParam(m_TemporalFilterCS, m_KernelFilter, HDShaderIDs._DepthTexture, m_SharedRTManager.GetDepthStencilBuffer());
            cmd.SetComputeTextureParam(m_TemporalFilterCS, m_KernelFilter, HDShaderIDs._DenoiseOutputTextureRW, outputSignal);
            cmd.SetComputeTextureParam(m_TemporalFilterCS, m_KernelFilter, HDShaderIDs._ValidationBuffer, validationBuffer);
            cmd.SetComputeIntParam(m_TemporalFilterCS, HDShaderIDs._DenoisingHistorySlot, slotIndex);
            cmd.DispatchCompute(m_TemporalFilterCS, m_KernelFilter, numTilesX, numTilesY, hdCamera.viewCount);

            m_KernelFilter = m_TemporalFilterCS.FindKernel(singleChannel ? (historyIsArray ? "CopyHistorySingleArray" : "CopyHistorySingle") : (historyIsArray ? "CopyHistoryColorArray" : "CopyHistoryColor"));
            cmd.SetComputeTextureParam(m_TemporalFilterCS, m_KernelFilter, HDShaderIDs._DenoiseInputTexture, outputSignal);
            cmd.SetComputeTextureParam(m_TemporalFilterCS, m_KernelFilter, HDShaderIDs._DenoiseOutputTextureRW, historySignal);
            cmd.SetComputeIntParam(m_TemporalFilterCS, HDShaderIDs._DenoisingHistorySlot, slotIndex);
            cmd.DispatchCompute(m_TemporalFilterCS, m_KernelFilter, numTilesX, numTilesY, hdCamera.viewCount);
        }
    }
}
