using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using FileFormat.Jpeg;

namespace FileFormat.LViewPro;

/// <summary>Reads LView Pro image files from bytes, streams, or file paths.</summary>
public static class LViewProReader {

  public static LViewProFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("LView Pro image file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static LViewProFile FromStream(Stream stream) {
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

  public static LViewProFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length <= LViewProFile.HeightAt + 4 || !data[..2].SequenceEqual(LViewProFile.Magic))
      throw new InvalidDataException("Not an LView Pro image file: it does not open with its magic.");

    var title = Encoding.ASCII.GetString(data.Slice(LViewProFile.TitleAt, LViewProFile.Title.Length));
    if (title != LViewProFile.Title)
      throw new InvalidDataException($"Not an LView Pro image file: it says \"{title}\" where the title belongs.");

    var width = BinaryPrimitives.ReadInt32LittleEndian(data[LViewProFile.WidthAt..]);
    var height = BinaryPrimitives.ReadInt32LittleEndian(data[LViewProFile.HeightAt..]);

    if (width < 1 || height < 1)
      throw new InvalidDataException($"Invalid LView Pro size: {width}x{height}.");

    var at = data.IndexOf(JpegSignature);
    if (at < 0)
      throw new InvalidDataException("An LView Pro file carries a JPEG and this one has none.");

    var embedded = data[at..].ToArray();

    // The header states the size and so does the JPEG. Requiring the two to agree is what says the
    // three bytes found are this file's picture rather than something that happens to look like one.
    var jpeg = JpegReader.FromBytes(embedded);
    if (jpeg.Width != width || jpeg.Height != height)
      throw new InvalidDataException(
        $"The LView Pro header says {width}x{height} and the JPEG it carries is {jpeg.Width}x{jpeg.Height}.");

    return new() {
      Width = width,
      Height = height,
      Depth = data[LViewProFile.DepthAt],
      Embedded = embedded,
    };
  }

  /// <summary>The three bytes a JFIF opens with.</summary>
  private static ReadOnlySpan<byte> JpegSignature => [0xFF, 0xD8, 0xFF];

  public static LViewProFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
