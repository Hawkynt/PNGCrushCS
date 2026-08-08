using FileFormat.Wrappers;

namespace FileFormat.PhotoStudio;

/// <summary>Assembles the wrapper: the name it opens with, then the picture.</summary>
public static class PhotoStudioWriter {

  public static byte[] ToBytes(PhotoStudioFile file) => WrappedPicture.Assemble(PhotoStudioFile.Magic, file.Embedded ?? []);
}
