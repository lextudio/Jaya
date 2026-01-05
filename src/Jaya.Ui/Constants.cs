//
// Copyright (c) Rubal Walia. All rights reserved.
// Licensed under the 3-Clause BSD license. See LICENSE file in the project root for full license information.
//
using System;
using System.IO;
using System.Reflection;

namespace Jaya.Ui
{
    static class Constants
    {
        const string IMAGES_PATH_FORMAT = "avares://Jaya.Ui/Assets/Images/{0}";
        const string REPO_URL = "https://github.com/lextudio/jaya/";

        public const string APP_SHORT_NAME = "JayaFM";
        public const string APP_NAME = "Jaya File Manager";
        public const string APP_DESCRIPTION = "Jaya File Manager is a small .NET Core based cross platform file explorer application which runs on Windows, Mac and Linux. Its goal is very simple, \"Allow browsing and managing of several file systems simultaneously using a single application which should work and look similar on all desktop platforms it supports.\".";

        public static readonly Uri
            URL_DONATION,
            URL_LICENSE,
            URL_ISSUES;

        public static readonly Version VERSION;
        public static readonly string DATA_DIRECTORY;

        static Constants()
        {
            URL_DONATION = new Uri("https://www.paypal.com/cgi-bin/webscr?cmd=_s-xclick&hosted_button_id=DEXCFJ6R48SR2");
            URL_LICENSE = new Uri("https://raw.githubusercontent.com/lextudio/jaya/dev/LICENSE");
            URL_ISSUES = GetRepositoryUrl("issues");
            VERSION = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0,0,0,0);
            DATA_DIRECTORY = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), APP_SHORT_NAME ?? "JayaFM");
        }

        static Uri GetRepositoryUrl(string urlFragment)
        {
            return new Uri(string.Format("{0}{1}", REPO_URL, urlFragment));
        }

        public static string GetImageUrl(this string fileName)
        {
            return string.Format(IMAGES_PATH_FORMAT, fileName);
        }
    }

    internal enum AccountAction : byte
    {
        Added,
        Removed
    }

    public enum ItemType : byte
    {
        Service,
        Account,
        Computer,
        Drive,
        Directory,
        File,
        Dummy
    }

    public enum CommandType : byte
    {
        None = 0,
        Exit = 1,
        Open = 2,
        Cut = 3,
        Copy = 4,
        Paste = 5,
        NewFolder = 6,
        ToggleToolbars = 7,
        ToggleToolbarFile = 8,
        ToggleToolbarEdit = 9,
        ToggleToolbarView = 10,
        ToggleToolbarHelp = 11,
        TogglePaneNavigation = 12,
        TogglePanePreview = 13,
        TogglePaneDetails = 14,
        ToggleItemCheckBoxes = 15,
        ToggleFileNameExtensions = 16,
        ToggleHiddenItems = 17,
        Delete = 18
        , SelectAll = 19
        , SelectNone = 20
        , InvertSelection = 21
        , CopyPath = 22
    }
}
