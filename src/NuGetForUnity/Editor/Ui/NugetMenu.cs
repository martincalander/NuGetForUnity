using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using JetBrains.Annotations;
using NugetForUnity.Configuration;
using UnityEditor;
using UnityEngine;

namespace NugetForUnity.Ui
{
    /// <summary>
    ///     Dynamically registers NuGet menu items so users can configure the menu root path.
    /// </summary>
    [InitializeOnLoad]
    internal static class NugetMenu
    {
        private const string RegisteredMenuRootSessionStateKey = "NuGetForUnity.RegisteredMenuRoot";

        private static readonly string[] KnownMenuRoots =
        {
            NugetConfigFile.DefaultMenuRoot,
            "Tools/NuGet",
            "Window/Package Management/NuGet",
        };

        private static readonly List<string> RegisteredMenuItemPaths = new List<string>();

        [CanBeNull]
        private static MethodInfo addMenuItemMethod;

        private static bool loggedMenuApiWarning;

        [CanBeNull]
        private static MethodInfo removeMenuItemMethod;

        static NugetMenu()
        {
            EditorApplication.delayCall += Refresh;
        }

        [MenuItem(NugetConfigFile.DefaultMenuRoot + "/Manage NuGet Packages", false, 0)]
        private static void DisplayNugetWindow()
        {
            NugetWindow.DisplayNugetWindow();
        }

        [MenuItem(NugetConfigFile.DefaultMenuRoot + "/Restore Packages", false, 1)]
        private static void RestorePackages()
        {
            NugetWindow.RestorePackages();
        }

        [MenuItem(NugetConfigFile.DefaultMenuRoot + "/Show Dependency Tree", false, 5)]
        private static void DisplayDependencyTree()
        {
            DependencyTreeViewer.DisplayDependencyTree();
        }

        [MenuItem(NugetConfigFile.DefaultMenuRoot + "/Preferences", false, 9)]
        private static void DisplayPreferences()
        {
            NugetWindow.DisplayPreferences();
        }

        [MenuItem(NugetConfigFile.DefaultMenuRoot + "/Version " + NugetPreferences.NuGetForUnityVersion + " \uD83D\uDD17", false, 10)]
        private static void DisplayVersion()
        {
            NuGetForUnityUpdater.DisplayVersion();
        }

        [MenuItem(NugetConfigFile.DefaultMenuRoot + "/Check for Updates...", false, 11)]
        private static void CheckForUpdates()
        {
            NuGetForUnityUpdater.CheckForUpdates();
        }

        /// <summary>
        ///     Re-registers NuGet menu items at the configured menu root.
        /// </summary>
        public static void Refresh()
        {
            if (!TryInitializeMenuMethods())
            {
                return;
            }

            RemoveRegisteredMenuItems();

            var menuRoot = GetConfiguredMenuRoot();
            foreach (var menuItem in GetMenuItems())
            {
                var menuPath = $"{menuRoot}/{menuItem.Name}";

                try
                {
                    RemoveMenuItem(menuPath);
                    addMenuItemMethod.Invoke(null, new object[] { menuPath, string.Empty, false, menuItem.Priority, menuItem.Execute, menuItem.Validate });
                    RegisteredMenuItemPaths.Add(menuPath);
                }
                catch (Exception exception)
                {
                    Debug.LogError($"Unable to add NuGet menu item '{menuPath}'. Error: {exception}");
                }
            }

            SessionState.SetString(RegisteredMenuRootSessionStateKey, menuRoot);
        }

        [NotNull]
        private static string GetConfiguredMenuRoot()
        {
            try
            {
                if (!File.Exists(ConfigurationManager.NugetConfigFilePath))
                {
                    return NugetConfigFile.DefaultMenuRoot;
                }

                return ConfigurationManager.NugetConfigFile.MenuRoot;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Unable to load NuGet menu root setting. Falling back to '{NugetConfigFile.DefaultMenuRoot}'. Error: {exception}");
                return NugetConfigFile.DefaultMenuRoot;
            }
        }

