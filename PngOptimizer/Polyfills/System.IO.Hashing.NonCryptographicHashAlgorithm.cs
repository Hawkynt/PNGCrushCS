﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace System.IO.Hashing;

/// <summary>
///   Represents a non-cryptographic hash algorithm.
/// </summary>
public abstract class NonCryptographicHashAlgorithm {
  /// <summary>
  ///   Gets the number of bytes produced from this hash algorithm.
  /// </summary>
  /// <value>The number of bytes produced from this hash algorithm.</value>
  public int HashLengthInBytes { get; }

  /// <summary>
  ///   Called from constructors in derived classes to initialize the
  ///   <see cref="NonCryptographicHashAlgorithm"/> class.
  /// </summary>
  /// <param name="hashLengthInBytes">
  ///   The number of bytes produced from this hash algorithm.
  /// </param>
  /// <exception cref="ArgumentOutOfRangeException">
  ///   <paramref name="hashLengthInBytes"/> is less than 1.
  /// </exception>
  protected NonCryptographicHashAlgorithm(int hashLengthInBytes) {
    switch (hashLengthInBytes) {
      case < 1:
        throw new ArgumentOutOfRangeException(nameof(hashLengthInBytes));
      default:
        this.HashLengthInBytes = hashLengthInBytes;
        break;
    }
  }

  /// <summary>
  ///   When overridden in a derived class,
  ///   appends the contents of <paramref name="source"/> to the data already
  ///   processed for the current hash computation.
  /// </summary>
  /// <param name="source">The data to process.</param>
  public abstract void Append(ReadOnlySpan<byte> source);

  /// <summary>
  ///   When overridden in a derived class,
  ///   resets the hash computation to the initial state.
  /// </summary>
  public abstract void Reset();

  /// <summary>
  ///   When overridden in a derived class,
  ///   writes the computed hash value to <paramref name="destination"/>
  ///   without modifying accumulated state.
  /// </summary>
  /// <param name="destination">The buffer that receives the computed hash value.</param>
  /// <remarks>
  ///   <para>
  ///     Implementations of this method must write exactly
  ///     <see cref="HashLengthInBytes"/> bytes to <paramref name="destination"/>.
  ///     Do not assume that the buffer was zero-initialized.
  ///   </para>
  ///   <para>
  ///     The <see cref="NonCryptographicHashAlgorithm"/> class validates the
  ///     size of the buffer before calling this method, and slices the span
  ///     down to be exactly <see cref="HashLengthInBytes"/> in length.
  ///   </para>
  /// </remarks>
  protected abstract void GetCurrentHashCore(Span<byte> destination);

  /// <summary>
  ///   Appends the contents of <paramref name="source"/> to the data already
  ///   processed for the current hash computation.
  /// </summary>
  /// <param name="source">The data to process.</param>
  /// <exception cref="ArgumentNullException">
  ///   <paramref name="source"/> is <see langword="null"/>.
  /// </exception>
  public void Append(byte[] source) {
    switch (source) {
      case null:
        throw new ArgumentNullException(nameof(source));
      default:
        this.Append(new ReadOnlySpan<byte>(source));
        break;
    }
  }

  /// <summary>
  ///   Appends the contents of <paramref name="stream"/> to the data already
  ///   processed for the current hash computation.
  /// </summary>
  /// <param name="stream">The data to process.</param>
  /// <exception cref="ArgumentNullException">
  ///   <paramref name="stream"/> is <see langword="null"/>.
  /// </exception>
  /// <seealso cref="AppendAsync(Stream, CancellationToken)"/>
  public void Append(Stream stream) {
    switch (stream) {
      case null:
        throw new ArgumentNullException(nameof(stream));
      default:
        stream.CopyTo(new CopyToDestinationStream(this));
        break;
    }
  }

