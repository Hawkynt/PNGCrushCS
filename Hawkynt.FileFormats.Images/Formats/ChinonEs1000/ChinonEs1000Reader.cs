using System;
using System.IO;

namespace FileFormat.ChinonEs1000;

/// <summary>Reads Chinon ES-1000 pictures (.cmt) from bytes, streams, or file paths.</summary>
public static class ChinonEs1000Reader {

  public static ChinonEs1000File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Chinon ES-1000 picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ChinonEs1000File FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var buffer = new byte[stream.Length - stream.Position];
      stream.ReadExactly(buffer);
      return FromBytes(buffer);
    }

    using var memory = new MemoryStream();
    stream.CopyTo(memory);
    return FromBytes(memory.ToArray());
  }

  public static ChinonEs1000File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static ChinonEs1000File FromSpan(ReadOnlySpan<byte> data) {
    // The camera writes one length and one only, and XnView will not look at a file of any other,
    // so the length is half the signature here.
    if (data.Length != ChinonEs1000File.FileSize)
      throw new InvalidDataException($"A Chinon ES-1000 picture is {ChinonEs1000File.FileSize} bytes and this is {data.Length}.");

    if (!data[..ChinonEs1000File.Magic.Length].SequenceEqual(ChinonEs1000File.Magic))
      throw new InvalidDataException("Not a Chinon ES-1000 picture: it does not open with COMET.");

    var ccd = data.Slice(ChinonEs1000File.FileHeaderSize + ChinonEs1000File.CameraHeaderSize,
                         ChinonEs1000File.CcdColumns * ChinonEs1000File.CcdLines).ToArray();
    return new() { CcdData = ccd };
  }
}
