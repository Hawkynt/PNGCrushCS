using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;
using FileFormat.Core;

namespace FileFormat.Oil;

/// <summary>Reads OIL (Open Image Library) pictures from bytes, streams, or file paths.</summary>
/// <remarks>
/// Nothing here is taken on trust. The description string has to be the one the specification gives,
/// the directory has to lie inside the file, the entry's stated length has to cover the image header
/// and the data behind it, and whatever the compression produces has to be exactly the number of
/// bytes the size and depth call for. A file that is not one of these comes back refused rather than
/// drawn.
/// </remarks>
public static class OilReader {

  public static OilFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("OIL file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static OilFile FromStream(Stream stream) {
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

  public static OilFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static OilFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < OilFile.HeaderSize)
      throw new InvalidDataException($"Data too small for an OIL file (minimum {OilFile.HeaderSize} bytes, got {data.Length}).");

    if (!data[..OilFile.Signature.Length].SequenceEqual(OilFile.Signature))
      throw new InvalidDataException("Not an OIL picture: it does not begin with OIL.");

    var magic = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
    if (magic != OilFile.MagicNumber)
      throw new InvalidDataException($"An OIL picture states {magic:X} where the format's own number is {OilFile.MagicNumber:X}.");

    var version = BinaryPrimitives.ReadUInt16LittleEndian(data[8..]);
    if (version != OilFile.SupportedVersion)
      throw new InvalidDataException($"An OIL picture states version {version}; only version {OilFile.SupportedVersion} is described.");

    // The description string is the header's own length made checkable: it can only be at 22, and
    // reading it there is what says the structures are packed rather than aligned.
    var described = Encoding.ASCII.GetString(data.Slice(22, OilFile.HeadStringLength - 1));
    if (described != OilFile.HeadString || data[22 + OilFile.HeadStringLength - 1] != 0)
      throw new InvalidDataException("An OIL picture does not carry the description string the format states.");

    var imageCount = BinaryPrimitives.ReadUInt32LittleEndian(data[10..]);
    var directoryOffset = BinaryPrimitives.ReadUInt32LittleEndian(data[14..]);

    if (imageCount < 1)
      throw new InvalidDataException("An OIL picture states that it holds no images.");

    if (directoryOffset > (uint)data.Length
        || (long)imageCount * OilFile.DirectoryEntrySize > data.Length - (long)directoryOffset)
      throw new InvalidDataException(
        $"An OIL picture states {imageCount} images in a directory at {directoryOffset} of a file of {data.Length}.");

    // The first entry is the picture. The rest are an animation's later frames, and a raster is one
    // picture rather than a sequence.
    var entry = (int)directoryOffset;
    var imageOffset = BinaryPrimitives.ReadUInt32LittleEndian(data[(entry + 255)..]);
    var imageLength = BinaryPrimitives.ReadUInt32LittleEndian(data[(entry + 259)..]);

    if (imageOffset > (uint)data.Length || imageLength > data.Length - (long)imageOffset)
      throw new InvalidDataException(
        $"An OIL picture states an image of {imageLength} bytes at {imageOffset} in a file of {data.Length}.");

    if (imageLength < OilFile.ImageHeaderSize)
      throw new InvalidDataException($"An OIL picture states an image of {imageLength} bytes, which is less than its own header.");

    var image = data.Slice((int)imageOffset, (int)imageLength);

    var width = BinaryPrimitives.ReadUInt32LittleEndian(image);
    var height = BinaryPrimitives.ReadUInt32LittleEndian(image[4..]);
    var depth = BinaryPrimitives.ReadUInt32LittleEndian(image[8..]);
    var channels = image[12];
    var bytesPerChannel = image[13];
    var type = image[14];
    var compression = image[15];
    var dataLength = BinaryPrimitives.ReadUInt32LittleEndian(image[21..]);

    if (width < 1 || height < 1 || width > int.MaxValue || height > int.MaxValue)
      throw new InvalidDataException($"An OIL picture states {width}x{height}.");