  /// <summary>
  ///   Asychronously reads the contents of <paramref name="stream"/>
  ///   and appends them to the data already
  ///   processed for the current hash computation.
  /// </summary>
  /// <param name="stream">The data to process.</param>
  /// <param name="cancellationToken">
  ///   The token to monitor for cancellation requests.
  ///   The default value is <see cref="CancellationToken.None"/>.
  /// </param>
  /// <returns>
  ///   A task that represents the asynchronous append operation.
  /// </returns>
  /// <exception cref="ArgumentNullException">
  ///   <paramref name="stream"/> is <see langword="null"/>.
  /// </exception>
  public Task AppendAsync(Stream stream, CancellationToken cancellationToken = default) {
    switch (stream) {
      case null:
        throw new ArgumentNullException(nameof(stream));
      default:
        return stream.CopyToAsync(
          new CopyToDestinationStream(this),
#if !NET
                81_920, // default size used by Stream.CopyTo{Async}
#endif
          cancellationToken);
    }
  }

  /// <summary>
  ///   Gets the current computed hash value without modifying accumulated state.
  /// </summary>
  /// <returns>
  ///   The hash value for the data already provided.
  /// </returns>
  public byte[] GetCurrentHash() {
    var ret = new byte[this.HashLengthInBytes];
    this.GetCurrentHashCore(ret);
    return ret;
  }

  /// <summary>
  ///   Attempts to write the computed hash value to <paramref name="destination"/>
  ///   without modifying accumulated state.
  /// </summary>
  /// <param name="destination">The buffer that receives the computed hash value.</param>
  /// <param name="bytesWritten">
  ///   On success, receives the number of bytes written to <paramref name="destination"/>.
  /// </param>
  /// <returns>
  ///   <see langword="true"/> if <paramref name="destination"/> is long enough to receive
  ///   the computed hash value; otherwise, <see langword="false"/>.
  /// </returns>
  public bool TryGetCurrentHash(Span<byte> destination, out int bytesWritten) {
    if (destination.Length < this.HashLengthInBytes) {
      bytesWritten = 0;
      return false;
    }

    this.GetCurrentHashCore(destination[..this.HashLengthInBytes]);
    bytesWritten = this.HashLengthInBytes;
    return true;
  }

  /// <summary>
  ///   Writes the computed hash value to <paramref name="destination"/>
  ///   without modifying accumulated state.
  /// </summary>
  /// <param name="destination">The buffer that receives the computed hash value.</param>
  /// <returns>
  ///   The number of bytes written to <paramref name="destination"/>,
  ///   which is always <see cref="HashLengthInBytes"/>.
  /// </returns>
  /// <exception cref="ArgumentException">
  ///   <paramref name="destination"/> is shorter than <see cref="HashLengthInBytes"/>.
  /// </exception>
  public int GetCurrentHash(Span<byte> destination) {
    if (destination.Length < this.HashLengthInBytes) {
      ThrowDestinationTooShort();
    }

    this.GetCurrentHashCore(destination[..this.HashLengthInBytes]);
    return this.HashLengthInBytes;
  }

  /// <summary>
  ///   Gets the current computed hash value and clears the accumulated state.
  /// </summary>
  /// <returns>
  ///   The hash value for the data already provided.
  /// </returns>
  public byte[] GetHashAndReset() {
    var ret = new byte[this.HashLengthInBytes];
    this.GetHashAndResetCore(ret);
    return ret;
  }

  /// <summary>
  ///   Attempts to write the computed hash value to <paramref name="destination"/>.
  ///   If successful, clears the accumulated state.
  /// </summary>
  /// <param name="destination">The buffer that receives the computed hash value.</param>
  /// <param name="bytesWritten">
  ///   On success, receives the number of bytes written to <paramref name="destination"/>.
  /// </param>
  /// <returns>
  ///   <see langword="true"/> and clears the accumulated state
  ///   if <paramref name="destination"/> is long enough to receive
  ///   the computed hash value; otherwise, <see langword="false"/>.
  /// </returns>
  public bool TryGetHashAndReset(Span<byte> destination, out int bytesWritten) {
    if (destination.Length < this.HashLengthInBytes) {
      bytesWritten = 0;
      return false;
    }

    this.GetHashAndResetCore(destination[..this.HashLengthInBytes]);
    bytesWritten = this.HashLengthInBytes;
    return true;
  }

