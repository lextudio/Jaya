using System;

namespace Jaya.Ui
{
    public class DirectPathNavigationRequest : EventArgs
    {
        public DirectPathNavigationRequest(string path)
        {
            Path = path;
        }

        public string Path { get; }
    }
}
