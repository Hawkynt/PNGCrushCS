using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.AtariPaintworks;

/// <summary>Reads Atari ST Paintworks/GFA/DeskPic files from bytes, streams, or file paths.</summary>
public static class AtariPaintworksReader {

  /// <summary>Standard Atari ST screen pixel data size: 32000 bytes.</summary>
  private const int _PIXEL_DATA_SIZE = 32000;

  /// <summary>Expected file size for a full screen file: 32-byte palette + 32000 bytes pixel data.</summary>
  private const int _EXPECTED_FILE_SIZE = AtariPaintworksFile.FileSize;

  public static AtariPaintworksFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Atari Paintworks file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AtariPaintworksFile FromStream(Stream stream) {
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

  /// <summary>
  /// Says why a file shorter than a whole screen was refused.
  /// </summary>
  /// <remarks>
  /// Only the uncompressed form is read here. The compressed one carries the same signature and the
  /// same palette, and is simply shorter — so calling it "too small" pointed at the wrong thing
  /// entirely, as though the file were damaged rather than in a form this does not decode.
  /// <para/>
  /// Seven samples in the corpus are refused here and every one is read by RECOIL, so this is the
  /// largest single gap in what we read. What is known about them, so the next attempt starts from
  /// it:
  /// <list type="bullet">
  ///   <item>Six are the compressed form — <c>.cl0</c>, <c>.cl1</c>, <c>.cl2</c> and <c>.pg0</c> to
  ///     <c>.pg2</c>. They carry the signature at 54, and the byte after it holds the resolution in
  ///     bits 4 and 5: low, medium and high come out as 0x80, 0x90 and 0xa0, with the <c>.cl</c>
  ///     files setting bit 1 on top of that. The packing is not PackBits: four conventions of it
  ///     were tried and none decodes to a whole screen or consumes the file exactly.</item>
  ///   <item><c>.pg3</c> is not one of these at all — it has no signature anywhere in it. Its word 0
  ///     is 2, which is the high resolution RECOIL renders it at, and its screen is simply the last
  ///     32000 bytes: read as 640 by 400 with a set bit standing for ink, it matches RECOIL's
  ///     rendering of the same file on every one of its 256000 pixels. What the 331 bytes ahead of
  ///     that hold, and which format it belongs to, is not established — so it is left refused
  ///     rather than given a reader built on one sample of something unidentified.</item>
  /// </list>
  /// </remarks>
  private static string _WrongLengthReason(ReadOnlySpan<byte> data) {
    if (data.Length > AtariPaintworksFile.SignatureOffset + AtariPaintworksFile.Signature.Length
        && data.Slice(AtariPaintworksFile.SignatureOffset, AtariPaintworksFile.Signature.Length).SequenceEqual(AtariPaintworksFile.Signature)
        && data.Length < _EXPECTED_FILE_SIZE)
      return $"This is a compressed Atari Paintworks picture ({data.Length} bytes against the {_EXPECTED_FILE_SIZE} an uncompressed one takes), which is not decoded here.";

    return $"An uncompressed Atari Paintworks picture is exactly {_EXPECTED_FILE_SIZE} bytes; this file is {data.Length}.";
  }

  /// <summary>Whether the file carries the signature that marks it as one of these.</summary>
  private static bool _HasSignature(ReadOnlySpan<byte> data)
    => data.Length > AtariPaintworksFile.SignatureOffset + AtariPaintworksFile.Signature.Length
       && data.Slice(AtariPaintworksFile.SignatureOffset, AtariPaintworksFile.Signature.Length).SequenceEqual(AtariPaintworksFile.Signature);

  /// <summary>Bitplanes each resolution spends.</summary>
  private static int _PlaneCount(AtariPaintworksResolution resolution) => resolution switch {
    AtariPaintworksResolution.Low => 4,
    AtariPaintworksResolution.Medium => 2,
    _ => 1,
  };

  /// <summary>
  /// Expands the packed screen: a byte under 128 repeats the byte after it that many times, and one
  /// of 128 or more is followed by that many bytes less 128, taken as they stand.
  /// </summary>
  /// <remarks>
  /// Established by decoding a file whose picture was already known: the packed form of a 640 by 400
  /// monochrome screen opens 0x50 0xFF, and the screen it stands for opens with exactly eighty bytes
  /// of 0xFF, which is one row. Every one of the six packed samples expands to precisely the screen
  /// its flags describe on that reading, and the first of them matches RECOIL to the byte.
  /// <para/>
  /// It is not PackBits, which was tried in four conventions and decodes none of them — the counts
  /// there are one less than they are here, and the roles of the two ranges are the other way round.
  /// </remarks>
  private static byte[] _Unpack(ReadOnlySpan<byte> packed, int wanted) {
    var result = new byte[wanted];
    var written = 0;
    var at = 0;

    while (at < packed.Length && written < wanted) {
      var control = packed[at++];
      if (control < 128) {
        if (at >= packed.Length)
          break;

        var value = packed[at++];
        var run = Math.Min(control, wanted - written);
        result.AsSpan(written, run).Fill(value);
        written += run;
        continue;
      }

      var literal = Math.Min(control - 128, Math.Min(packed.Length - at, wanted - written));
      packed.Slice(at, literal).CopyTo(result.AsSpan(written));
      at += literal;
      written += literal;
    }

    if (written < wanted)
      throw new InvalidDataException($"A packed Atari Paintworks picture states {wanted} bytes of screen; this one ran out after {written}.");

    return result;
  }

  public static AtariPaintworksFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < AtariPaintworksFile.BitmapOffset)
      throw new InvalidDataException("Data too small for a valid Atari Paintworks file.");

