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
/// Each frame is an FLI frame, not a plain screen. A bank is eight video matrices of 1024 followed
/// by the bitmap on the next page: bitmap at 2 with its eight matrices at 8194, and bitmap at 16386
/// with its eight at 24578. Reading one matrix a raster line, as FLI does, draws 82.6 per cent of
/// the picture exactly against 80.4 for a single matrix a frame.
/// <para/>
/// What is left is the colour that pattern 11 selects, and the shape of it is now known. Take only
/// the pixels where both frames choose that pattern, so the blend is of one colour with itself and
/// the palette entry can be read straight off what the reference draws. Grouped by character cell,
/// 166 of 208 cells want a single colour and 42 want more than one — so it is not a colour map that
/// stands still for a cell. Grouped by cell and raster line instead, all 1064 of 1064 want a single
/// colour. The colour is line-strided exactly as the matrices are.
/// <para/>
/// It is not simply a ninth block of that shape, though: sweeping every even offset in the file for
/// an eight-page run whose low nibbles satisfy those 1064 constraints gets 83 of them at best. So
/// either the two frames carry a line-strided colour each and only their blend is visible here, or
/// pattern 11 draws from something other than a colour map. That is the one thing between this
/// format and a reader.
/// <para/>
/// Two families ruled out on the way, so they are not tried again. The picture the reference draws
/// is 90.8 per cent column-doubled and carries 41 distinct colours, which says the two frames are
/// blended rather than one drawn — but the ninth of it that is *not* doubled is not a half-pixel
/// interlace either. Shifting either frame by one stored pixel on either the even or the odd drawn
/// columns scores 78.2, 79.5, 82.1 and 82.1 against 82.1 for no shift at all, so no shift helps and
/// two hurt. Nor is it the frames taking alternate columns outright, which scores 72.9. Whatever
/// makes those columns differ comes from the colour rather than from the geometry.
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
