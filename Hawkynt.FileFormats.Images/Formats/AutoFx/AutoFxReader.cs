using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.AutoFx;

/// <summary>Reads Auto F/X pictures from bytes, streams, or file paths.</summary>
public static class AutoFxReader {

  /// <summary>The three bytes a JFIF opens with.</summary>
  private static ReadOnlySpan<byte> JpegSignature => [0xFF, 0xD8, 0xFF];

  public static AutoFxFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Auto F/X picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AutoFxFile FromStream(Stream stream) {
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

  public static AutoFxFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < AutoFxFile.PictureLengthAt + 4)
      throw new InvalidDataException($"Data too small for an Auto F/X picture (got {data.Length} bytes).");

    if (!data[..AutoFxFile.Magic.Length].SequenceEqual(AutoFxFile.Magic))
      throw new InvalidDataException("Not an Auto F/X picture: it does not open the way one does.");

    var offset = BinaryPrimitives.ReadUInt32BigEndian(data[AutoFxFile.PictureOffsetAt..]);
    var length = BinaryPrimitives.ReadUInt32BigEndian(data[AutoFxFile.PictureLengthAt..]);

    // The two together are the length of the file in every sample there is. That identity is the
    // whole of the evidence that they are an offset and a length at all, so a file failing it is not
    // one of these however it opens.
    if (offset + (long)length != data.Length)
      throw new InvalidDataException(
        $"The Auto F/X header points at {offset} for {length} bytes, which does not account for a file of {data.Length}.");

    if (offset < AutoFxFile.PictureLengthAt + 4)
      throw new InvalidDataException($"The Auto F/X header points at {offset}, which is inside the header.");

    var embedded = data[(int)offset..];
    if (embedded.Length < JpegSignature.Length || !embedded[..JpegSignature.Length].SequenceEqual(JpegSignature))
      throw new InvalidDataException($"An Auto F/X picture is a JPEG and no JPEG begins at {offset}.");

    return new() {
      PictureOffset = (int)offset,
      Embedded = embedded.ToArray(),
    };
  }

  public static AutoFxFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
