using System;
using System.Buffers;
using System.IO;

namespace FileFormat.Core;

/// <summary>Zero-cost generic entry points for format I/O. All convenience overloads (byte[], Stream, FileInfo) live here — format types only implement <see cref="IImageFormatReader{T}.FromSpan"/> and <see cref="IImageFormatWriter{T}.ToBytes"/>.</summary>
public static class FormatIO {

  private const int _HEADER_READ_SIZE = 4096;

  // --- Read ---

  public static T Read<T>(ReadOnlySpan<byte> data) where T : IImageFormatReader<T>
    => T.FromSpan(data);

  public static T Read<T>(byte[] data) where T : IImageFormatReader<T>
    => T.FromSpan(data);

  public static T Read<T>(Stream stream) where T : IImageFormatReader<T>
    => _ReadFromStream<T, T>(stream, static (span) => T.FromSpan(span));

  public static T Read<T>(FileInfo file) where T : IImageFormatReader<T>
    => _ReadFromFile<T>(file, static (span) => T.FromSpan(span));

  // --- Read + Decode ---

  public static RawImage Decode<T>(ReadOnlySpan<byte> data) where T : IImageFormatReader<T>, IImageToRawImage<T>
    => T.ToRawImage(T.FromSpan(data));

  public static RawImage Decode<T>(byte[] data) where T : IImageFormatReader<T>, IImageToRawImage<T>
    => T.ToRawImage(T.FromSpan(data));

  public static RawImage Decode<T>(Stream stream) where T : IImageFormatReader<T>, IImageToRawImage<T>
    => _ReadFromStream<T, RawImage>(stream, static (span) => T.ToRawImage(T.FromSpan(span)));

  public static RawImage Decode<T>(FileInfo file) where T : IImageFormatReader<T>, IImageToRawImage<T>
    => _ReadFromFile<RawImage>(file, static (span) => T.ToRawImage(T.FromSpan(span)));

  // --- Write ---

  public static byte[] Write<T>(T file) where T : IImageFormatWriter<T>
    => T.ToBytes(file);

  public static void Write<T>(T file, Stream stream) where T : IImageFormatWriter<T>
    => stream.Write(T.ToBytes(file));

  // --- Encode + Write ---

  public static byte[] Encode<T>(RawImage image) where T : IImageFromRawImage<T>, IImageFormatWriter<T>
    => T.ToBytes(T.FromRawImage(image));

  public static void Encode<T>(RawImage image, Stream stream) where T : IImageFromRawImage<T>, IImageFormatWriter<T>
    => stream.Write(T.ToBytes(T.FromRawImage(image)));

  // --- Metadata ---

  public static string PrimaryExtension<T>() where T : IImageFormatMetadata<T> => T.PrimaryExtension;
  public static string[] FileExtensions<T>() where T : IImageFormatMetadata<T> => T.FileExtensions;
  public static bool? MatchesSignature<T>(ReadOnlySpan<byte> header) where T : IImageFormatMetadata<T> => T.MatchesSignature(header);

  // --- Image Info (metadata-only, no pixel decode) ---

  public static ImageInfo? ReadInfo<T>(ReadOnlySpan<byte> data) where T : IImageInfoReader<T>
    => T.ReadImageInfo(data);

  public static ImageInfo? ReadInfo<T>(byte[] data) where T : IImageInfoReader<T>
    => T.ReadImageInfo(data);

  public static ImageInfo? ReadInfo<T>(Stream stream) where T : IImageInfoReader<T> {
    var buf = ArrayPool<byte>.Shared.Rent(_HEADER_READ_SIZE);
    try {
      var read = stream.Read(buf, 0, Math.Min(buf.Length, _HEADER_READ_SIZE));
      return T.ReadImageInfo(buf.AsSpan(0, read));
    } finally {
      ArrayPool<byte>.Shared.Return(buf);
    }
  }

  public static ImageInfo? ReadInfo<T>(FileInfo file) where T : IImageInfoReader<T> {
    using var fs = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
    var size = (int)Math.Min(_HEADER_READ_SIZE, fs.Length);
    var buf = ArrayPool<byte>.Shared.Rent(size);
    try {
      var read = fs.Read(buf, 0, size);
      return T.ReadImageInfo(buf.AsSpan(0, read));
    } finally {
      ArrayPool<byte>.Shared.Return(buf);
    }
  }

  // --- Internal helpers ---

  private delegate TResult SpanFunc<out TResult>(ReadOnlySpan<byte> span);

  private static TResult _ReadFromStream<T, TResult>(Stream stream, SpanFunc<TResult> action) where T : IImageFormatReader<T> {
    if (stream.CanSeek) {
      var length = (int)(stream.Length - stream.Position);
      var buf = ArrayPool<byte>.Shared.Rent(length);
      try {
        stream.ReadExactly(buf, 0, length);
        return action(buf.AsSpan(0, length));
      } finally {
        ArrayPool<byte>.Shared.Return(buf);
      }
    }

    // Non-seekable: must buffer entire stream (can't know size upfront)
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return action(ms.GetBuffer().AsSpan(0, (int)ms.Length));
  }

  private static TResult _ReadFromFile<TResult>(FileInfo file, SpanFunc<TResult> action) {
    var length = (int)file.Length;
    var buf = ArrayPool<byte>.Shared.Rent(length);
    try {
      using var fs = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
      fs.ReadExactly(buf, 0, length);
      return action(buf.AsSpan(0, length));
    } finally {
      ArrayPool<byte>.Shared.Return(buf);
    }
  }
}
