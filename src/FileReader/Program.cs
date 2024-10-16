if (args.Length == 0) return 1;
var path = args[0];
if (!Directory.Exists(path)) return 2;
foreach (var file in GetCSharpFiles(path))
{
    var relative = Path.GetRelativePath(path, file.FullName);
    await Console.Out.WriteLineAsync($"-n{relative}");
    foreach (var line in await File.ReadAllLinesAsync(file.FullName))
    {
        await Console.Out.WriteLineAsync($"-c{line}");
    }
    await Console.Out.WriteLineAsync("-f");
    file.Delete();
}

return 0;


IEnumerable<FileInfo> GetCSharpFiles(string directory)
{
    var dir = new DirectoryInfo(directory);
    foreach (var file in dir.GetFiles("*.cs"))
    {
        yield return file;
    }
    foreach (var directoryInfo in dir.GetDirectories())
    {
        foreach (var file in GetCSharpFiles(directoryInfo.FullName))
        {
            yield return file;
        }
    }
}