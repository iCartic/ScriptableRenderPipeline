﻿using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityEditor.ShaderAnalysis.Internal
{
    class ShaderAnalysisReport
    {
        int m_UpdateCallRegistered;
        List<IAsyncJob> m_Jobs = new List<IAsyncJob>();
        Dictionary<BuildTarget, IPlatformJobFactory> m_PlatformJobFactories = new Dictionary<BuildTarget, IPlatformJobFactory>();
        public IEnumerable<BuildTarget> SupportedBuildTargets => m_PlatformJobFactories.Keys;

        public void SetPlatformJobs(BuildTarget targetPlatform, IPlatformJobFactory factory)
        {
            m_PlatformJobFactories[targetPlatform] = factory;
        }

        public bool DoesPlatformSupport(BuildTarget targetPlatform, PlatformJob job)
        {
            return m_PlatformJobFactories.ContainsKey(targetPlatform) && m_PlatformJobFactories[targetPlatform].HasCapability(job);
        }

        public IAsyncJob BuildReportAsync(Object asset, BuildTarget targetPlatform)
        {
            if (!DoesPlatformSupport(targetPlatform, PlatformJob.BuildComputeShaderPerfReport))
            {
                Debug.LogWarningFormat("Platform {0} is not supported to build shader reports", targetPlatform);
                return null;
            }

            if (ClearCompletedJobs())
            {
                Debug.LogWarning("A build job is already running");
                return null;
            }

            PackagesUtilities.CreateProjectLocalPackagesSymlinks();

            var factory = m_PlatformJobFactories[targetPlatform];

            IAsyncJob job;
            var shader = asset as Shader;
            var compute = asset as ComputeShader;
            var material = asset as Material;
            if (shader != null)
                job = factory.CreateBuildReportJob(shader);
            else if (compute != null)
                job = factory.CreateBuildReportJob(compute);
            else if (material != null)
                job = factory.CreateBuildReportJob(material);
            else
                throw new ArgumentException("Invalid asset");
            m_Jobs.Add(job);
            RegisterUpdate();
            return job;
        }

        void RegisterUpdate()
        {
            if (m_UpdateCallRegistered == 0)
                EditorApplication.update += Update;

            ++m_UpdateCallRegistered;
        }

        void UnregisterUpdate()
        {
            --m_UpdateCallRegistered;

            if (m_UpdateCallRegistered == 0)
                EditorApplication.update -= Update;
        }

        void Update()
        {
            if (m_Jobs.Count > 0)
            {
                var completed = m_Jobs[0].Tick();
                if (completed)
                    m_Jobs.RemoveAt(0);
            }
        }

        bool ClearCompletedJobs()
        {
            for (var i = m_Jobs.Count - 1; i >= 0; --i)
            {
                if (m_Jobs[i].IsComplete())
                    m_Jobs.RemoveAt(i);
            }
            return m_Jobs.Count > 0;
        }
    }
}
