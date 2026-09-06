using System;
using System.IO;

namespace FileFormat.FunPainter;

/// <summary>Reads Fun Painter II pictures (.fp2, .fun) from bytes, streams, or file paths.</summary>
public static class FunPainterReader {

  public static FunPainterFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Fun Painter picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static FunPainterFile FromStream(Stream stream) {
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

  /// <summary>Parses a picture, expanding the payload where the file says it is packed.</summary>
  public static FunPainterFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length <= FunPainterFile.PayloadOffset)
      throw new InvalidDataException(
        $"Data too small for a Fun Painter picture (expected more than {FunPainterFile.PayloadOffset} bytes, got {data.Length}).");

    if (!_HasSignature(data))
      throw new InvalidDataException($"Not a Fun Painter picture: the bytes at offset {FunPainterFile.SignatureOffset} are not \"{FunPainterFile.Signature}\".");

    var packed = data[FunPainterFile.PackedFlagOffset] != 0;
    if (!packed) {
      if (data.Length != FunPainterFile.FileSize)
        throw new InvalidDataException(
          $"An unpacked Fun Painter picture is {FunPainterFile.FileSize} bytes, got {data.Length}.");

      return new() { Data = data.ToArray(), Packed = false };
    }

    var escape = data[FunPainterFile.EscapeOffset];
    var body = FunPainterRle.Unpack(
      data[FunPainterFile.PayloadOffset..], escape, FunPainterFile.FileSize - FunPainterFile.PayloadOffset);

    // The header is not part of the packed run, so it is carried across rather than expanded.
    var whole = new byte[FunPainterFile.FileSize];
    data[..FunPainterFile.PayloadOffset].CopyTo(whole);
    body.CopyTo(whole.AsSpan(FunPainterFile.PayloadOffset));

    return new() { Data = whole, Packed = true };
  }

  private static bool _HasSignature(ReadOnlySpan<byte> data) {
    var signature = FunPainterFile.Signature;
    if (data.Length < FunPainterFile.SignatureOffset + signature.Length)
      return false;

    for (var i = 0; i < signature.Length; ++i)
      if (data[FunPainterFile.SignatureOffset + i] != signature[i])
        return false;

    return true;
  }

  public static FunPainterFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