        [NotNull]
        private static IEnumerable<MenuItemDefinition> GetMenuItems()
        {
            yield return new MenuItemDefinition("Manage NuGet Packages", 0, NugetWindow.DisplayNugetWindow);
            yield return new MenuItemDefinition("Restore Packages", 1, NugetWindow.RestorePackages);
            yield return new MenuItemDefinition("Show Dependency Tree", 5, DependencyTreeViewer.DisplayDependencyTree);
            yield return new MenuItemDefinition("Preferences", 9, NugetWindow.DisplayPreferences);
            yield return new MenuItemDefinition($"Version {NugetPreferences.NuGetForUnityVersion} \uD83D\uDD17", 10, NuGetForUnityUpdater.DisplayVersion);
            yield return new MenuItemDefinition("Check for Updates...", 11, NuGetForUnityUpdater.CheckForUpdates);
        }

        private static void RemoveRegisteredMenuItems()
        {
            foreach (var menuPath in RegisteredMenuItemPaths)
            {
                RemoveMenuItem(menuPath);
            }

            RegisteredMenuItemPaths.Clear();

            var menuRootsToClear = new HashSet<string>(StringComparer.Ordinal);
            foreach (var menuRoot in KnownMenuRoots)
            {
                menuRootsToClear.Add(menuRoot);
            }

            var sessionMenuRoot = SessionState.GetString(RegisteredMenuRootSessionStateKey, string.Empty);
            if (!string.IsNullOrEmpty(sessionMenuRoot))
            {
                menuRootsToClear.Add(sessionMenuRoot);
            }

            foreach (var menuRoot in menuRootsToClear)
            {
                RemoveMenuItems(menuRoot);
            }
        }

        private static void RemoveMenuItems([NotNull] string menuRoot)
        {
            foreach (var menuItem in GetMenuItems())
            {
                RemoveMenuItem($"{menuRoot}/{menuItem.Name}");
            }
        }

        private static bool TryInitializeMenuMethods()
        {
            if (addMenuItemMethod != null && removeMenuItemMethod != null)
            {
                return true;
            }

            var menuType = typeof(EditorApplication).Assembly.GetType("UnityEditor.Menu");
            if (menuType == null)
            {
                LogMenuApiWarning("Unable to find type 'UnityEditor.Menu'. NuGet menu root configuration will not be applied.");
                return false;
            }

            addMenuItemMethod = menuType.GetMethod(
                "AddMenuItem",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(string), typeof(string), typeof(bool), typeof(int), typeof(Action), typeof(Func<bool>) },
                null);
            removeMenuItemMethod = menuType.GetMethod(
                "RemoveMenuItem",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(string) },
                null);

            if (addMenuItemMethod == null || removeMenuItemMethod == null)
            {
                LogMenuApiWarning(
                    "Unable to resolve UnityEditor.Menu.AddMenuItem/RemoveMenuItem methods. NuGet menu root configuration will not be applied.");
                return false;
            }

            return true;
        }

        private static void LogMenuApiWarning([NotNull] string message)
        {
            if (loggedMenuApiWarning)
            {
                return;
            }

            loggedMenuApiWarning = true;
            Debug.LogWarning(message);
        }

        private static void RemoveMenuItem([NotNull] string menuPath)
        {
            if (removeMenuItemMethod == null)
            {
                return;
            }

            try
            {
                removeMenuItemMethod.Invoke(null, new object[] { menuPath });
            }
            catch (Exception)
            {
                // Menu cleanup is best-effort; missing menu items are harmless.
            }
        }

        private sealed class MenuItemDefinition
        {
            public MenuItemDefinition([NotNull] string name, int priority, [NotNull] Action execute, [CanBeNull] Func<bool> validate = null)
            {
                Name = name;
                Priority = priority;
                Execute = execute;
                Validate = validate;
            }

            [NotNull]
            public string Name { get; }

            public int Priority { get; }

            [NotNull]
            public Action Execute { get; }

            [CanBeNull]
            public Func<bool> Validate { get; }
        }
    }
}
