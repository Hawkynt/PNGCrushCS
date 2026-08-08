using FileFormat.Wrappers;

namespace FileFormat.PhotoLine;

/// <summary>Assembles the wrapper: the name it opens with, then the picture.</summary>
public static class PhotoLineWriter {

  public static byte[] ToBytes(PhotoLineFile file) => WrappedPicture.Assemble(PhotoLineFile.Magic, file.Embedded ?? []);
}
