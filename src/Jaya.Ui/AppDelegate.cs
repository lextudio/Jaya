#if NET10_0_MACOS
using System;
using AppKit;
using Foundation;

namespace Jaya.Ui
{
    [Register("AppDelegate")]
    public class AppDelegate : NSApplicationDelegate
    {
        public override NSMenu ApplicationDockMenu(NSApplication sender)
        {
            var menu = new NSMenu();

            var newItem = new NSMenuItem("New Jaya Window", new ObjCRuntime.Selector("newJayaWindow:"), "");
            newItem.Target = this;
            menu.AddItem(newItem);

            return menu;
        }

        [Export("newJayaWindow:")]
        void NewJayaWindow(NSObject sender)
        {
            App.OpenNewJayaWindow();
        }
    }
}
#endif
