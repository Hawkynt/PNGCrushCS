using System;
using System.IO;

namespace FileFormat.AtariPlayerEditor;

/// <summary>Reads Atari Player Editor sheets from bytes, streams, or file paths.</summary>
public static class AtariPlayerEditorReader {

  public static AtariPlayerEditorFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Sheet not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AtariPlayerEditorFile FromStream(Stream stream) {
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

  public static AtariPlayerEditorFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length != AtariPlayerEditorFile.FileSize
        || !data[..AtariPlayerEditorFile.Signature.Length].SequenceEqual(AtariPlayerEditorFile.Signature))
      throw new InvalidDataException("Not an Atari Player Editor sheet.");

    int frames = data[4], height = data[5], gap = data[6];
    if (frames == 0 || frames > AtariPlayerEditorFile.MaxFrames || height == 0
        || height > AtariPlayerEditorFile.MaxHeight || gap > AtariPlayerEditorFile.MaxGap)
      throw new InvalidDataException($"Not a sheet: {frames} frames of {height} rows with a gap of {gap}.");

    return new() { Data = data.ToArray(), Frames = frames, Height = height, Gap = gap };
  }

  public static AtariPlayerEditorFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
