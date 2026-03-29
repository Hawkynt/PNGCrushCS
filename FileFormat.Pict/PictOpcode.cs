namespace FileFormat.Pict;

/// <summary>PICT2 opcodes used in raster picture data.</summary>
public enum PictOpcode : ushort {
  Version = 0x0011,
  HeaderOp = 0x0C00,
  PackBitsRect = 0x0098,
  DirectBitsRect = 0x009A,
  EndOfPicture = 0x00FF,
}
