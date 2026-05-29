namespace FileFormat.Fli;

/// <summary>Identifies the FLI/FLC file format variant.</summary>
public enum FliFrameType : short {
  Fli = unchecked((short)0xAF11),
  Flc = unchecked((short)0xAF12)
}
