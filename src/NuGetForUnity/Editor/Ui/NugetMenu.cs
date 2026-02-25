using System;
using System.Collections.Generic;
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

            var menuRoot = ConfigurationManager.NugetConfigFile.MenuRoot;
            foreach (var menuItem in GetMenuItems())
            {
                var menuPath = $"{menuRoot}/{menuItem.Name}";

                try
                {
                    RemoveMenuItem(menuPath);
                    addMenuItemMethod.Invoke(null, new object[] { menuPath, string.Empty, false, menuItem.Priority, menuItem.Execute, null });
                    RegisteredMenuItemPaths.Add(menuPath);
                }
                catch (Exception exception)
                {
                    Debug.LogError($"Unable to add NuGet menu item '{menuPath}'. Error: {exception}");
                }
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
            if (removeMenuItemMethod == null)
            {
                RegisteredMenuItemPaths.Clear();
                return;
            }

            foreach (var menuPath in RegisteredMenuItemPaths)
            {
                RemoveMenuItem(menuPath);
            }

            RegisteredMenuItemPaths.Clear();
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
                // Ignore, this is a best-effort cleanup of existing menu entries.
            }
        }

        private sealed class MenuItemDefinition
        {
            public MenuItemDefinition([NotNull] string name, int priority, [NotNull] Action execute)
            {
                Name = name;
                Priority = priority;
                Execute = execute;
            }

            [NotNull]
            public string Name { get; }

            public int Priority { get; }

            [NotNull]
            public Action Execute { get; }
        }
    }
}
