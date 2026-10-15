using Contract;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Text;

namespace PluginA
{
    [Export(typeof(IMagicPlugin))]
    internal class PluginA2 : IMagicPlugin
    {
        public string Name => "Plugin A2";

        public string Message => "You mean to tell me that one plugin is forty plugins?!";
        
    }
}
