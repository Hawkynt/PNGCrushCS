namespace FileFormat.BigTiff;

/// <summary>Represents a single page/IFD in a multi-page BigTIFF file.</summary>
public sealed class BigTiffPage {
  public int Width { get; init; }
  public int Height { get; init; }
  public int SamplesPerPixel { get; init; } = 1;
  public int BitsPerSample { get; init; } = 8;
  public ushort PhotometricInterpretation { get; init; } = BigTiffFile.PhotometricMinIsBlack;
  public byte[] PixelData { get; init; } = [];
}
