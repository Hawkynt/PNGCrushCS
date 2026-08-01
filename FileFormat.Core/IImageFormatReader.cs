using System;
using System.IO;

namespace FileFormat.Core;

/// <summary>Parses a file format from a byte span. Use <see cref="FormatIO"/> for byte[], Stream, and FileInfo overloads.</summary>
public interface IImageFormatReader<TSelf> : IImageFormatMetadata<TSelf> where TSelf : IImageFormatReader<TSelf> {

  /// <summary>Parses the format from raw bytes.</summary>
  static abstract TSelf FromSpan(ReadOnlySpan<byte> data);

  /// <summary>Parses the format from a file, which a few of them need more than the bytes for.</summary>
  /// <remarks>
  /// Almost nothing needs this and the default just reads the bytes. A handful of formats keep part
  /// of themselves beside the file — most often the palette, under the same name with a different
  /// extension — and the picture cannot be shown properly without it. Those need to know where the
  /// file is, which the bytes alone do not say.
  /// <para/>
  /// Reading by bytes still works for them and still returns a picture; what it returns is the one
  /// the file can describe on its own, which for a drawing whose colours live elsewhere is a grey
  /// ramp rather than the drawing as intended.
  /// </remarks>
  static virtual TSelf FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);

    return TSelf.FromSpan(File.ReadAllBytes(file.FullName));
  }
}
