using System;
using System.Collections.Generic;
using System.Text;

namespace PluginBaseProject.Plugins
{
    internal class PluginException : Exception
    {
        public PluginException(string message) : base(message) { }
    }
}
