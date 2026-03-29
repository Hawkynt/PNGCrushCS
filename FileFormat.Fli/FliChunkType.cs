namespace FileFormat.Fli;

/// <summary>Identifies the type of a sub-chunk within a FLI/FLC frame.</summary>
public enum FliChunkType : short {
  Color256 = 4,
  DeltaFlc = 7,
  Color64 = 11,
  DeltaFli = 12,
  Black = 13,
  ByteRun = 15,
  Literal = 16
}
