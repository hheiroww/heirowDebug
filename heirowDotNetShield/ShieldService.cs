using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace HeirowDotNetShield;

internal sealed record ShieldRequest(string InputAssembly, string OutputDirectory);
internal sealed record ShieldResult(string OutputAssembly, string ReportPath);

internal static class ShieldService
{
    private const string ObfuscarVersion = "2.2.50";

    internal static async Task<ShieldResult> ProtectAsync(
        ShieldRequest request,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var inputAssembly = Path.GetFullPath(request.InputAssembly.Trim().Trim('"'));
        var outputDirectory = Path.GetFullPath(request.OutputDirectory.Trim().Trim('"'));
        ValidateRequest(inputAssembly, outputDirectory);

        var sourceDirectory = Path.GetDirectoryName(inputAssembly)!;
        progress?.Report($"CHECK managed assembly: {inputAssembly}");
        var assemblyInfo = InspectAssembly(inputAssembly);
        if (assemblyInfo.StrongNameSigned)
        {
            throw new InvalidOperationException("Strong-name signed assemblies require a signing-key workflow. This build was left unchanged.");
        }

        if (Directory.Exists(outputDirectory) && Directory.EnumerateFileSystemEntries(outputDirectory).Any())
        {
            throw new InvalidOperationException("The protected output directory must be empty so existing files cannot be overwritten.");
        }

        Directory.CreateDirectory(outputDirectory);
        progress?.Report("COPY  application deployment (PDB files excluded)");
        CopyDeployment(sourceDirectory, outputDirectory, cancellationToken);

        var outputAssembly = Path.Combine(outputDirectory, Path.GetFileName(inputAssembly));
        var workDirectory = Path.Combine(Path.GetTempPath(), "heirowDotNetShield", Guid.NewGuid().ToString("N"));
        var obfuscatedDirectory = Path.Combine(workDirectory, "obfuscated");
        Directory.CreateDirectory(obfuscatedDirectory);

        try
        {
            var configPath = Path.Combine(workDirectory, "obfuscar.xml");
            WriteObfuscarConfiguration(configPath, outputDirectory, obfuscatedDirectory, outputAssembly);
            progress?.Report("OBF   private metadata names and managed string literals");
            await RunObfuscarAsync(configPath, workDirectory, cancellationToken);

            var obfuscatedAssembly = Path.Combine(obfuscatedDirectory, Path.GetFileName(outputAssembly));
            if (!File.Exists(obfuscatedAssembly))
            {
                throw new InvalidOperationException("The obfuscator did not produce the expected protected assembly.");
            }

            File.Copy(obfuscatedAssembly, outputAssembly, overwrite: true);
            DeletePdbFiles(outputDirectory);

            progress?.Report("HOST  disable startup hooks, managed debugger, and metadata updates");
            var runtimeConfigPath = PatchRuntimeConfiguration(outputAssembly);
            InstallNativeLauncher(outputAssembly);

            progress?.Report("HASH  write integrity inventory");
            var manifestPath = Path.Combine(outputDirectory, "heirowDotNetShield.sha256");
            var reportPath = await WriteReportAsync(
                outputDirectory,
                outputAssembly,
                runtimeConfigPath,
                manifestPath,
                assemblyInfo.AuthenticodeSigned,
                cancellationToken);
            await WriteIntegrityManifestAsync(outputDirectory, cancellationToken);

            progress?.Report("VERIFY protected assembly is readable and runtime policy is present");
            _ = InspectAssembly(outputAssembly);
            VerifyRuntimeConfiguration(runtimeConfigPath);
            return new ShieldResult(outputAssembly, reportPath);
        }
        catch
        {
            TryWriteFailureMarker(outputDirectory);
            throw;
        }
        finally
        {
            try
            {
                Directory.Delete(workDirectory, recursive: true);
            }
            catch
            {
                // Temporary cleanup must not hide a completed protection result.
            }
        }
    }

