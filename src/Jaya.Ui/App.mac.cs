// mac-specific helper for Dock menu actions
using System;
#if NET10_0_MACOS
using AppKit;
#endif
using Avalonia.Threading;

namespace Jaya.Ui
{
    public partial class App
    {
#if NET10_0_MACOS
        public static void OpenNewJayaWindow()
        {
            try
            {
                Dispatcher.UIThread.Post(() =>
                {
                    var w = new MainView();
                    w.Show();
                });
            }
            catch { }
        }

        // Exposed to Objective-C if needed by native delegate
        [Foundation.Export("openNewJayaWindow:")]
        public void OpenNewJayaWindowObjC(Foundation.NSObject sender)
        {
            OpenNewJayaWindow();
        }
#endif
    }
}