  /// <summary>
  ///   Writes the computed hash value to <paramref name="destination"/>
  ///   then clears the accumulated state.
  /// </summary>
  /// <param name="destination">The buffer that receives the computed hash value.</param>
  /// <returns>
  ///   The number of bytes written to <paramref name="destination"/>,
  ///   which is always <see cref="HashLengthInBytes"/>.
  /// </returns>
  /// <exception cref="ArgumentException">
  ///   <paramref name="destination"/> is shorter than <see cref="HashLengthInBytes"/>.
  /// </exception>
  public int GetHashAndReset(Span<byte> destination) {
    if (destination.Length < this.HashLengthInBytes) {
      ThrowDestinationTooShort();
    }

    this.GetHashAndResetCore(destination[..this.HashLengthInBytes]);
    return this.HashLengthInBytes;
  }

  /// <summary>
  ///   Writes the computed hash value to <paramref name="destination"/>
  ///   then clears the accumulated state.
  /// </summary>
  /// <param name="destination">The buffer that receives the computed hash value.</param>
  /// <remarks>
  ///   <para>
  ///     Implementations of this method must write exactly
  ///     <see cref="HashLengthInBytes"/> bytes to <paramref name="destination"/>.
  ///     Do not assume that the buffer was zero-initialized.
  ///   </para>
  ///   <para>
  ///     The <see cref="NonCryptographicHashAlgorithm"/> class validates the
  ///     size of the buffer before calling this method, and slices the span
  ///     down to be exactly <see cref="HashLengthInBytes"/> in length.
  ///   </para>
  ///   <para>
  ///     The default implementation of this method calls
  ///     <see cref="GetCurrentHashCore"/> followed by <see cref="Reset"/>.
  ///     Overrides of this method do not need to call either of those methods,
  ///     but must ensure that the caller cannot observe a difference in behavior.
  ///   </para>
  /// </remarks>
  protected virtual void GetHashAndResetCore(Span<byte> destination) {
    Debug.Assert(destination.Length == this.HashLengthInBytes);

    this.GetCurrentHashCore(destination);
    this.Reset();
  }

  /// <summary>
  ///   This method is not supported and should not be called.
  ///   Call <see cref="GetCurrentHash()"/> or <see cref="GetHashAndReset()"/>
  ///   instead.
  /// </summary>
  /// <returns>This method will always throw a <see cref="NotSupportedException"/>.</returns>
  /// <exception cref="NotSupportedException">In all cases.</exception>
  [EditorBrowsable(EditorBrowsableState.Never)]
  [Obsolete("Use GetCurrentHash() to retrieve the computed hash code.", true)]
#pragma warning disable CS0809 // Obsolete member overrides non-obsolete member
  public override int GetHashCode()
#pragma warning restore CS0809 // Obsolete member overrides non-obsolete member
  {
    throw new NotSupportedException("Unknown Hashcode");
  }

  [DoesNotReturn]
  private protected static void ThrowDestinationTooShort() =>
    throw new ArgumentException("Destination too short", "destination");

  /// <summary>Stream-derived type used to support copying from a source stream to this instance via CopyTo{Async}.</summary>
  private sealed class CopyToDestinationStream(NonCryptographicHashAlgorithm hash) : Stream {
    public override bool CanWrite => true;

    public override void Write(byte[] buffer, int offset, int count) => hash.Append(buffer.AsSpan(offset, count));

    public override void WriteByte(byte value) =>
      hash.Append(
#if NET
        new ReadOnlySpan<byte>(in value)
#else
                    [value]
#endif
      );

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) {
      hash.Append(buffer.AsSpan(offset, count));
      return Task.CompletedTask;
    }

#if NET
    public override void Write(ReadOnlySpan<byte> buffer) => hash.Append(buffer);

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) {
      hash.Append(buffer.Span);
      return default;
    }

    public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback? callback, object? state) =>
      TaskToAsyncResult.Begin(this.WriteAsync(buffer, offset, count), callback, state);

    public override void EndWrite(IAsyncResult asyncResult) =>
      TaskToAsyncResult.End(asyncResult);
#endif

    public override void Flush() { }

    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override long Length => throw new NotSupportedException();

    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();
  }
}