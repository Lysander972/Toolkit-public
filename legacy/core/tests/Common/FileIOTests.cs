using Xunit;
using Toolkit.Core.Infra;
using Toolkit.Core.Persistence;

namespace Toolkit.Core.Tests.Common;

/// <summary>
/// Tests for FileIO utility class.
/// Test data sourced from real test projects at test/data/projects
/// </summary>
[Collection("FileIO")] // 隔离测试集合，避免并行执行时与其他测试的全局状态冲突
public sealed class FileIOTests : TempDirectoryTestFixture
{
    private const string TestFileRelativePath = "test.txt";
    private const string TestDirRelativePath = "testdir";
    private readonly string _testFilePath;
    private readonly string _testDirPath;

    public FileIOTests() : base("Toolkit.Core.Tests.FileIO")
    {
        _testFilePath = SystemIOWrapper.Combine(TempDir, TestFileRelativePath);
        _testDirPath = SystemIOWrapper.Combine(TempDir, TestDirRelativePath);
        ProjectModel.SetProjectRootFullPath(TempDir);
    }

    [Fact]
    public void FileExists_Should_Return_True_For_Existing_File()
    {
        // Arrange
        SystemIOWrapper.WriteAllText(_testFilePath, "test content");

        // Act
        bool exists = SystemIOWrapper.FileExists(_testFilePath);

        // Assert
        Assert.True(exists);
    }

    [Fact]
    public void FileExists_Should_Return_False_For_Non_Existent_File()
    {
        // Act
        bool exists = SystemIOWrapper.FileExists("nonexistent.txt");

        // Assert
        Assert.False(exists);
    }

    [Fact]
    public void DirectoryExists_Should_Return_True_For_Existing_Directory()
    {
        // Arrange
        SystemIOWrapper.CreateDirectory(_testDirPath);

        // Act
        bool exists = SystemIOWrapper.DirectoryExists(_testDirPath);

        // Assert
        Assert.True(exists);
    }

    [Fact]
    public void DirectoryExists_Should_Return_False_For_Non_Existent_Directory()
    {
        // Act
        bool exists = SystemIOWrapper.DirectoryExists("nonexistent-dir");

        // Assert
        Assert.False(exists);
    }

    [Fact]
    public void ReadAllText_Should_Read_File_Content()
    {
        // Arrange - Test data from real project vars.json format
        // Source: test/data/projects/水果切切切/map.map_/255/vars.json
        string expectedContent = @"[
 {
  name:参数调节列表
  type:List
 }
 {
  name:是否为GM
  type:Bool
 }
]";
        SystemIOWrapper.WriteAllText(_testFilePath, expectedContent);

        // Act
        string content = SystemIOWrapper.ReadAllText(_testFilePath);

