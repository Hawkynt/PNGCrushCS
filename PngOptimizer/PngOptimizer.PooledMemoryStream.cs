using System;
using System.Buffers;
using System.IO;

namespace PngOptimizer;

public sealed partial class PngOptimizer {
  /// <summary>Ein MemoryStream, der Array-Pooling verwendet</summary>
  private readonly struct PooledMemoryStream :IDisposable {
    
    private readonly byte[] _rentedBuffer;

    public PooledMemoryStream(int initialCapacity) {
      this._rentedBuffer = ArrayPool<byte>.Shared.Rent(initialCapacity);
      this.Stream = new(this._rentedBuffer);
    }

    public  MemoryStream Stream { get; } 
    
    public Span<byte> AsSpan()=>this._rentedBuffer.AsSpan(0, (int)this.Stream.Position);

    #region IDisposable

    public void Dispose() {
      this.Stream.Dispose();
      ArrayPool<byte>.Shared.Return(this._rentedBuffer);
    }

    #endregion
  }
}
