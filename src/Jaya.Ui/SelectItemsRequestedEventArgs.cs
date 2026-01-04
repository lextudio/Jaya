using System;
using System.Collections.Generic;

namespace Jaya.Ui
{
    public class SelectItemsRequestedEventArgs : EventArgs
    {
        public SelectItemsRequestedEventArgs(IReadOnlyList<string> paths)
        {
            Paths = paths ?? Array.Empty<string>();
        }

        public IReadOnlyList<string> Paths { get; }
    }
}
