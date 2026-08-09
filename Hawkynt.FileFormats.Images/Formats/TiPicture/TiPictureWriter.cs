using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace FileFormat.TiPicture;

/// <summary>Writes a TI transfer file carrying one picture variable.</summary>
/// <remarks>
/// The layout is the one the samples carry, entry for entry. A TI-82 or TI-83 entry is an eleven
/// byte header — the type, an eight byte name and the length again — and the TI-82 and TI-83 name a
/// picture with the token <c>0x60</c> and a number rather than with letters, which is what both
/// samples hold. A TI-85 counts its name and a TI-86 pads it to eight, so the header is eleven bytes
/// plus the name on the one and twelve on the other.
/// <para/>
/// The two bytes after the entries are the checksum: the low sixteen bits of the sum of every byte
/// of every entry. Nothing here reads it back — the reader accounts for the file by its lengths
/// instead — so it is computed for the sake of the calculator and of the link software, both of which
/// refuse a file whose sum does not come out. It was checked against four of the five samples, which
/// agree to the bit; the fifth is out by five and is the only one of them that is damaged.
/// </remarks>
public static class TiPictureWriter {

  /// <summary>The three bytes between the signature and the comment, per model.</summary>
  private static ReadOnlySpan<byte> _Terminator8286 => [0x1A, 0x0A, 0x00];
  private static ReadOnlySpan<byte> _Terminator85 => [0x1A, 0x0C, 0x00];

  /// <summary>What the comment says when there is nothing else to put there.</summary>
  private const string _Comment = "Picture";

  /// <summary>The token the TI-82 and TI-83 name a picture with, and the number that follows it.</summary>
  private const byte _PictureToken = 0x60;

  /// <summary>What a TI-85 or TI-86 calls the picture, in letters.</summary>
  private const string _Name8586 = "PIC1";

  /// <summary>How wide a TI-86 pads its name to.</summary>
  private const int _PaddedNameLength = 8;

  public static byte[] ToBytes(TiPictureFile file) {
    var width = file.Width;
    if (width is not (TiPictureFile.Width8283 or TiPictureFile.Width8586) || file.Height != TiPictureFile.ScreenHeight)
      throw new ArgumentException(
        $"A TI picture is the calculator's screen: {TiPictureFile.Width8283} or {TiPictureFile.Width8586} by "
        + $"{TiPictureFile.ScreenHeight}, not {width} by {file.Height}.", nameof(file));

    var model = _Model(file);
    var rowBytes = (width + 7) / 8;
    var expected = rowBytes * TiPictureFile.ScreenHeight;

    var pixels = file.PixelData ?? new byte[expected];
    if (pixels.Length < expected)
      throw new ArgumentException($"A TI picture of {width} by {TiPictureFile.ScreenHeight} needs {expected} bytes and has {pixels.Length}.", nameof(file));

    var name = _Name(model);
    var entryHeader = 1 + name.Length + 2;
    var length = expected + 2;

    using var entries = new MemoryStream();
    _WriteUInt16(entries, entryHeader);
    _WriteUInt16(entries, length);
    entries.WriteByte(model is "85" or "86" ? TiPictureFile.PictureType8586 : TiPictureFile.PictureType8283);
    entries.Write(name);
    _WriteUInt16(entries, length);
    _WriteUInt16(entries, expected);
    entries.Write(pixels, 0, expected);

    var body = entries.ToArray();

    var sum = 0;
    foreach (var b in body)
      sum += b;

    var result = new byte[TiPictureFile.HeaderSize + body.Length + 2];
    Encoding.ASCII.GetBytes($"**TI{model}**").CopyTo(result, 0);
    (model == "85" ? _Terminator85 : _Terminator8286).CopyTo(result.AsSpan(8));
    Encoding.ASCII.GetBytes(_Comment).CopyTo(result, TiPictureFile.SignatureSize);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(TiPictureFile.HeaderSize - 2), (ushort)body.Length);
    body.CopyTo(result, TiPictureFile.HeaderSize);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(TiPictureFile.HeaderSize + body.Length), (ushort)sum);

    return result;
  }

  /// <summary>Which calculator the file says it came off, falling back on what its width can only be.</summary>
  private static string _Model(TiPictureFile file) {
    var model = file.Model;
    if (model is "73" or "82" or "83" && file.Width == TiPictureFile.Width8283)
      return model;

    if (model is "85" or "86" && file.Width == TiPictureFile.Width8586)
      return model;

    return file.Width == TiPictureFile.Width8283 ? "82" : "86";
  }

  /// <summary>The name as the model spells one: a token on the TI-73, TI-82 and TI-83, letters on the others.</summary>
  private static byte[] _Name(string model) {
    if (model is "73" or "82" or "83") {
      var token = new byte[_PaddedNameLength];
      token[0] = _PictureToken;
      return token;
    }

    var letters = Encoding.ASCII.GetBytes(_Name8586);
    if (model == "85")
      return [(byte)letters.Length, .. letters];

    var padded = new byte[1 + _PaddedNameLength];
    padded[0] = (byte)letters.Length;
    letters.CopyTo(padded, 1);
    for (var i = 1 + letters.Length; i <= _PaddedNameLength; ++i)
      padded[i] = (byte)' ';

    return padded;
  }

  private static void _WriteUInt16(Stream output, int value) {
    output.WriteByte((byte)value);
    output.WriteByte((byte)(value >> 8));
  }
}
