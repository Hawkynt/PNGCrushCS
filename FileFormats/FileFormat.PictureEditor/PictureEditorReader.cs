using System;
using System.IO;

namespace FileFormat.PictureEditor;

/// <summary>Reads Picture Editor files from bytes, streams, or file paths.</summary>
public static class PictureEditorReader {

  public static PictureEditorFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture Editor file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PictureEditorFile FromStream(Stream stream) {
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

  public static PictureEditorFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length != PictureEditorFile.ExpectedFileSize)
      throw new InvalidDataException($"Picture Editor file must be exactly {PictureEditorFile.ExpectedFileSize} bytes, got {data.Length}.");

    var pixelData = new byte[PictureEditorFile.ExpectedFileSize];
    data.Slice(0, PictureEditorFile.ExpectedFileSize).CopyTo(pixelData);

    return new PictureEditorFile { PixelData = pixelData };
    }

  public static PictureEditorFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != PictureEditorFile.ExpectedFileSize)
      throw new InvalidDataException($"Picture Editor file must be exactly {PictureEditorFile.ExpectedFileSize} bytes, got {data.Length}.");

    var pixelData = new byte[PictureEditorFile.ExpectedFileSize];
    data.AsSpan(0, PictureEditorFile.ExpectedFileSize).CopyTo(pixelData);

    return new PictureEditorFile { PixelData = pixelData };
  }
}