    private static void ValidateRequest(string inputAssembly, string outputDirectory)
    {
        if (!File.Exists(inputAssembly) || !string.Equals(Path.GetExtension(inputAssembly), ".dll", StringComparison.OrdinalIgnoreCase))
        {
            throw new FileNotFoundException("Select the main .NET application DLL from its build output.", inputAssembly);
        }

        var sourceDirectory = Path.GetDirectoryName(inputAssembly)!;
        var relativeOutput = Path.GetRelativePath(sourceDirectory, outputDirectory);
        if (!relativeOutput.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) && relativeOutput != "..")
        {
            throw new InvalidOperationException("Choose an output directory outside the source deployment directory.");
        }
    }

    private static (bool StrongNameSigned, bool AuthenticodeSigned) InspectAssembly(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new PEReader(stream);
        if (!reader.HasMetadata || reader.GetMetadataReader().MetadataKind != MetadataKind.Ecma335)
        {
            throw new InvalidOperationException("The selected file is not a supported managed ECMA-335 assembly.");
        }

        var corFlags = reader.PEHeaders.CorHeader?.Flags ?? 0;
        var strongNameSigned = (corFlags & CorFlags.StrongNameSigned) != 0;
        var authenticodeSigned = reader.PEHeaders.PEHeader?.CertificateTableDirectory.Size > 0;
        return (strongNameSigned, authenticodeSigned);
    }

    private static void CopyDeployment(string sourceDirectory, string outputDirectory, CancellationToken cancellationToken)
    {
        foreach (var sourcePath in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(Path.GetExtension(sourcePath), ".pdb", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relative = Path.GetRelativePath(sourceDirectory, sourcePath);
            var destination = Path.Combine(outputDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(sourcePath, destination, overwrite: false);
        }
    }

    private static void WriteObfuscarConfiguration(string path, string inputDirectory, string outputDirectory, string assemblyPath)
    {
        var document = new XDocument(
            new XElement("Obfuscator",
                Variable("InPath", inputDirectory),
                Variable("OutPath", outputDirectory),
                Variable("KeepPublicApi", "true"),
                Variable("HidePrivateApi", "true"),
                Variable("RenameProperties", "false"),
                Variable("RenameEvents", "false"),
                Variable("HideStrings", "true"),
                Variable("ReuseNames", "true"),
                Variable("RegenerateDebugInfo", "false"),
                new XElement("Module", new XAttribute("file", assemblyPath))));
        document.Save(path);
    }

    private static XElement Variable(string name, string value) =>
        new("Var", new XAttribute("name", name), new XAttribute("value", value));

    private static async Task RunObfuscarAsync(string configPath, string workingDirectory, CancellationToken cancellationToken)
    {
        var executable = Path.Combine(AppContext.BaseDirectory, "Obfuscar", "Obfuscar.Console.exe");
        if (!File.Exists(executable))
        {
            var packageRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
            if (string.IsNullOrWhiteSpace(packageRoot))
            {
                packageRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
            }

            executable = Path.Combine(packageRoot, "obfuscar", ObfuscarVersion, "tools", "Obfuscar.Console.exe");
        }

        if (!File.Exists(executable))
        {
            throw new FileNotFoundException("The pinned Obfuscar engine was not found. Restore the heirowDotNetShield project first.", executable);
        }

        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add(configPath);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the offline obfuscation engine.");
        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await standardOutput;
        var error = await standardError;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Obfuscation failed with exit code {process.ExitCode}: {error}{Environment.NewLine}{output}".Trim());
        }
    }

    private static string PatchRuntimeConfiguration(string outputAssembly)
    {
        var basePath = Path.Combine(Path.GetDirectoryName(outputAssembly)!, Path.GetFileNameWithoutExtension(outputAssembly));
        var runtimeConfigPath = basePath + ".runtimeconfig.json";
        if (!File.Exists(runtimeConfigPath))
        {
            throw new FileNotFoundException("The main application runtimeconfig.json was not found beside the DLL.", runtimeConfigPath);
        }

        var root = JsonNode.Parse(File.ReadAllText(runtimeConfigPath))?.AsObject()
            ?? throw new InvalidOperationException("The runtime configuration is not valid JSON.");
        var runtimeOptions = root["runtimeOptions"] as JsonObject ?? new JsonObject();
        root["runtimeOptions"] = runtimeOptions;
        var properties = runtimeOptions["configProperties"] as JsonObject ?? new JsonObject();
        runtimeOptions["configProperties"] = properties;
        properties["System.StartupHookProvider.IsSupported"] = false;
        properties["System.Diagnostics.Debugger.IsSupported"] = false;
        properties["System.Reflection.Metadata.MetadataUpdater.IsSupported"] = false;

        File.WriteAllText(runtimeConfigPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return runtimeConfigPath;
    }

    private static void InstallNativeLauncher(string outputAssembly)
    {
        var launcherPath = Path.Combine(AppContext.BaseDirectory, "ShieldLauncher", "heirowDotNetShield.Launcher.exe");
        if (!File.Exists(launcherPath))
        {
            throw new FileNotFoundException("The native pre-runtime launcher is missing. Rebuild heirowDotNetShield in Release.", launcherPath);
        }

        var targetExecutable = Path.ChangeExtension(outputAssembly, ".exe");
        File.Copy(launcherPath, targetExecutable, overwrite: true);
    }

    private static void VerifyRuntimeConfiguration(string runtimeConfigPath)
    {
        var properties = JsonNode.Parse(File.ReadAllText(runtimeConfigPath))?["runtimeOptions"]?["configProperties"];
        foreach (var name in new[]
                 {
                     "System.StartupHookProvider.IsSupported",
                     "System.Diagnostics.Debugger.IsSupported",
                     "System.Reflection.Metadata.MetadataUpdater.IsSupported"
                 })
        {
            if (properties?[name]?.GetValue<bool>() != false)
            {
                throw new InvalidOperationException($"Runtime policy verification failed for {name}.");
            }
        }
    }

    private static void DeletePdbFiles(string outputDirectory)
    {
        foreach (var path in Directory.EnumerateFiles(outputDirectory, "*.pdb", SearchOption.AllDirectories))
        {
            File.Delete(path);
        }
    }

    private static async Task<string> WriteIntegrityManifestAsync(string outputDirectory, CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(outputDirectory, "heirowDotNetShield.sha256");
        var lines = new List<string>();
        foreach (var path in Directory.EnumerateFiles(outputDirectory, "*", SearchOption.AllDirectories)
                     .Where(path => !string.Equals(path, manifestPath, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            await using var stream = File.OpenRead(path);
            var hash = await SHA256.HashDataAsync(stream, cancellationToken);
            lines.Add($"{Convert.ToHexString(hash)}  {Path.GetRelativePath(outputDirectory, path).Replace('\\', '/')}");
        }

        await File.WriteAllLinesAsync(manifestPath, lines, cancellationToken);
        return manifestPath;
    }

    private static async Task<string> WriteReportAsync(
        string outputDirectory,
        string outputAssembly,
        string runtimeConfigPath,
        string manifestPath,
        bool sourceWasAuthenticodeSigned,
        CancellationToken cancellationToken)
    {
        var reportPath = Path.Combine(outputDirectory, "heirowDotNetShield-report.json");
        var report = new
        {
            product = "heirowDotNetShield",
            protectedAtUtc = DateTimeOffset.UtcNow,
            assembly = Path.GetFileName(outputAssembly),
            runtimeConfig = Path.GetFileName(runtimeConfigPath),
            integrityManifest = Path.GetFileName(manifestPath),
            controls = new[]
            {
                "startup-hook-disabled",
                "diagnostic-ipc-disabled-by-native-launcher",
                "managed-debugger-disabled",
                "metadata-updater-disabled",
                "private-symbols-obfuscated",
                "string-literals-hidden",
                "pdb-files-removed"
            },
            signingNotice = sourceWasAuthenticodeSigned
                ? "The input carried an Authenticode signature. The protected output must be signed again after rewriting."
                : "The output is not Authenticode-signed. Sign the final deployment before distribution."
        };
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        return reportPath;
    }

    private static void TryWriteFailureMarker(string outputDirectory)
    {
        try
        {
            File.WriteAllText(Path.Combine(outputDirectory, "PROTECTION_FAILED.txt"),
                "heirowDotNetShield did not complete. Do not distribute this output. The source build was not modified.");
        }
        catch
        {
            // Preserve the original error.
        }
    }
}
