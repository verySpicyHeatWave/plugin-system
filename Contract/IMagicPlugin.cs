using System;
using System.Collections.Generic;
using System.Text;

namespace Contract
{
    public interface IMagicPlugin
    {
        string Name { get; }
        string Message { get; }
    }
}
