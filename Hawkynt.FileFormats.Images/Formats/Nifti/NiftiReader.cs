using System;
using System.IO;

namespace FileFormat.Nifti;

/// <summary>Reads NIfTI-1 single-file images, accepting both little- and big-endian headers/voxels.</summary>
public static class NiftiReader {

  public static NiftiFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("NIfTI file not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static NiftiFile FromStream(Stream stream) {
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

  public static NiftiFile FromSpan(ReadOnlySpan<byte> data)
    => Nifti1Codec.ParseSingle(data);

  public static NiftiFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
