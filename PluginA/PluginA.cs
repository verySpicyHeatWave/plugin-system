using Contract;
using System.ComponentModel.Composition;

namespace PluginA
{
    [Export(typeof(IMagicPlugin))]
    public class PluginA : IMagicPlugin
    {
        public string Name => "Plugin A";

        public string Message => "This message is from Plugin A! Wow, isn't that something?!";
    }
}
