namespace FileFormat.Gif;

/// <summary>The disposal method applied between successive animation frames (GIF89a Graphic Control Extension).</summary>
public enum FrameDisposalMethod : byte {
  /// <summary>No disposal specified — decoder default behaviour applies.</summary>
  Unspecified = 0,
  /// <summary>Do not dispose — leave the frame in place before drawing the next one.</summary>
  DoNotDispose = 1,
  /// <summary>Restore the frame area to the background colour from the Logical Screen Descriptor.</summary>
  RestoreToBackground = 2,
  /// <summary>Restore the frame area to whatever was there before this frame was drawn.</summary>
  RestoreToPrevious = 3,
}
