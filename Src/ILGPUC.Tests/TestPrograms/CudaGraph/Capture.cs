// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                           Copyright (c) 2026 ILGPU Project
//                                    www.ilgpu.net
//
// File: Capture.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------


// disable: max_line_length
// Test program: CUDA graph capture
// A stream capture records submitted work without executing it; each launch of the
// instantiated graph replays the whole recorded sequence exactly once.
// Expected output (one value per line):
//   0   buffer after capture, before any launch (capture must not execute)
//   1   after 1 launch of a single-kernel graph
//   2   after 2 launches
//   10  after 8 more launches (the graph is reusable)
//   80  multi-kernel graph (16 kernels) launched 5 times

using System;
using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.Cuda;

static class Kernels
{
    public static void IncKernel(
        Index1D index,
        ArrayView1D<int, Stride1D.Dense> data)
    {
        data[index] += 1;
    }
}

static class Program
{
    const int Length = 1024;

    static void Main()
    {
        using var context = Context.Create(b => b.Cuda());
        using var accelerator = context.CreateCudaAccelerator(0);
        using var stream = (CudaStream)accelerator.CreateStream();

        SingleKernelGraph(stream);
        MultiKernelGraph(stream);
    }

    static int First(MemoryBuffer1D<int, Stride1D.Dense> buffer, int expectedAll)
    {
        var data = buffer.GetAsArray1D();
        foreach (var v in data)
        {
            if (v != expectedAll)
                throw new InvalidOperationException(
                    $"Non-uniform buffer: expected {expectedAll}, found {v}");
        }
        return data[0];
    }

    static void SingleKernelGraph(CudaStream stream)
    {
        using var buffer = stream.Allocate1D<int>(Length);
        var view = buffer.View;

        // Warm up (module load / finalization) before capturing.
        stream.Launch((Index1D)Length, index => Kernels.IncKernel(index, view));
        buffer.View.MemSetToZero(stream);
        stream.Synchronize();

        stream.BeginCapture();
        stream.Launch((Index1D)Length, index => Kernels.IncKernel(index, view));
        using var graph = stream.EndCapture();

        Console.WriteLine(First(buffer, 0));

        using var exec = graph.Instantiate();

        exec.Launch(stream);
        stream.Synchronize();
        Console.WriteLine(First(buffer, 1));

        exec.Launch(stream);
        stream.Synchronize();
        Console.WriteLine(First(buffer, 2));

        for (int i = 0; i < 8; i++)
            exec.Launch(stream);
        stream.Synchronize();
        Console.WriteLine(First(buffer, 10));
    }

    static void MultiKernelGraph(CudaStream stream)
    {
        const int kernelsPerGraph = 16;
        const int replays = 5;

        using var buffer = stream.Allocate1D<int>(Length);
        var view = buffer.View;

        stream.Launch((Index1D)Length, index => Kernels.IncKernel(index, view));
        buffer.View.MemSetToZero(stream);
        stream.Synchronize();

        stream.BeginCapture();
        for (int i = 0; i < kernelsPerGraph; i++)
            stream.Launch((Index1D)Length, index => Kernels.IncKernel(index, view));
        using var graph = stream.EndCapture();
        using var exec = graph.Instantiate();

        for (int i = 0; i < replays; i++)
            exec.Launch(stream);
        stream.Synchronize();
        Console.WriteLine(First(buffer, kernelsPerGraph * replays));
    }
}
