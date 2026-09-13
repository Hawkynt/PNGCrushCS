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

  public static PictureEditorFile FromStream(Stream stream) => FromBytes(StreamBytes.ReadAll(stream));

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
