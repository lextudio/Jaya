//
// Copyright (c) Rubal Walia. All rights reserved.
// Licensed under the 3-Clause BSD license. See LICENSE file in the project root for full license information.
//
using System;
using System.Collections;
using System.Collections.Generic;
using Avalonia.Controls;

namespace Jaya.Ui.Helpers
{
    static class NativeMenuHelper
    {
        public static NativeMenu BuildNativeMenu(Menu menu)
        {
            if (menu == null)
                throw new ArgumentNullException(nameof(menu));

            var nativeMenu = new NativeMenu();
            AddMenuItems(menu.Items, nativeMenu.Items);
            return nativeMenu;
        }

        static void AddMenuItems(IEnumerable items, IList<NativeMenuItemBase> nativeItems)
        {
            foreach (var item in items)
            {
                var native = ConvertItem(item);
                if (native != null)
                    nativeItems.Add(native);
            }
        }

        static NativeMenuItemBase? ConvertItem(object item)
        {
            switch (item)
            {
                case Separator _:
                    return new NativeMenuItemSeparator();
                case MenuItem menuItem:
                {
                    var header = menuItem.Header?.ToString() ?? string.Empty;
                    var nativeItem = new NativeMenuItem(header)
                    {
                        Command = menuItem.Command,
                        CommandParameter = menuItem.CommandParameter,
                    };

                    if (menuItem.Items.Count > 0)
                    {
                        var submenu = new NativeMenu();
                        AddMenuItems(menuItem.Items, submenu.Items);
                        nativeItem.Menu = submenu;
                    }

                    return nativeItem;
                }
            }

            return null;
        }
    }
}
