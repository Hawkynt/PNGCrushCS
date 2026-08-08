using System;
using System.IO;
using FileFormat.Wrappers;

namespace FileFormat.CorelGallery;

/// <summary>Reads the preview out of a Corel GALLERY clipart file.</summary>
public static class CorelGalleryReader {

  public static CorelGalleryFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Corel GALLERY clipart not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static CorelGalleryFile FromStream(Stream stream) {
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

  public static CorelGalleryFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < CorelGalleryFile.PreviewOffset || !data[..CorelGalleryFile.Magic.Length].SequenceEqual(CorelGalleryFile.Magic))
      throw new InvalidDataException("Not Corel GALLERY clipart: it does not open with @CorelBMF.");

    var preview = WrappedDib.Decode(data, CorelGalleryFile.PreviewOffset, CorelGalleryFile.MaxDimension, "Corel GALLERY clipart");

    return new() { Preview = preview };
  }

  public static CorelGalleryFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
