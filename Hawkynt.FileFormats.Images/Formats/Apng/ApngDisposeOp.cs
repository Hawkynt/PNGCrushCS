namespace FileFormat.Apng;

/// <summary>APNG dispose operations as defined in the APNG specification.</summary>
public enum ApngDisposeOp : byte {
  None = 0,
  Background = 1,
  Previous = 2
}
