using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Png;

namespace FileFormat.EccHeader;

/// <summary>Reads ECC pictures from bytes, streams, or file paths.</summary>
public static class EccHeaderReader {

  public static EccHeaderFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("ECC picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static EccHeaderFile FromStream(Stream stream) {
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

  public static EccHeaderFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length <= EccHeaderFile.SecondHeightAt + 2 || !data[..4].SequenceEqual(EccHeaderFile.Magic))
      throw new InvalidDataException("Not an ECC picture: it does not open with ECCH.");

    var width = BinaryPrimitives.ReadUInt16LittleEndian(data[EccHeaderFile.WidthAt..]);
    var height = BinaryPrimitives.ReadUInt16LittleEndian(data[EccHeaderFile.HeightAt..]);

    if (width < 1 || height < 1)
      throw new InvalidDataException($"Invalid ECC size: {width}x{height}.");

    var at = data.IndexOf(PngSignature);
    if (at < 0)
      throw new InvalidDataException("An ECC picture carries a PNG and this one has none.");

    var embedded = data[at..].ToArray();

    // The header states the size and the picture states it too. Taking the picture on the strength
    // of its signature alone would draw whatever eight bytes happened to match; requiring the two to
    // agree is what says this is the file's picture rather than something that looks like one.
    var png = PngReader.FromBytes(embedded);
    if (png.Width != width || png.Height != height)
      throw new InvalidDataException(
        $"The ECC header says {width}x{height} and the PNG it carries is {png.Width}x{png.Height}.");

    return new() { Width = width, Height = height, Embedded = embedded };
  }

  /// <summary>The eight bytes a PNG opens with.</summary>
  private static ReadOnlySpan<byte> PngSignature => [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

  public static EccHeaderFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
