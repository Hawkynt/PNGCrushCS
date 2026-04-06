namespace FileFormat.Jpeg;

/// <summary>Per-component 2D array of coefficient blocks.</summary>
internal sealed class JpegComponentData {
  public int WidthInBlocks { get; init; }
  public int HeightInBlocks { get; init; }
  public JpegCoefficientBlock[][] Blocks { get; init; } = [];

  public static JpegComponentData Allocate(int widthInBlocks, int heightInBlocks) {
    var blocks = new JpegCoefficientBlock[heightInBlocks][];
    for (var y = 0; y < heightInBlocks; ++y) {
      blocks[y] = new JpegCoefficientBlock[widthInBlocks];
      for (var x = 0; x < widthInBlocks; ++x)
        blocks[y][x] = new JpegCoefficientBlock();
    }

    return new() { WidthInBlocks = widthInBlocks, HeightInBlocks = heightInBlocks, Blocks = blocks };
  }
}
