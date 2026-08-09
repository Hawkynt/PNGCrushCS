using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;
using FileFormat.Tiff;

namespace FileFormat.PicturePublisher4;

/// <summary>Reads Micrografx Picture Publisher 4 documents from bytes, streams, or file paths.</summary>
public static class PicturePublisher4Reader {

  public static PicturePublisher4File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture Publisher 4 file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PicturePublisher4File FromStream(Stream stream) {
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

  public static PicturePublisher4File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static PicturePublisher4File FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < PicturePublisher4File.MinFileSize)
      throw new InvalidDataException(
        $"Data too small for a Picture Publisher 4 document (at least {PicturePublisher4File.MinFileSize} bytes are needed, got {data.Length}).");

    if (!data[..PicturePublisher4File.Signature.Length].SequenceEqual(PicturePublisher4File.Signature))
      throw new InvalidDataException("Not a Picture Publisher 4 document: it does not open with II.");

    var at = BinaryPrimitives.ReadInt32LittleEndian(data[PicturePublisher4File.PointerOffset..]);
    if (at < PicturePublisher4File.MinFileSize || at >= data.Length)
      throw new InvalidDataException(
        $"A Picture Publisher 4 document points at {at}, which is not inside a file of {data.Length} bytes.");

    var embedded = data[at..];
    if (!_LooksLikeTiff(embedded))
      throw new InvalidDataException("A Picture Publisher 4 document's offset does not point at a TIFF.");

    var tiff = TiffReader.FromSpan(embedded);
    var image = TiffFile.ToRawImage(tiff);

    return new() {
      Width = image.Width,
      Height = image.Height,
      PictureOffset = at,
      Embedded = embedded.ToArray(),
    };
  }

  /// <summary>Turns the carried TIFF into a picture.</summary>
  public static RawImage Decode(PicturePublisher4File file) {
    var embedded = file.Embedded ?? throw new InvalidDataException("A Picture Publisher 4 document carries no TIFF.");
    return TiffFile.ToRawImage(TiffReader.FromSpan(embedded));
  }

  /// <summary>The eight bytes a TIFF opens with: a byte order, the number 42 and an offset.</summary>
  private static bool _LooksLikeTiff(ReadOnlySpan<byte> data) {
    if (data.Length < 8)
      return false;

    return data[0] switch {
      (byte)'I' when data[1] == (byte)'I' => BinaryPrimitives.ReadUInt16LittleEndian(data[2..]) == 42,
      (byte)'M' when data[1] == (byte)'M' => BinaryPrimitives.ReadUInt16BigEndian(data[2..]) == 42,
      _ => false,
    };
  }
}
