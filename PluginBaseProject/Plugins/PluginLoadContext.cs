using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace PluginBaseProject.Plugins
{
    public class PluginLoadContext : AssemblyLoadContext
    {
        public PluginLoadContext(string filePath) : base(Path.GetFileName(filePath), isCollectible: true) { resolver = new(filePath); }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (string.Equals(assemblyName.Name, "Contract", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (string.Equals(assemblyName.Name, "System.ComponentModel.Composition", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var assemblyPath = resolver.ResolveAssemblyToPath(assemblyName);
            if (!string.IsNullOrEmpty(assemblyPath))
            {
                return LoadFromAssemblyPath(assemblyPath);
            }

            return null;
        }

        AssemblyDependencyResolver resolver;
    }
}
