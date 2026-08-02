using System;
using System.IO;

namespace FileFormat.ZxMultiArtist;

/// <summary>Reads ZX Spectrum MultiArtist (.mg1/.mg2/.mg4/.mg8) files from bytes, streams, or file paths.</summary>
public static class ZxMultiArtistReader {

  /// <summary>Bitmap data size in bytes.</summary>
  internal const int BitmapSize = 6144;

  /// <summary>Bytes per pixel row (256 pixels / 8 bits per pixel).</summary>
  internal const int BytesPerRow = 32;

  /// <summary>Number of pixel rows.</summary>
  internal const int RowCount = 192;

  public static ZxMultiArtistFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("ZX Spectrum MultiArtist file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ZxMultiArtistFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  /// <summary>Turns the Spectrum's screen layout into rows one after another.</summary>
  private static byte[] _Deinterleave(ReadOnlySpan<byte> data) {
    var linear = new byte[BitmapSize];
    for (var y = 0; y < RowCount; ++y) {
      var third = y / 64;
      var characterRow = y % 64 / 8;
      var pixelLine = y % 8;
      var from = third * 2048 + pixelLine * 256 + characterRow * BytesPerRow;
      data.Slice(from, BytesPerRow).CopyTo(linear.AsSpan(y * BytesPerRow));
    }

    return linear;
  }

  /// <summary>
  /// Reads a MultiArtist picture: a header, then two frames.
  /// </summary>
  /// <remarks>
  /// What used to be assumed here was a single frame with no header at all, its mode guessed from
  /// the file's length. A real file opens with <c>MGH</c>, states its mode in the header, and then
  /// carries both bitmaps followed by both sets of attributes — so nothing real could be read, and
  /// the guess from length could only ever match a file this library had written itself.
  /// </remarks>
  public static ZxMultiArtistFile FromSpan(ReadOnlySpan<byte> data, ZxMultiArtistMode? hintMode = null) {
    var mode = ZxMultiArtistFile.DetectMode(data)
      ?? throw new InvalidDataException("Not a ZX Spectrum MultiArtist picture: it does not begin with MGH and a mode.");

    var expectedSize = ZxMultiArtistFile.GetFileSize(mode);
    if (data.Length < expectedSize)
      throw new InvalidDataException($"A MultiArtist {mode} picture is {expectedSize} bytes; this file is {data.Length}.");

    var attributeSize = ZxMultiArtistFile.GetAttributeSize(mode);
    var at = ZxMultiArtistFile.HeaderSize;

    // Both bitmaps come first, and only then the two sets of attributes.
    var first = _Deinterleave(data.Slice(at, BitmapSize));
    var second = _Deinterleave(data.Slice(at + BitmapSize, BitmapSize));
    at += BitmapSize * 2;

    return new ZxMultiArtistFile {
      Mode = mode,
      BitmapData = first,
      SecondBitmapData = second,
      AttributeData = data.Slice(at, attributeSize).ToArray(),
      SecondAttributeData = data.Slice(at + attributeSize, attributeSize).ToArray(),
    };
  }

  public static ZxMultiArtistFile FromBytes(byte[] data) => FromBytes(data, null);

  public static ZxMultiArtistFile FromBytes(byte[] data, ZxMultiArtistMode? hintMode) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data, hintMode);
  }

  private static ZxMultiArtistMode? _DetectModeFromExtension(string extension) => extension.ToLowerInvariant() switch {
    ".mg1" => ZxMultiArtistMode.Mg1,
    ".mg2" => ZxMultiArtistMode.Mg2,
    ".mg4" => ZxMultiArtistMode.Mg4,
    ".mg8" => ZxMultiArtistMode.Mg8,
    _ => null
  };
}
