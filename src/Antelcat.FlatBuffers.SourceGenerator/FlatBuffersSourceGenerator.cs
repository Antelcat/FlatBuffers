using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Antelcat.FlatBuffers.SourceGenerator;

[Generator]
public class FlatBuffersSourceGenerator : IIncrementalGenerator
{
    /// <summary>
    /// /analyzer/dotnet/cs
    /// </summary>
    private static readonly string Current =
        Path.GetDirectoryName(typeof(FlatBuffersSourceGenerator).Assembly.Location)!;

    private static readonly string Generated = Path.Combine(Current, nameof(Generated));

    /// <summary>
    /// /analyzer/dotnet/tool/{arch}/flatc
    /// </summary>
    private static readonly string Flatc = Path.Combine(Current,
        "flatc-" +
        (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "win.exe"
            : RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                ? "linux"
                : RuntimeInformation.IsOSPlatform(
                    OSPlatform.OSX)
                    ? "osx"
                    : throw new InvalidOperationException()));

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var provider = context.SyntaxProvider.ForAttributeWithMetadataName(
            $"{FlatcLocationAttributeGenerator.Namespace}.{FlatcLocationAttributeGenerator.AttributeName}",
            (n, t) => true,
            (n, t) => n);
        context.RegisterSourceOutput(context.AdditionalTextsProvider.Collect().Combine(provider.Collect()),
            (c, s) =>
            {
                var attr = s.Right.SelectMany(x => x.Attributes)
                    .FirstOrDefault(x => x.AttributeClass?.Name == FlatcLocationAttributeGenerator.AttributeName);
                if (attr?.ConstructorArguments.FirstOrDefault().Value is not string flatc)
                {
                    CreateNative();
                    flatc = Flatc;
                }

                foreach (var additionalText in s.Left.Where(static x => Path.GetExtension(x.Path) == ".fbs"))
                {
                    if (!Run(additionalText.Path, flatc)) break;
                }

                foreach (var (name, content) in Collect())
                {
                    c.AddSource(name.Replace('/', '.').Replace('\\', '.'),
                        SyntaxFactory.ParseCompilationUnit(content).GetText(Encoding.UTF8));
                }
            });
    }

    private static bool Run(string path, string flatc)
    {
        new DirectoryInfo(Generated).Create();
        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName         = flatc,
                WorkingDirectory = Generated,
                Arguments        = $"-n {path}",
                CreateNoWindow   = true,
            });
            process?.WaitForExit();
        }
        catch (Win32Exception ex)
        {
            return false;
        }

        return true;
    }

    private static IEnumerable<FileInfo> CollectCSharpFiles(string directory)
    {
        var dir = new DirectoryInfo(directory);
        foreach (var file in dir.GetFiles("*.cs"))
        {
            yield return file;
        }

        foreach (var directoryInfo in dir.GetDirectories())
        {
            foreach (var file in CollectCSharpFiles(directoryInfo.FullName))
            {
                yield return file;
            }
        }
    }

    private static IEnumerable<(string name, string content)> Collect()
    {
        foreach (var file in CollectCSharpFiles(Current))
        {
            using var stream = new FileStream(file.FullName, FileMode.Open);
            using var reader = new StreamReader(stream);
            var       text   = reader.ReadToEnd();
            yield return (file.FullName.Substring(Generated.Length + 1), text);
        }

        new DirectoryInfo(Generated).Delete(true);
    }

    private static void WriteNotExist(string name, Stream source)
    {
        var full = Path.Combine(Current, name);
        try
        {
            using var write = new FileStream(full, FileMode.Open); //already exists ?
        }
        catch (FileNotFoundException)
        {
            using var write  = new FileStream(full, FileMode.Create);
            var       buffer = new byte[source.Length];
            var       length = source.Read(buffer, 0, buffer.Length);
            write.Write(buffer, 0, length);
        }
    }

    private static void CreateNative()
    {
        var ass = typeof(FlatBuffersSourceGenerator).Assembly;
        foreach (var name in ass.GetManifestResourceNames())
        {
            if (!name.Contains("flatc")) continue;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && name.Contains("win"))
            {
                using var stream = ass.GetManifestResourceStream(name);
                if (stream is null) continue;
                WriteNotExist(Path.GetFileName(Flatc), stream);
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && name.Contains("linux"))
            {
                using var stream = ass.GetManifestResourceStream(name);
                if (stream is null) continue;
                WriteNotExist(Path.GetFileName(Flatc), stream);
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && name.Contains("osx"))
            {
                using var stream = ass.GetManifestResourceStream(name);
                if (stream is null) continue;
                WriteNotExist(Path.GetFileName(Flatc), stream);
            }
        }
    }
}