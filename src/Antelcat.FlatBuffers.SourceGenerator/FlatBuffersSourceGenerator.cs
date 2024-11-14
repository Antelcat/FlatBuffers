using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Antelcat.FlatBuffers.SourceGenerator;

[Generator]
public class FlatBuffersSourceGenerator : IIncrementalGenerator
{
    /// <summary>
    /// /analyzer/dotnet/cs
    /// </summary>
    private static readonly string Current =
        Path.GetDirectoryName(typeof(FlatBuffersSourceGenerator).Assembly.Location)!;

    private static readonly object RunLocker = new();

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
        var flatcProvider = context.SyntaxProvider.ForAttributeWithMetadataName(
            $"{FlatcLocationAttributeGenerator.Namespace}.{FlatcLocationAttributeGenerator.FlatcLocation}",
            (_, _) => true,
            (n, _) => n);
        var flatcArgumentsProvider = context.SyntaxProvider.ForAttributeWithMetadataName(
            $"{FlatcLocationAttributeGenerator.Namespace}.{FlatcLocationAttributeGenerator.FlatcArguments}",
            (_, _) => true,
            (n, t) => n);
        var flatcReplaceProvider =context.SyntaxProvider.ForAttributeWithMetadataName(
            $"{FlatcLocationAttributeGenerator.Namespace}.{FlatcLocationAttributeGenerator.FlatcReplaces}",
            (_, _) => true,
            (n, t) => n);
        string[] args;
        string?  flatc = null;
        context.RegisterSourceOutput(
            context.AdditionalTextsProvider.Collect()
                .Combine(flatcProvider.Collect()
                    .Combine(flatcArgumentsProvider.Collect()
                        .Combine(flatcReplaceProvider.Collect()))),
            (c, s) =>
            {
                var flatcLocations = s.Right.Left;
                var flatcArguments = s.Right.Right.Left;
                var flatcReplaces  = s.Right.Right.Right;
                var stamp          = Guid.NewGuid().ToString().Replace("-", "");
                var tempPath       = Path.Combine(Current, stamp);
                if (flatc is null) //get flatc
                {
                    if (!flatcLocations.Any() || flatcLocations.FirstOrDefault()
                            .Attributes
                            .FirstOrDefault(static x =>
                                x.AttributeClass?.Name == FlatcLocationAttributeGenerator.FlatcLocation)
                            ?.ConstructorArguments.FirstOrDefault().Value is not string tmp)
                    {
                        CreateNative();
                        flatc = Flatc;
                    }
                    else if (!Path.IsPathRooted(tmp))
                    {
                        var loc  = flatcLocations.FirstOrDefault().TargetNode.GetLocation();
                        var span = loc.GetMappedLineSpan().ToString();
                        var file = span.Substring(0, span.LastIndexOf(' ') - 1);
                        var dir  = Path.Combine(Path.GetDirectoryName(file)!, tmp);
                        flatc = Path.GetFullPath(dir);
                    }
                    else
                    {
                        flatc = tmp;
                    }
                }

                var flatArgument = flatcArguments
                    .SelectMany(static x => x.Attributes)
                    .FirstOrDefault(static x =>
                        x.AttributeClass?.Name == FlatcLocationAttributeGenerator.FlatcArguments);
                args = flatArgument?
                    .ConstructorArguments
                    .FirstOrDefault()
                    .Values
                    .IsDefaultOrEmpty is true
                    ? []
                    : flatArgument!
                        .ConstructorArguments
                        .FirstOrDefault()
                        .Values
                        .Select(x => x.Value as string ?? "")
                        .ToArray();

                var tempDir = new DirectoryInfo(tempPath);
                tempDir.Create();

                var flatReplaces = flatcReplaces
                    .SelectMany(static x => x.Attributes)
                    .Where(static x =>
                        x.AttributeClass?.Name == FlatcLocationAttributeGenerator.FlatcReplaces)
                    .Select(static x => (
                            from: x.ConstructorArguments[0].Value as string,
                            to: x.ConstructorArguments[1].Value as string
                        )
                    ).ToArray();

                if (!Generate(tempPath,
                    flatc,
                    string.Join(" ", args),
                    s.Left.Where(static x => Path.GetExtension(x.Path) == ".fbs")
                        .Select(x => x.Path).ToArray()))
                {
                    return;
                }

                foreach (var (name, content) in Collect(tempPath))
                {
                    var fin = flatReplaces.Aggregate(content, (a, config)
                        => a.Replace(config.from!, config.to!));
                    c.AddSource(name.Replace('/', '.').Replace('\\', '.'),
                        SyntaxFactory.ParseCompilationUnit(fin).GetText(Encoding.UTF8));
                }

                Retry(() => tempDir.Delete(true), static ex => ex is IOException);
            });
    }

    private static bool Generate(string outputDir,
                                 string flatc,
                                 string? arguments = null,
                                 params string[] fbs)
    {
        foreach (var rest in ShorterThan(fbs,
            2080 - flatc.Length - $"-n {arguments}".Length, // rest length for fbs
            (each, _) => ' ' + each))
        {
            lock (RunLocker)
            {
                try
                {
                    var process = Process.Start(new ProcessStartInfo
                    {
                        FileName         = flatc,
                        WorkingDirectory = outputDir,
                        Arguments        = $"-n {arguments}{rest}",
                        CreateNoWindow   = true,
                    });
                    while (process?.HasExited is false)
                    {
                    }
                }
                catch (Win32Exception)
                {
                    return false;
                }
            }
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

    private static IEnumerable<(string name, string content)> Collect(string directory)
    {
        foreach (var file in CollectCSharpFiles(directory))
        {
            using var stream =
                Retry(() => new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read),
                    ex => ex is IOException);
            if (stream is null) continue;
            using var reader = new StreamReader(stream);
            yield return (file.FullName.Substring(directory.Length + 1), reader.ReadToEnd());
        }
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

    private static void Retry(Action action, Func<Exception, bool> again)
    {
        while (true)
        {
            try
            {
                action();
                break;
            }
            catch (Exception ex)
            {
                if (!again(ex))
                {
                    break;
                }
            }
        }
    }

    private static T? Retry<T>(Func<T> func, Func<Exception, bool> again)
    {
        while (true)
        {
            try
            {
                return func();
            }
            catch (Exception ex)
            {
                if (!again(ex))
                {
                    break;
                }
            }
        }

        return default;
    }

    private static IEnumerable<string> ShorterThan(IEnumerable<string> inputs,
                                                   int max,
                                                   Func<string, int, string>? modify = null)
    {
        var sb = new StringBuilder();
        foreach (var (input, index) in inputs.Select(static (x, i) => (x, i)))
        {
            var next = modify?.Invoke(input, index) ?? input;
            if (next.Length + sb.Length > max)
            {
                yield return sb.ToString();
                sb = new StringBuilder(modify?.Invoke(input, index) ?? input);
            }
            else
            {
                sb = sb.Append(next);
            }
        }

        yield return sb.ToString();
    }
}