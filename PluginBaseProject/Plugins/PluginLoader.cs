using Contract;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace PluginBaseProject.Plugins
{

    public record LoadResult(bool Success, string Message)
    {
        public static LoadResult Fail(string message) => new(false, message);
    }


    /* TODO:
     * 
     * 1) See how Prism might fit into this and improve it
     * 2) Look into Assembly "Unload" at runtime (Brad wants that) 
     * 3) How can I handle "Updating" a plugin at runtime? Say Plugin A is v 1.0.1 and I am going to pass in v1.0.2.
     * 
     * I might also build a parallel application that doesn't use MEF at all and JUST uses Prism 8 with DryIoc like Mat's doing, and see how that looks.
     * 
     * */
    public class PluginLoader
    {
        private readonly string _pluginDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PluginBaseProject", "Plugins");
        private const string _manifestFileName = "pkg-manifest.json";
        private const string _manifestSignatureFileName = "pkg-manifest.json.sig";
        private readonly AggregateCatalog _catalog = new();
        private readonly CompositionContainer _container;
        private readonly Dictionary<string, PluginLoadContext> _contexts = new(StringComparer.OrdinalIgnoreCase);
        private IEnumerable<IMagicPlugin> _plugins = [];

        public PluginLoader()
        {
            if (!Directory.Exists(_pluginDir))
                Directory.CreateDirectory(_pluginDir);

            _container = new CompositionContainer(_catalog);
            _container.ComposeParts(this);
        }

        [ImportMany(AllowRecomposition = true)]
        public IEnumerable<IMagicPlugin> Plugins
        {
            get => _plugins;
            set
            {
                // could probably diff the two collections and find the new plugins, and then raise an event for each new one, that'd be smarter
                _plugins = value;
                PluginsChanged?.Invoke();
            }
        }

        public event Action? PluginsChanged;

        public LoadResult InstallNewPlugin(string package)
        {
            LoadResult? result;
            string pluginDir = CreatePluginDirectory(Path.GetFileNameWithoutExtension(package));
            try
            {
                ZipFile.ExtractToDirectory(package, pluginDir);
                result = TryLoad(pluginDir);
            }
            catch (Exception ex)
            {
                return new LoadResult(false, $"Unhandled {ex.GetType().Name} while installing new plugin: {ex.Message}");
            }

            if (!result.Success)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                if (Directory.Exists(pluginDir))
                {
                    try { Directory.Delete(pluginDir, true); }
                    catch { /* maybe I should do something here... But not yet! */ }
                }
            }
            return result;
        }

        public IReadOnlyDictionary<string, LoadResult> LoadAllPlugins()
        {
            if (_plugins.Count() > 0) return new Dictionary<string, LoadResult>();

            var results = new Dictionary<string, LoadResult>();
            int i = 1;
            foreach (var manifest in Directory.EnumerateFiles(_pluginDir, _manifestFileName, SearchOption.TopDirectoryOnly))
            {
                var dir = Path.GetDirectoryName(manifest);
                if (dir == null)
                {
                    results[$"UNKNOWN{i++}"] = LoadResult.Fail($"Manifest file {manifest} has no directory??");
                    continue;
                }

                string plugin = Path.GetFileName(dir) ?? $"UNKNOWN{i++}";
                results[plugin] = TryLoad(dir);
            }
            return results;
        }

        private LoadResult TryLoad(string pluginDir)
        {
            PluginLoadContext? ctx = null;

            LoadResult Reject(string message)
            {
                ctx?.Unload(); 
                return LoadResult.Fail($"{Path.GetFileName(pluginDir)}: {message}");
            }

            int pluginCount;

            try
            {
                var manifestFile = Path.Combine(pluginDir, _manifestFileName);
                var signature = Path.Combine(pluginDir, _manifestSignatureFileName);

                Dictionary<string, string> manifest;
                try { manifest = GetSignedManifest(manifestFile, signature); }
                catch (PluginException ex) { return Reject(ex.Message); }
                catch (Exception ex) { return Reject($"Unexpected {ex.GetType().Name} reading manifest: {ex.Message}"); }

                string? mainDll = null;
                try { mainDll = ValidateManifestContents(manifest, pluginDir); }
                catch (PluginException ex) { return Reject(ex.Message); }
                catch (Exception ex) { return Reject($"Unexpected {ex.GetType().Name} validating manifest contents: {ex.Message}"); }

                if (ExtraFilesInPackage(manifest, pluginDir))
                    return Reject("The package contains files not listed in the manifest.");

                if (mainDll == null)
                    return Reject("Manifest does not specify a main DLL file.");

                var key = Path.GetFileNameWithoutExtension(mainDll);
                if (_contexts.ContainsKey(key)) 
                    return Reject($"{key} is already loaded.");

                mainDll = Path.Combine(pluginDir, Path.GetFileName(mainDll));
                ctx = new PluginLoadContext(mainDll);

                Assembly asm;
                try { asm = ctx.LoadFromAssemblyPath(mainDll); }
                catch (BadImageFormatException) { return Reject($"{key} is not a managed .NET assembly."); }

                using (var tempContainer = new CompositionContainer(new AssemblyCatalog(asm)))
                {
                    List<IMagicPlugin> found;
                    try
                    {
                        found = tempContainer.GetExportedValues<IMagicPlugin>().ToList();
                    }
                    catch (CompositionException ex)
                    {
                        return Reject($"MEF composition failed: {ex.Message}");
                    }

                    if (found.Count == 0)
                        return Reject("no [Export(typeof(IMagicPlugin))] found " +
                                      "(is it referencing the same contract assembly?).");

                    foreach (var p in found)
                    {
                        if (string.IsNullOrWhiteSpace(p.Name))
                            return Reject("a plugin has an empty Name.");
                        if (found.Count(x => x.Name == p.Name) > 1 || Plugins.Any(x => x.Name == p.Name))
                            return Reject($"a plugin named '{p.Name}' is already loaded.");
                    }

                    pluginCount = found.Select(p => p.Name).Count();
                }

                _catalog.Catalogs.Add(new AssemblyCatalog(asm));
                _contexts[key] = ctx;

                return new LoadResult(true, $"{Path.GetFileName(pluginDir)}: loaded {pluginCount} plugin(s).");
            }
            catch (Exception ex)
            {
                return Reject($"load failed ({ex.GetType().Name}: {ex.Message}).");
            }
        }

        private static byte[]? ReadSignature(string sigFilePath) =>
            File.Exists(sigFilePath) ? File.ReadAllBytes(sigFilePath) : null;

        private string CreatePluginDirectory(string pluginName)
        {
            var pluginDir = Path.Combine(_pluginDir, pluginName);
            if (!Directory.Exists(pluginDir)) Directory.CreateDirectory(pluginDir);
            return pluginDir;
        }

        private Dictionary<string, string> GetSignedManifest(string manifestFile, string signatureFile)
        {
            if (!File.Exists(manifestFile))
                throw new PluginException($"Package does not have a manifest.");

            if (!File.Exists(signatureFile))
                throw new PluginException($"Package does not have a manifest signature.");

            if (!PluginSignature.Verify(File.ReadAllBytes(manifestFile), ReadSignature(signatureFile)))
                throw new PluginException("Manifest signature check failed.");

            var manifestContents = File.ReadAllText(manifestFile);
            var manifest = JsonSerializer.Deserialize<Dictionary<string, string>>(manifestContents);

            if (manifest == null || manifest.Count == 0)
                throw new PluginException("Could not read the manifest.");

            return manifest;
        }

        // this name and return value don't make any fucking sense, I need to refactor this so it's obvious what's happening.
        // maytbe return the maindll name as an outvar
        private string? ValidateManifestContents(Dictionary<string, string> manifest, string dir)
        {
            string? mainDll = null;

            if (!manifest.ContainsKey("mainDllFile"))
                throw new PluginException("Manifest does not specify a main DLL file.");

            foreach (var entry in manifest)
            {
                if (string.Equals(entry.Key, "mainDllFile", StringComparison.OrdinalIgnoreCase))
                {
                    var dll = Path.Combine(dir, entry.Value);

                    if (!File.Exists(dll))
                        throw new PluginException($"File in the manifest was not present in the directory: {Path.GetFileName(dll)}");

                    mainDll = dll;
                }
                else
                {
                    var dll = Path.Combine(dir, entry.Key);

                    if (!File.Exists(dll))
                        throw new PluginException($"File in the manifest was not present in the directory: {Path.GetFileName(dll)}");

                    using FileStream stream = File.OpenRead(dll);
                    var hash = SHA256.HashData(stream);
                    if (!string.Equals(Convert.ToHexStringLower(hash), entry.Value))
                        throw new PluginException($"The contents of a file did not match the manifest: {Path.GetFileName(dll)}");
                }
            }
            return mainDll;
        }

        private bool ExtraFilesInPackage(Dictionary<string, string> manifest, string dir)
        {
            var files = Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                var relativePath = Path.GetRelativePath(dir, file)
                    .Replace('\\', Path.DirectorySeparatorChar)
                    .Replace('/', Path.DirectorySeparatorChar);
                // the manifest and sig files won't be in there so skip 'em
                string fullManifestPath = Path.Combine(dir, _manifestFileName);
                string fullSignaturePath = Path.Combine(dir, _manifestSignatureFileName);

                if (string.Equals(Path.GetFullPath(fullManifestPath), Path.GetFullPath(file), StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Path.GetFullPath(fullSignaturePath), Path.GetFullPath(file), StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!manifest.ContainsKey(relativePath))
                    return true;
            }
            return false;
        }
    }
}
