namespace FileFormat.Pds;

/// <summary>Band storage organization modes for PDS images.</summary>
public enum PdsBandStorage {
  BandSequential = 0,
  LineInterleaved = 1,
  SampleInterleaved = 2
}
