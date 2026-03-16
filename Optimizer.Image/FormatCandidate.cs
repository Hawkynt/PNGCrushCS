using System;
using System.Threading;
using System.Threading.Tasks;
using Crush.Core;

namespace Optimizer.Image;

/// <summary>A candidate format for optimization.</summary>
internal readonly record struct FormatCandidate(
  ImageFormat Format,
  string Extension,
  Func<CancellationToken, IProgress<OptimizationProgress>?, ValueTask<byte[]?>> Optimize
);
