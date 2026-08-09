using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Ccitt;

namespace FileFormat.NcrImage;

/// <summary>Reads NCR Images from bytes, streams, or file paths.</summary>
public static class NcrImageReader {

  /// <summary>The smallest coding byte that selects Group 4, which is the one setting read here.</summary>
  public const byte CodingGroup4 = 1;

  public static NcrImageFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("NCR Image file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static NcrImageFile FromStream(Stream stream) {
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

  public static NcrImageFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static NcrImageFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length <= NcrImageFile.CodedDataOffset)
      throw new InvalidDataException(
        $"Data too small for an NCR Image (more than {NcrImageFile.CodedDataOffset} bytes are needed, got {data.Length}).");

    if (!data[..NcrImageFile.Signature.Length].SequenceEqual(NcrImageFile.Signature))
      throw new InvalidDataException("Not an NCR Image: the four bytes it opens with are not the format's.");

    var width = BinaryPrimitives.ReadUInt16LittleEndian(data[NcrImageFile.WidthOffset..]);
    var height = BinaryPrimitives.ReadUInt16LittleEndian(data[NcrImageFile.HeightOffset..]);
    if (width < 1 || height < 1)
      throw new InvalidDataException($"An NCR Image states a picture of {width}x{height}.");

    var coding = data[NcrImageFile.CodingOffset];
    if (coding < CodingGroup4)
      throw new InvalidDataException(
        $"An NCR Image states coding {coding}, which is not Group 4 and is not decoded here.");

    var coded = data[NcrImageFile.CodedDataOffset..].ToArray();
    var pixelData = CcittG4Decoder.Decode(coded, width, height, out var rowsDecoded);
    if (rowsDecoded != height)
      throw new InvalidDataException(
        $"An NCR Image's Group 4 coding runs out after {rowsDecoded} of the {height} rows its header states.");

    return new() { Width = width, Height = height, PixelData = pixelData };
  }
}
