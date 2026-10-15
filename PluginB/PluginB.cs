using Contract;
using SubPlugin;
using System.ComponentModel.Composition;

namespace PluginB
{
    [Export(typeof(IMagicPlugin))]
    public class PluginB : IMagicPlugin
    {
        public string Name => "Plugin B";
        public string Message => NotTheInterface.NewMessage;
    }
}
