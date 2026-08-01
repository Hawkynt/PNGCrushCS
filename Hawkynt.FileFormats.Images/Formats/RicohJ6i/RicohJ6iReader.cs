using System;
using System.IO;
using System.Text;
using FileFormat.Jpeg;

namespace FileFormat.RicohJ6i;

/// <summary>Reads Ricoh J6I pictures from bytes, streams, or file paths.</summary>
public static class RicohJ6iReader {

  public static RicohJ6iFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static RicohJ6iFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromSpan(data);
    }

    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromSpan(ms.ToArray());
  }

  public static RicohJ6iFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length <= RicohJ6iFile.HeaderSize
        || data[0] != RicohJ6iFile.SignatureFirst || data[1] != RicohJ6iFile.SignatureSecond
        || Encoding.ASCII.GetString(data.Slice(2, RicohJ6iFile.Marker.Length)) != RicohJ6iFile.Marker)
      throw new InvalidDataException("Not a Ricoh J6I picture.");

    var jpeg = data[RicohJ6iFile.HeaderSize..];
    if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8)
      throw new InvalidDataException("A Ricoh J6I holds a JPEG after its header, and this one does not.");

    // The size is the JPEG's own; the header states the camera's mode rather than the picture's shape.
    var decoded = JpegReader.FromSpan(jpeg);

    return new() {
      Header = data[..RicohJ6iFile.HeaderSize].ToArray(),
      JpegData = jpeg.ToArray(),
      Width = decoded.Width,
      Height = decoded.Height,
    };
  }

  public static RicohJ6iFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