    var compressed = data.Length != _EXPECTED_FILE_SIZE;
    if (compressed && !_HasSignature(data))
      throw new InvalidDataException(_WrongLengthReason(data));

    var span = data;
    var header = AtariPaintworksHeader.ReadFrom(span[AtariPaintworksFile.PaletteOffset..]);

    var resolution = _DetectResolution(data);
    var (width, height) = _GetDimensions(resolution);

    // A picture may be one screenful or two; bit 1 of the flags is what says which, and the taller
    // ones are documents that scroll rather than anything the screen shows at once.
    if (compressed && (data[AtariPaintworksFile.FlagsOffset] & 0x02) == 0)
      height *= 2;

    var wanted = width / 8 * _PlaneCount(resolution) * height;
    var pixelData = compressed
      ? _Unpack(data[AtariPaintworksFile.BitmapOffset..], wanted)
      : data.Slice(AtariPaintworksFile.BitmapOffset, _PIXEL_DATA_SIZE).ToArray();

    return new AtariPaintworksFile {
      Width = width,
      Height = height,
      Resolution = resolution,
      Palette = header.Palette,
      PixelData = pixelData
    };
    }

  public static AtariPaintworksFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  /// <summary>
  /// Reads the resolution the file states.
  /// </summary>
  /// <remarks>
  /// This used to be answered from the file's length, which cannot say: the screen is the same 32000
  /// bytes in all three resolutions, so every file measured the same and every one was called low.
  /// A 640 by 400 picture therefore came back 320 by 200 and drew from the wrong half of its own
  /// data. The resolution is the long word the file opens with.
  /// </remarks>
  private static AtariPaintworksResolution _DetectResolution(ReadOnlySpan<byte> data)
    => BinaryPrimitives.ReadInt32BigEndian(data) switch {
      1 => AtariPaintworksResolution.Medium,
      2 => AtariPaintworksResolution.High,
      _ => AtariPaintworksResolution.Low,
    };

  private static (int Width, int Height) _GetDimensions(AtariPaintworksResolution resolution) => resolution switch {
    AtariPaintworksResolution.Low => (320, 200),
    AtariPaintworksResolution.Medium => (640, 200),
    AtariPaintworksResolution.High => (640, 400),
    _ => throw new InvalidDataException($"Unknown resolution: {resolution}.")
  };

  /// <summary>
  ///   Reads a file with an explicit resolution hint (useful when resolution is inferred from file extension).
  /// </summary>
  public static AtariPaintworksFile FromBytes(byte[] data, AtariPaintworksResolution resolution) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < AtariPaintworksFile.BitmapOffset)
      throw new InvalidDataException("Data too small for a valid Atari Paintworks file.");

    if (data.Length != _EXPECTED_FILE_SIZE)
      throw new InvalidDataException(_WrongLengthReason(data));

    var span = data.AsSpan();
    var header = AtariPaintworksHeader.ReadFrom(span[AtariPaintworksFile.PaletteOffset..]);
    var (width, height) = _GetDimensions(resolution);

    var pixelData = new byte[_PIXEL_DATA_SIZE];
    data.AsSpan(AtariPaintworksFile.BitmapOffset, _PIXEL_DATA_SIZE).CopyTo(pixelData.AsSpan(0));

    return new AtariPaintworksFile {
      Width = width,
      Height = height,
      Resolution = resolution,
      Palette = header.Palette,
      PixelData = pixelData
    };
  }
}
