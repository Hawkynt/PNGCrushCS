using System;
using System.IO;

namespace FileFormat.EciGraphicEditor;

/// <summary>Reads ECI Graphic Editor (.eci/.ecp) files from bytes, streams, or file paths.</summary>
/// <remarks>
/// KNOWN INCOMPLETE. This decodes one frame at 160 across; the reference decoder draws 296 by 200
/// and the two disagree on nearly every pixel, so what comes out here is a picture but not this
/// one.
/// <para/>
/// What was measured off the 32770-byte sample, so the next attempt does not start from nothing.
/// It is two frames, page-aligned and blended: the file is 2 plus two banks of 16384, the first
/// bitmap at 2 and the second at 16386, and every block inside a bank starts on a page boundary
/// rather than after the thousand bytes it uses — the same striding as the FLI formats here. The
/// picture is 296 wide, which is 148 stored pixels with 12 hidden at the left, again as in FLI. The
/// two frames are averaged channel by channel.
/// <para/>
/// With the first bitmap at 2, its screen at 8194, the second bitmap at 16386, its screen at 24578
/// and one shared colour map at 25602, that model draws 80.4 per cent of the picture exactly.
/// Sweeping every page boundary in the file for the two screens and the two colour maps moves it
/// only to 81.9, so the remaining fifth is not a misplaced block. The errors are not spread evenly
/// either: where both frames select the colour map they are 95.6 per cent wrong, and where the
/// first frame selects the screen's low nibble they are wrong essentially always — while the same
/// low nibble against a different second frame is right four times in five. So the colour sources
/// are what is left to work out, not the geometry.
/// <para/>
/// Nothing is applied. Four fifths of a picture is not a decoder, and this is the same interlaced
/// family as Drazlace, DrazPaint, True Paint and Pixel Perfect, which all sit at 2 to 4 per cent —
/// whatever settles the colour rule here is likely to settle several of them.
/// </remarks>
public static class EciGraphicEditorReader {

  public static EciGraphicEditorFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("ECI Graphic Editor file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static EciGraphicEditorFile FromStream(Stream stream) {
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

  public static EciGraphicEditorFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < EciGraphicEditorFile.LoadAddressSize + EciGraphicEditorFile.MinPayloadSize)
      throw new InvalidDataException($"Data too small for a valid ECI Graphic Editor file (expected at least {EciGraphicEditorFile.LoadAddressSize + EciGraphicEditorFile.MinPayloadSize} bytes, got {data.Length}).");

    var loadAddress = (ushort)(data[0] | (data[1] << 8));

    var rawData = new byte[data.Length - EciGraphicEditorFile.LoadAddressSize];
    data.Slice(EciGraphicEditorFile.LoadAddressSize, rawData.Length).CopyTo(rawData.AsSpan(0));

    return new() {
      LoadAddress = loadAddress,
      RawData = rawData,
    };
    }

  public static EciGraphicEditorFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
