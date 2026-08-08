using System;
using System.IO;

namespace FileFormat.CasioQv;

/// <summary>Writes a Casio QV file: the area table, then the areas it names.</summary>
/// <remarks>
/// One area, number 12, holding the whole JFIF — which is how the later cameras store a picture and
/// what the reader takes first when a file has both. The QV-10 generation's area 3 is not written.
/// That one is a stream with its markers, its frame header and its Huffman tables taken out and its
/// three components coded as separate scans on a three-by-two luminance grid; producing it would mean
/// a second encoder built to one camera's stripped shape, and the picture it stored would be no
/// better recorded for it.
/// <para/>
/// Nothing in the file states where an area begins — the offsets are the running sum of the lengths —
/// so the table and the one area it names have to account for the file exactly, which is the check
/// the reader makes and the arithmetic this writes to.
/// </remarks>
public static class CasioQvWriter {

  public static byte[] ToBytes(CasioQvFile file) {
    var jpeg = file.Jpeg ?? throw new ArgumentException("A Casio QV file carries a picture and this one has none.", nameof(file));
    if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8)
      throw new ArgumentException("A Casio QV picture area holds a JFIF and this one does not begin with a start-of-image marker.", nameof(file));

    using var output = new MemoryStream();
    output.Write(CasioQvFile.Magic);
    output.WriteByte(0);
    output.WriteByte(1);

    var descriptor = new byte[CasioQvFile.DescriptorSize];
    descriptor[0] = (byte)(CasioQvFile.AreaWholeJpeg >> 8);
    descriptor[1] = (byte)CasioQvFile.AreaWholeJpeg;
    descriptor[2] = (byte)(jpeg.Length >> 24);
    descriptor[3] = (byte)(jpeg.Length >> 16);
    descriptor[4] = (byte)(jpeg.Length >> 8);
    descriptor[5] = (byte)jpeg.Length;
    output.Write(descriptor);

    output.Write(jpeg);
    return output.ToArray();
  }
}
