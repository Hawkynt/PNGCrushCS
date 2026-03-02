using System;
using System.IO;

namespace PngOptimizer;

public sealed partial class PngOptimizer {
  /// <summary>A MemoryStream wrapper with pooling-friendly initial capacity</summary>
  private sealed class PooledMemoryStream(int initialCapacity) : IDisposable {
    public MemoryStream Stream { get; } = new(initialCapacity);

    public void Dispose() => this.Stream.Dispose();

    public Span<byte> AsSpan() => this.Stream.GetBuffer().AsSpan(0, (int)this.Stream.Position);

  }
}
