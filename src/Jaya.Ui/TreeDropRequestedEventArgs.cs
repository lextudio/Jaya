using System;
using Jaya.Shared.Models;
using Avalonia.Input;

namespace Jaya.Ui
{
    public class TreeDropRequestedEventArgs : EventArgs
    {
        public TreeDropRequestedEventArgs(string[] sourcePaths, DirectoryModel? targetDirectory, DragDropEffects effect)
        {
            SourcePaths = sourcePaths;
            TargetDirectory = targetDirectory;
            Effect = effect;
        }

        public string[] SourcePaths { get; }
        public DirectoryModel? TargetDirectory { get; }
        public DragDropEffects Effect { get; }
    }
}
