using System;

namespace FileFormat.ExtendSuperHires;

/// <summary>Assembles an Extend Super Hires picture from an <see cref="ExtendSuperHiresFile"/>.</summary>
public static class ExtendSuperHiresWriter {

  /// <summary>
  /// Writes the unpacked form, whose third byte being zero is what says the rest is not compressed.
  /// </summary>
  /// <remarks>
  /// The packed form saves bytes and says nothing the unpacked one does not; its command byte spends
  /// two of its 256 encodings on doing nothing, which is a sign the packer was written to be small
  /// rather than to pack well.
  /// </remarks>
  public static byte[] ToBytes(ExtendSuperHiresFile file) {
    var data = new byte[ExtendSuperHiresFile.UnpackedFileSize];
    var source = file.Data ?? [];
    source.AsSpan(0, Math.Min(source.Length, data.Length)).CopyTo(data);
    data[2] = 0;

    return data;
  }
}
