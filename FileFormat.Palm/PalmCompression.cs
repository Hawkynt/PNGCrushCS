namespace FileFormat.Palm;

public enum PalmCompression : byte {
  None = 0,
  Scanline = 1,
  Rle = 2,
  PackBits = 255
}