    // A picture is one slice. The format can state a stack of them, and taking the first would be
    // choosing which one on no evidence.
    if (depth != 1)
      throw new InvalidDataException($"An OIL picture states a depth of {depth}; only a single slice is a picture.");

    if (bytesPerChannel != 1)
      throw new InvalidDataException($"An OIL picture states {bytesPerChannel} bytes a channel, and only one is read here.");

    var expectedChannels = type switch {
      OilFile.TypePalette or OilFile.TypeLuminance => 1,
      OilFile.TypeBgr => 3,
      OilFile.TypeBgra => 4,
      _ => throw new InvalidDataException($"An OIL picture states type {type}, which the format does not describe."),
    };

    if (channels != expectedChannels)
      throw new InvalidDataException($"An OIL picture of type {type} states {channels} channels where that type has {expectedChannels}.");

    var at = OilFile.ImageHeaderSize;
    byte[]? palette = null;
    var paletteCount = 0;

    if (type == OilFile.TypePalette) {
      if (at + 4 > image.Length)
        throw new InvalidDataException("An OIL picture states a palette and ends before its size.");

      var paletteBytes = BinaryPrimitives.ReadUInt32LittleEndian(image[at..]);
      at += 4;

      if (paletteBytes < OilFile.PaletteEntrySize || paletteBytes % OilFile.PaletteEntrySize != 0
          || at + (long)paletteBytes > image.Length)
        throw new InvalidDataException($"An OIL picture states a palette of {paletteBytes} bytes with {image.Length - at} left in the image.");

      paletteCount = (int)(paletteBytes / OilFile.PaletteEntrySize);
      palette = new byte[paletteCount * 3];
      for (var i = 0; i < paletteCount; ++i) {
        var from = at + i * OilFile.PaletteEntrySize;
        palette[i * 3] = image[from + 2];
        palette[i * 3 + 1] = image[from + 1];
        palette[i * 3 + 2] = image[from];
      }

      at += (int)paletteBytes;
    }

    if (dataLength > image.Length - (long)at)
      throw new InvalidDataException($"An OIL picture states {dataLength} bytes of data with {image.Length - at} left in the image.");

    var wanted = (long)width * height * channels;
    if (wanted > int.MaxValue)
      throw new InvalidDataException($"An OIL picture of {width}x{height} in {channels} channels is larger than can be held.");

    var stored = image.Slice(at, (int)dataLength);
    var pixels = compression switch {
      OilFile.CompressionNone => _Uncompressed(stored, (int)wanted),
      OilFile.CompressionRle => _Unpack(stored, (int)wanted, channels),
      OilFile.CompressionZlib => _Inflate(stored, (int)wanted),
      OilFile.CompressionLzo => throw new InvalidDataException("An OIL picture is compressed with miniLZO, which is not decoded here."),
      _ => throw new InvalidDataException($"An OIL picture states compression {compression}, which the format does not describe."),
    };

