using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;


const string _manifestFileName = "pkg-manifest.json";

if (args is ["keygen"])
{
    using var rsa = RSA.Create(3072);
    File.WriteAllText("publisher.private.pem", rsa.ExportPkcs8PrivateKeyPem());
    File.WriteAllText("publisher.public.pem", rsa.ExportSubjectPublicKeyInfoPem());
    Console.WriteLine("Created public and private keys");
}
else if (args is ["sign", var dll])
{
    if (!File.Exists(dll))
    {
        Console.WriteLine("File does not exist.");
        return;
    }
    using var rsa = RSA.Create();
    rsa.ImportFromPem(File.ReadAllText("publisher.private.pem"));
    var sig = rsa.SignData(File.ReadAllBytes(dll), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    File.WriteAllBytes($"{dll}.sig", sig);
    Console.WriteLine($"Signed DLL {dll}");
}
else if (args is ["package", var mainDll])
{
    if (!File.Exists(mainDll))
    {
        Console.WriteLine("File does not exist.");
        return;
    }

    var dir = Path.GetDirectoryName(mainDll);
    if (string.IsNullOrEmpty(dir)) return;

    var packageFile = Path.Combine(dir, Path.ChangeExtension(Path.GetFileNameWithoutExtension(mainDll), ".mppk"));
    if (File.Exists(packageFile)) File.Delete(packageFile);

    var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);

    try
    {
        CopyDirectory(dir, tempDir, true);
        Dictionary<string, string> manifest = [];
        manifest.Add("mainDllFile", Path.GetFileName(mainDll));

        var files = Directory.EnumerateFiles(tempDir, "*.*", SearchOption.AllDirectories);

        foreach (var file in files)
        {
            if (IsIgnorableExtension(file) || file.Contains(_manifestFileName)) continue;
            using FileStream stream = File.OpenRead(file);
            var hash = SHA256.HashData(stream);
            string relativePath = Path.GetRelativePath(tempDir, file)
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);
            manifest.Add(relativePath, Convert.ToHexStringLower(hash));
        }

        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions() { WriteIndented = true });
        string manifestFile = Path.Combine(tempDir, _manifestFileName);
        File.WriteAllText(manifestFile, json);

        using var rsa = RSA.Create();
        rsa.ImportFromPem(File.ReadAllText("publisher.private.pem"));
        var sig = rsa.SignData(File.ReadAllBytes(manifestFile), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        File.WriteAllBytes($"{manifestFile}.sig", sig);

        ZipFile.CreateFromDirectory(tempDir, packageFile);
        Console.WriteLine($"Created MPPK Package {packageFile}");
    }
    finally
    {
        Directory.Delete(tempDir, recursive: true);
    }

    // get the parent directory
    // copy each file to a temporary directory
    // create a dictionary of string-string
    // foreach file, hash the file contents and add the filename and file hash to the dictionary as key/value respectively
    // the manifest should have an entry for the "MainDll" as well and it should be the DLL referenced here.
    // save the manifest to the temporary directory
    // sign the manifest itself
    // zip up the file, give it a special non-zip extension like .mppk
}
else
{
    Console.WriteLine("Bad args! You fool!");
}

static bool IsIgnorableExtension(string filePath)
{
    string[] ignorableExtensions = new string[] { ".sig", ".mppk" };
    string extension = Path.GetExtension(filePath);
    return ignorableExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
}

// shamelessly ripped straight from Microsoft learn docs: https://learn.microsoft.com/en-us/dotnet/standard/io/how-to-copy-directories
static void CopyDirectory(string sourceDir, string destinationDir, bool recursive)
{
    // Get information about the source directory
    var dir = new DirectoryInfo(sourceDir);

    // Check if the source directory exists
    if (!dir.Exists)
        throw new DirectoryNotFoundException($"Source directory not found: {dir.FullName}");

    // Cache directories before we start copying
    DirectoryInfo[] dirs = dir.GetDirectories();

    // Create the destination directory
    Directory.CreateDirectory(destinationDir);

    // Get the files in the source directory and copy to the destination directory
    foreach (FileInfo file in dir.GetFiles())
    {
        string targetFilePath = Path.Combine(destinationDir, file.Name);
        file.CopyTo(targetFilePath);
    }

    // If recursive and copying subdirectories, recursively call this method
    if (recursive)
    {
        foreach (DirectoryInfo subDir in dirs)
        {
            string newDestinationDir = Path.Combine(destinationDir, subDir.Name);
            CopyDirectory(subDir.FullName, newDestinationDir, true);
        }
    }
}