        // Assert
        Assert.Equal(expectedContent, content);
    }

    [Fact]
    public void ReadAllText_Should_Handle_UTF8_With_BOM()
    {
        // Arrange
        string expectedContent = "测试内容";
        byte[] utf8WithBom = TextEncoding.Utf8.GetPreamble().Concat(TextEncoding.Utf8.GetBytes(expectedContent)).ToArray();
        SystemIOWrapper.WriteAllBytes(_testFilePath, utf8WithBom);

        // Act
        string content = SystemIOWrapper.ReadAllText(_testFilePath);

        // Assert
        Assert.Equal(expectedContent, content);
    }

    [Fact]
    public void ReadAllBytes_Should_Read_File_Bytes()
    {
        // Arrange
        byte[] expectedBytes = new byte[] { 0xEF, 0xBB, 0xBF, 0x74, 0x65, 0x73, 0x74 }; // UTF-8 BOM + "test"
        SystemIOWrapper.WriteAllBytes(_testFilePath, expectedBytes);

        // Act
        byte[] bytes = SystemIOWrapper.ReadAllBytes(_testFilePath);

        // Assert
        Assert.Equal(expectedBytes, bytes);
    }


    [Fact]
    public void ReadAllLines_Should_Split_Lines_Correctly()
    {
        // Arrange - Test data from real list.json format
        // Source: test/data/projects/水果切切切/map.map_/255/scripts/list.json
        string content = @"[
 按钮
 使用帮助
 初始化快捷调试面板
]";
        SystemIOWrapper.WriteAllText(_testFilePath, content);

        // Act
        string[] lines = FileIO.ReadAllLines(TestFileRelativePath);

        // Assert
        Assert.Equal(5, lines.Length);
        Assert.Equal("[", lines[0]);
        Assert.Contains("按钮", lines[1]);
        Assert.Contains("使用帮助", lines[2]);
    }

    [Fact]
    public void ReadAllLines_Should_Handle_Windows_Line_Endings()
    {
        // Arrange - 使用 FileIO.WriteAllText，写入时会按统一换行规范落盘
        string content = "line1\r\nline2\r\nline3";
        FileIO.WriteAllText(TestFileRelativePath, content);

        // Act
        string[] lines = FileIO.ReadAllLines(TestFileRelativePath);

        // Assert
        Assert.Equal(3, lines.Length);
        Assert.Equal("line1", lines[0]);
        Assert.Equal("line2", lines[1]);
        Assert.Equal("line3", lines[2]);
    }

    [Fact]
    public void WriteAllText_Should_Write_Content_Correctly()
    {
        // Arrange
        string content = "test content for writing";

        // Act
        SystemIOWrapper.WriteAllText(_testFilePath, content);
        string readContent = SystemIOWrapper.ReadAllText(_testFilePath);

        // Assert
        Assert.Equal(content, readContent);
    }

    [Fact]
    public void WriteAllText_Should_Skip_Write_If_Content_Unchanged()
    {
        // Arrange
        string content = "unchanged content";

        // First write with FileIO to ensure consistent encoding
        SystemIOWrapper.WriteAllText(_testFilePath, content);

        // Get file hash before
        byte[] bytes1 = SystemIOWrapper.ReadAllBytes(_testFilePath);

        // Act - Write same content again with FileIO
        SystemIOWrapper.WriteAllText(_testFilePath, content);

        // Get file hash after
        byte[] bytes2 = SystemIOWrapper.ReadAllBytes(_testFilePath);

        // Assert
        Assert.Equal(bytes1, bytes2); // Content should be identical
    }

    [Fact]
    public void WriteAllBytes_Should_Write_Bytes_Correctly()
    {
        // Arrange
        byte[] bytes = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };

        // Act
        SystemIOWrapper.WriteAllBytes(_testFilePath, bytes);
        byte[] readBytes = SystemIOWrapper.ReadAllBytes(_testFilePath);

        // Assert
        Assert.Equal(bytes, readBytes);
    }

    [Fact]
    public void WriteAllBytes_Should_Skip_Write_If_Bytes_Unchanged()
    {
        // Arrange
        byte[] bytes = new byte[] { 0x01, 0x02, 0x03 };
        SystemIOWrapper.WriteAllBytes(_testFilePath, bytes);

        // Get file info before
        byte[] bytes1 = SystemIOWrapper.ReadAllBytes(_testFilePath);

        // Act - Write same bytes again
        SystemIOWrapper.WriteAllBytes(_testFilePath, bytes);

        // Get file info after
        byte[] bytes2 = SystemIOWrapper.ReadAllBytes(_testFilePath);

        // Assert
        Assert.Equal(bytes1, bytes2); // Bytes should be identical
    }

    [Fact]
    public void MoveFile_Should_Move_File_To_New_Location()
    {
        // Arrange
        string sourcePath = SystemIOWrapper.Combine(TempDir, "source.txt");
        string destPath = SystemIOWrapper.Combine(TempDir, "dest.txt");
        SystemIOWrapper.WriteAllText(sourcePath, "content to move");

        // Act
        SystemIOWrapper.MoveFile(sourcePath, destPath);

        // Assert
        Assert.False(SystemIOWrapper.FileExists(sourcePath), "Source file should not exist");
        Assert.True(SystemIOWrapper.FileExists(destPath), "Destination file should exist");
        Assert.Equal("content to move", SystemIOWrapper.ReadAllText(destPath));
    }

    [Fact]
    public void MoveDirectory_Should_Move_Directory_To_New_Location()
    {
        // Arrange
        string sourceRelative = "sourcedir";
        string destRelative = "destdir";
        string sourceDir = SystemIOWrapper.Combine(TempDir, sourceRelative);
        string destDir = SystemIOWrapper.Combine(TempDir, destRelative);
        SystemIOWrapper.CreateDirectory(sourceDir);
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(sourceDir, "file.txt"), "content");

        // Act
        FileIO.MoveDirectory(sourceRelative, destRelative);

        // Assert
        Assert.False(SystemIOWrapper.DirectoryExists(sourceDir), "Source directory should not exist");
        Assert.True(SystemIOWrapper.DirectoryExists(destDir), "Destination directory should exist");
        Assert.True(SystemIOWrapper.FileExists(SystemIOWrapper.Combine(destDir, "file.txt")), "File should exist in new location");
    }

    [Fact]
    public void DeleteFile_Should_Remove_File()
    {
        // Arrange
        SystemIOWrapper.WriteAllText(_testFilePath, "content to delete");

        // Act
        SystemIOWrapper.DeleteFile(_testFilePath);

        // Assert
        Assert.False(SystemIOWrapper.FileExists(_testFilePath));
    }

    [Fact]
    public void CreateDirectory_Should_Create_Directory()
    {
        // Arrange
        string newDir = SystemIOWrapper.Combine(TempDir, "newdir", "subdir");

        // Act
        SystemIOWrapper.CreateDirectory(newDir);

        // Assert
        Assert.True(SystemIOWrapper.DirectoryExists(newDir));
    }

    [Fact]
    public void GetFiles_Should_Return_Files_Matching_Pattern()
    {
        // Arrange
        SystemIOWrapper.CreateDirectory(_testDirPath);
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(_testDirPath, "file1.txt"), "content1");
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(_testDirPath, "file2.txt"), "content2");
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(_testDirPath, "file3.log"), "content3");

        // Act
        string[] files = SystemIOWrapper.GetFiles(_testDirPath, "*.txt", SearchOption.TopDirectoryOnly);

        // Assert
        Assert.Equal(2, files.Length);
        Assert.Contains(SystemIOWrapper.Combine(_testDirPath, "file1.txt"), files);
        Assert.Contains(SystemIOWrapper.Combine(_testDirPath, "file2.txt"), files);
    }

    [Fact]
    public void GetDirectories_Should_Return_Subdirectories()
    {
        // Arrange
        SystemIOWrapper.CreateDirectory(_testDirPath);
        SystemIOWrapper.CreateDirectory(SystemIOWrapper.Combine(_testDirPath, "subdir1"));
        SystemIOWrapper.CreateDirectory(SystemIOWrapper.Combine(_testDirPath, "subdir2"));
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(_testDirPath, "file.txt"), "content");

        // Act
        string[] dirs = SystemIOWrapper.GetDirectories(_testDirPath, "*", SearchOption.TopDirectoryOnly);

        // Assert
        Assert.Equal(2, dirs.Length);
    }

    [Fact]
    public void EnumerateDirectories_Should_Enumerate_Subdirectories()
    {
        // Arrange
        SystemIOWrapper.CreateDirectory(_testDirPath);
        SystemIOWrapper.CreateDirectory(SystemIOWrapper.Combine(_testDirPath, "dir1"));
        SystemIOWrapper.CreateDirectory(SystemIOWrapper.Combine(_testDirPath, "dir2"));

        // Act
        List<string> dirs = SystemIOWrapper.EnumerateDirectories(_testDirPath, "*", SearchOption.TopDirectoryOnly).ToList();

        // Assert
        Assert.Equal(2, dirs.Count);
    }

    [Fact]
    public void EnumerateFiles_Should_Filter_By_Path_Filters()
    {
        // Arrange
        SystemIOWrapper.CreateDirectory(_testDirPath);
        SystemIOWrapper.CreateDirectory(SystemIOWrapper.Combine(_testDirPath, "sub1"));
        SystemIOWrapper.CreateDirectory(SystemIOWrapper.Combine(_testDirPath, "sub2"));

        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(_testDirPath, "root.txt"), "root");
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(_testDirPath, "sub1", "file1.txt"), "content1");
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(_testDirPath, "sub2", "file2.txt"), "content2");

        // Act
        List<string> files = FileIO.EnumerateFiles(
            TestDirRelativePath,
            "*.txt",
            new[] { "sub1" },
            acceptRelativePath: null,
            SearchOption.AllDirectories).ToList();

        // Assert
        Assert.Single(files);
        Assert.Contains("sub1", files[0]);
    }

    [Fact]
    public void EnumerateFiles_Should_Filter_By_AcceptFullPath()
    {
        // Arrange
        SystemIOWrapper.CreateDirectory(_testDirPath);
        string file1Path = SystemIOWrapper.Combine(_testDirPath, "keep_this_one.txt");
        string file2Path = SystemIOWrapper.Combine(_testDirPath, "discard_this_one.txt");
        SystemIOWrapper.WriteAllText(file1Path, "content1");
        SystemIOWrapper.WriteAllText(file2Path, "content2");

        // Act - Only keep files with "keep" in the path
        List<string> files = SystemIOWrapper.EnumerateFiles(_testDirPath, "*.txt", SearchOption.TopDirectoryOnly)
            .Where(static path => SystemIOWrapper.GetLastSegment(path).StartsWith("keep"))
            .ToList();

        // Assert
        Assert.Single(files);
        Assert.Equal(file1Path, files[0]);
    }

    [Fact]
    public void EnumerateFiles_Should_Return_Files()
    {
        // Arrange
        SystemIOWrapper.CreateDirectory(_testDirPath);
        SystemIOWrapper.WriteAllText(SystemIOWrapper.Combine(_testDirPath, "file.txt"), "content");

        // Act
        IEnumerable<string> files = SystemIOWrapper.EnumerateFiles(_testDirPath, "*.txt", SearchOption.TopDirectoryOnly);

        // Assert
        Assert.NotNull(files);
        Assert.Single(files);
    }
}