    // The rows run from the bottom of the picture upwards. The document does not say so; three
    // things do, and they are the weakest part of this reader. XnView reports these files as
    // "Bottom Left"; the row it draws — it draws only one, over and over — is the last row stored,
    // which is the top row of the picture only if the file is bottom-up; and the library the format
    // belongs to holds its images at a lower-left origin. A sample would settle it and there is
    // none.
    return _Assemble(pixels, (int)width, (int)height, type, channels, palette, paletteCount);
  }

  private static byte[] _Uncompressed(ReadOnlySpan<byte> stored, int wanted) {
    if (stored.Length != wanted)
      throw new InvalidDataException($"An uncompressed OIL picture states {stored.Length} bytes where its size calls for {wanted}.");

    return stored.ToArray();
  }

  /// <summary>zlib's own stream, which has to give exactly the picture and no more.</summary>
  private static byte[] _Inflate(ReadOnlySpan<byte> stored, int wanted) {
    var output = new byte[wanted];
    try {
      using var source = new MemoryStream(stored.ToArray(), writable: false);
      using var inflate = new ZLibStream(source, CompressionMode.Decompress);
      var filled = 0;
      while (filled < wanted) {
        var read = inflate.Read(output, filled, wanted - filled);
        if (read <= 0)
          throw new InvalidDataException($"A zlib-compressed OIL picture gives {filled} bytes where its size calls for {wanted}.");

        filled += read;
      }

      if (inflate.ReadByte() >= 0)
        throw new InvalidDataException($"A zlib-compressed OIL picture gives more than the {wanted} bytes its size calls for.");
    } catch (InvalidDataException) {
      throw;
    } catch (Exception failure) {
      throw new InvalidDataException($"A zlib-compressed OIL picture does not inflate: {failure.Message}");
    }

    return output;
  }

  /// <summary>
  /// The run-length coding the specification takes from Targa: a control byte, then either one
  /// pixel repeated or a run of pixels as they stand, counted in pixels rather than bytes.
  /// </summary>
  private static byte[] _Unpack(ReadOnlySpan<byte> stored, int wanted, int channels) {
    var output = new byte[wanted];
    var at = 0;
    var written = 0;

    while (written < wanted) {
      if (at >= stored.Length)
        throw new InvalidDataException($"A run-length coded OIL picture runs out {written} bytes into {wanted}.");

      var control = stored[at++];
      var count = (control & 0x7F) + 1;
      var bytes = count * channels;
      if (written + bytes > wanted)
        throw new InvalidDataException($"A run-length coded OIL picture states {count} pixels with {(wanted - written) / channels} left.");

      if ((control & 0x80) != 0) {
        if (at + channels > stored.Length)
          throw new InvalidDataException("A run-length coded OIL picture ends inside the pixel a run repeats.");

        for (var i = 0; i < count; ++i)
          stored.Slice(at, channels).CopyTo(output.AsSpan(written + i * channels));

        at += channels;
      } else {
        if (at + bytes > stored.Length)
          throw new InvalidDataException($"A run-length coded OIL picture states {count} pixels and holds {(stored.Length - at) / channels}.");

        stored.Slice(at, bytes).CopyTo(output.AsSpan(written));
        at += bytes;
      }

      written += bytes;
    }

    if (at != stored.Length)
      throw new InvalidDataException($"A run-length coded OIL picture codes its pixels in {at} bytes and states {stored.Length}.");

    return output;
  }

  /// <summary>Turns the stored bytes the right way up and the right way round.</summary>
  private static OilFile _Assemble(byte[] pixels, int width, int height, byte type, int channels, byte[]? palette, int paletteCount) {
    var stride = width * channels;
    var result = new byte[pixels.Length];

    for (var y = 0; y < height; ++y) {
      var from = (height - 1 - y) * stride;
      var to = y * stride;

      if (type is OilFile.TypePalette or OilFile.TypeLuminance) {
        pixels.AsSpan(from, stride).CopyTo(result.AsSpan(to));
        continue;
      }

      // Blue, green, red — and alpha where there is one, which stays where it is.
      for (var x = 0; x < width; ++x) {
        var source = from + x * channels;
        var destination = to + x * channels;
        result[destination] = pixels[source + 2];
        result[destination + 1] = pixels[source + 1];
        result[destination + 2] = pixels[source];
        if (channels == 4)
          result[destination + 3] = pixels[source + 3];
      }
    }

    return new() {
      Width = width,
      Height = height,
      Format = type switch {
        OilFile.TypePalette => PixelFormat.Indexed8,
        OilFile.TypeLuminance => PixelFormat.Gray8,
        OilFile.TypeBgr => PixelFormat.Rgb24,
        _ => PixelFormat.Rgba32,
      },
      PixelData = result,
      Palette = palette,
      PaletteCount = paletteCount,
    };
  }
}
