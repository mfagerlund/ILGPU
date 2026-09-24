// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                           Copyright (c) 2026 ILGPU Project
//                                    www.ilgpu.net
//
// File: CudaGraphCaptureExecutionTests.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPUC.Backends;
using ILGPUC.Tests.Framework;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace ILGPUC.Tests.ExecutionTests;

/// <summary>
/// CUDA-only: graph capture is a CUDA runtime feature
/// (<c>CudaStream.BeginCapture</c> / <c>EndCapture</c>), so these tests are not part of
/// the per-backend matrix in Backends.Generated.tt.
/// </summary>
public sealed class CudaGraphCaptureExecutionTests : ExecutionTestBase
{
    public CudaGraphCaptureExecutionTests(ITestOutputHelper output)
        : base(output, BackendType.Cuda) { }

    [SkippableFact]
    public async Task CaptureRecordsAndReplays()
    {
        await VerifyProgramOutputAsync(
            "TestPrograms/CudaGraph/Capture.cs",
            ["Kernels.IncKernel"],
            ["0", "1", "2", "10", "80"]);
    }
}
