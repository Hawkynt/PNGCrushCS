using System;
using System.IO;
using System.Text;

namespace FileFormat.PixelPowerCollage;

/// <summary>Reads a Pixel Power Collage picture, name and all.</summary>
public static class PixelPowerCollageReader {

  /// <summary>Where the code saying how wide a pixel is stands.</summary>
  private const int _TYPE_AT = 0x40;

  /// <summary>Where the size stands.</summary>
  private const int _WIDTH_AT = 0x4C;

  private const int _HEIGHT_AT = 0x50;

  /// <summary>
  /// Reads a file and checks that it is the file it says it is.
  /// </summary>
  /// <remarks>
  /// The comparison is against the name with its extension and it ignores case, both of which were
  /// settled by handing the same bytes over under one name after another. The original splits the
  /// path on a backslash and nothing else, so on a system whose separator is a slash it ends up
  /// comparing the whole path it was given — a quirk of a Windows program run elsewhere rather than
  /// anything the format says, and not reproduced here: what is compared is the file's own name.
  /// </remarks>
  public static PixelPowerCollageFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return _Parse(File.ReadAllBytes(file.FullName), file.Name);
  }

  public static PixelPowerCollageFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);

    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromSpan(ms.ToArray());
  }

  public static PixelPowerCollageFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  /// <summary>Always refuses: the check this format turns on cannot be made without a name.</summary>
  /// <remarks>
  /// The alternative would be to read the header and skip the one test that tells a Collage still from
  /// any other file — and since nothing else in the file identifies it, that would make this reader
  /// accept anything 128 bytes long with three plausible numbers in it. Declining is the smaller
  /// error, and <see cref="FromFile"/> is what a caller with a path should use.
  /// </remarks>
  public static PixelPowerCollageFile FromSpan(ReadOnlySpan<byte> data)
    => throw new InvalidDataException(
      "A Collage picture carries the name it must be saved under in its first 32 bytes and is "
      + "identified by nothing else, so it can only be read from a named file.");

  /// <summary>Reads the picture, having been told what the file is called.</summary>
  public static PixelPowerCollageFile FromNamedBytes(byte[] data, string name) {
    ArgumentNullException.ThrowIfNull(data);
    ArgumentNullException.ThrowIfNull(name);

    return _Parse(data, name);
  }

  private static PixelPowerCollageFile _Parse(byte[] data, string name) {
    if (data.Length < PixelPowerCollageFile.PixelOffset)
      throw new InvalidDataException($"A Collage picture is at least {PixelPowerCollageFile.PixelOffset} bytes; this one is {data.Length}.");

    var stored = _StoredName(data);
    if (!string.Equals(stored, name, StringComparison.OrdinalIgnoreCase))
      throw new InvalidDataException($"This picture is \"{stored}\"; the file it is in is called \"{name}\".");

    var bits = _ReadBigEndian(data, _TYPE_AT) switch {
      0 => 32,
      1 => 24,
      2 => 8,
      var other => throw new InvalidDataException($"A Collage pixel is 8, 24 or 32 bits wide; code {other} names none of them."),
    };

    var width = _ReadBigEndian(data, _WIDTH_AT);
    var height = _ReadBigEndian(data, _HEIGHT_AT);
    if (width is <= 0 or > PixelPowerCollageFile.MaximumExtent || height is <= 0 or > PixelPowerCollageFile.MaximumExtent)
      throw new InvalidDataException($"A picture of {width} by {height} is not one this format holds.");

    var stride = width * bits / 8;
    var needed = (long)stride * height;
    if (PixelPowerCollageFile.PixelOffset + needed > data.Length)
      throw new InvalidDataException(
        $"The header states {width} by {height} at {bits} bits, which needs {needed} bytes of picture; the file holds {data.Length - PixelPowerCollageFile.PixelOffset}.");

    var pixels = new byte[needed];
    Array.Copy(data, PixelPowerCollageFile.PixelOffset, pixels, 0, needed);

    // The stored name rather than the one on disk: the two matched or we would not be here, and it
    // is the stored one a re-write has to put back.
    return new() { Name = stored, Width = width, Height = height, BitsPerPixel = bits, PixelData = pixels };
  }

  /// <summary>The name at the head of the file, which ends at the first zero.</summary>
  private static string _StoredName(byte[] data) {
    var length = 0;
    while (length < PixelPowerCollageFile.NameSize && data[length] != 0)
      ++length;

    return Encoding.ASCII.GetString(data, 0, length);
  }

  private static int _ReadBigEndian(byte[] data, int at)
    => (data[at] << 24) | (data[at + 1] << 16) | (data[at + 2] << 8) | data[at + 3];
}
