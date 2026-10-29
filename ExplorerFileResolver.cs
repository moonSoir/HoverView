// Based on ExplorerFileResolver.cs from HoverPeek
// Copyright (c) 2026 kyosora, licensed under MIT.
// Modifications Copyright (c) 2026 zes / moonSoir.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Automation;

#nullable enable

namespace HoverView.Core.FileResolver;

public sealed class ExplorerFileResolver
{
    private IntPtr _cachedTabHandle;
    private string? _cachedFolderPath;
    private DateTime _cachedAt;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(3);

    // Кэш проверки папок на наличие поддерживаемых файлов
    private static readonly Dictionary<string, (bool HasSupported, DateTime Timestamp)> _folderCache = new();
    private static readonly object _folderCacheLock = new();
    private static readonly TimeSpan FolderCacheTtl = TimeSpan.FromSeconds(10);

    // --- Win32 API ---

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    private const uint GA_ROOT = 2;
    private const uint GW_CHILD = 5;
    private const uint GW_HWNDNEXT = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    // --- COM interop для IShellBrowser ---

    [ComImport]
    [Guid("6d5140c1-7436-11ce-8034-00aa006009fa")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ComServiceProvider
    {
        [return: MarshalAs(UnmanagedType.IUnknown)]
        object QueryService(ref Guid guidService, ref Guid riid);
    }

    [ComImport]
    [Guid("000214e2-0000-0000-c000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        IntPtr GetWindow();
    }

    // --- Логирование ---

    private static void Log(string msg)
    {
        Debug.WriteLine($"[ExplorerFileResolver] {msg}");
        Console.WriteLine($"[ExplorerFileResolver] {msg}");
    }

    // --- Основная логика ---

    public string? ResolveFileAtPoint(int screenX, int screenY)
    {
        Log($"=== ResolveFileAtPoint({screenX}, {screenY}) ===");
        try
        {
            var hwndUnderCursor = WindowFromPoint(new POINT { X = screenX, Y = screenY });
            Log($"WindowFromPoint -> HWND=0x{hwndUnderCursor.ToInt64():X}");
            if (hwndUnderCursor == IntPtr.Zero) { Log("FAIL: hwndUnderCursor == 0"); return null; }

            var rootHwnd = GetAncestor(hwndUnderCursor, GA_ROOT);
            Log($"GetAncestor(GA_ROOT) -> HWND=0x{rootHwnd.ToInt64():X}");

            var className = GetWindowClass(rootHwnd);
            Log($"Root class = '{className}'");

            if (className != "CabinetWClass" && className != "ExploreWClass")
            {
                Log("FAIL: не Проводник");
                return null;
            }

            var activeTabHwnd = GetActiveTabHwnd(rootHwnd);
            Log($"ActiveTab HWND=0x{activeTabHwnd.ToInt64():X} (root=0x{rootHwnd.ToInt64():X})");

            var point = new System.Windows.Point(screenX, screenY);
            var element = AutomationElement.FromPoint(point);
            if (element == null) { Log("FAIL: AutomationElement.FromPoint == null"); return null; }
            Log($"Element under point: Name='{element.Current.Name}', " +
                $"ControlType={element.Current.ControlType.ProgrammaticName}, " +
                $"ClassName='{element.Current.ClassName}', " +
                $"NativeWindowHandle=0x{element.Current.NativeWindowHandle:X}");

            var fileElement = FindFileListItem(element, activeTabHwnd);
            if (fileElement == null) { Log("FAIL: FindFileListItem == null"); return null; }

            var fileName = fileElement.Current.Name;
            Log($"FileName = '{fileName}'");
            if (string.IsNullOrWhiteSpace(fileName)) { Log("FAIL: fileName пустой"); return null; }

            var folderPath = GetCurrentExplorerFolderCached(rootHwnd, activeTabHwnd);
            Log($"FolderPath = '{folderPath}'");
            if (string.IsNullOrEmpty(folderPath)) { Log("FAIL: folderPath пустой"); return null; }

            var fullPath = Path.Combine(folderPath, fileName);
            bool exists = File.Exists(fullPath) || Directory.Exists(fullPath);
            Log($"FullPath = '{fullPath}', exists={exists}");
            return exists ? fullPath : null;
        }
        catch (ElementNotAvailableException ex) { Log($"ElementNotAvailableException: {ex.Message}"); return null; }
        catch (Exception ex) { Log($"Exception: {ex.GetType().Name}: {ex.Message}"); return null; }
    }

    /// <summary>
    /// Проверяет (с кэшем), есть ли в папке файлы с поддерживаемыми расширениями.
    /// Возвращает true/false, если результат известен, или null, если проверка ещё идёт.
    /// </summary>
    public bool? FolderHasSupportedFiles(string folderPath, List<string> supportedExtensions)
    {
        if (string.IsNullOrEmpty(folderPath)) return null;

        lock (_folderCacheLock)
        {
            if (_folderCache.TryGetValue(folderPath, out var cached) &&
                DateTime.UtcNow - cached.Timestamp < FolderCacheTtl)
            {
                return cached.HasSupported;
            }
        }

        // Запускаем проверку в фоне
        _ = Task.Run(() =>
        {
            bool hasSupported = false;
            try
            {
                var exts = new HashSet<string>(supportedExtensions, StringComparer.OrdinalIgnoreCase);
                foreach (var file in Directory.EnumerateFiles(folderPath))
                {
                    var ext = Path.GetExtension(file);
                    if (exts.Contains(ext))
                    {
                        hasSupported = true;
                        break;
                    }
                }
            }
            catch { }

            lock (_folderCacheLock)
            {
                _folderCache[folderPath] = (hasSupported, DateTime.UtcNow);
            }
        });

        return null;
    }

    // --- Вспомогательные методы ---

    private static string GetWindowClass(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static IntPtr GetActiveTabHwnd(IntPtr explorerHwnd)
    {
        Log($"  GetActiveTabHwnd: перебор дочерних окон 0x{explorerHwnd.ToInt64():X} в Z-порядке");
        var child = GetWindow(explorerHwnd, GW_CHILD);
        int idx = 0;
        while (child != IntPtr.Zero)
        {
            var cls = GetWindowClass(child);
            Log($"    child[{idx}] HWND=0x{child.ToInt64():X} class='{cls}'");
            if (cls == "ShellTabWindowClass")
            {
                Log($"    -> активная вкладка = 0x{child.ToInt64():X}");
                return child;
            }
            child = GetWindow(child, GW_HWNDNEXT);
            idx++;
        }
        Log("    -> ShellTabWindowClass не найден, возвращаем исходный HWND (Win10?)");
        return explorerHwnd;
    }

    private static AutomationElement? FindFileListItem(AutomationElement element, IntPtr activeTabHwnd)
    {
        Log("  FindFileListItem: поднимаемся по дереву UIA");
        var walker = TreeWalker.ControlViewWalker;
        var current = element;

        for (int i = 0; i < 8 && current != null; i++)
        {
            var controlType = current.Current.ControlType;
            var name = current.Current.Name;
            var cls = current.Current.ClassName;
            int nativeHwndRaw = current.Current.NativeWindowHandle;
            Log($"    [{i}] type={controlType.ProgrammaticName}, name='{name}', class='{cls}', nativeHWND=0x{nativeHwndRaw:X}");

            if (controlType == ControlType.ListItem || controlType == ControlType.DataItem)
            {
                Log($"    [{i}] -> это ListItem/DataItem, проверяем принадлежность вкладке");
                if (nativeHwndRaw != 0)
                {
                    var elementHwnd = new IntPtr(nativeHwndRaw);
                    var elementRoot = GetAncestor(elementHwnd, GA_ROOT);
                    Log($"      elementRoot=0x{elementRoot.ToInt64():X}, activeTab=0x{activeTabHwnd.ToInt64():X}, rootEqual={elementRoot == activeTabHwnd}");
                    if (elementRoot != activeTabHwnd)
                    {
                        Log("      -> не наша вкладка, идём выше");
                        current = walker.GetParent(current);
                        continue;
                    }
                }
                else
                {
                    Log("      nativeHWND == 0, пропускаем проверку HWND");
                }

                var parent = walker.GetParent(current);
                int depth = 0;
                while (parent != null && depth < 5)
                {
                    var cn = parent.Current.ClassName;
                    Log($"      parent[{depth}] class='{cn}'");
                    if (cn == "UIItemsView" || cn == "DirectUIHWND" ||
                        cn == "ShellView" || cn == "SHELLDLL_DefView")
                    {
                        Log($"      -> найден родитель '{cn}', возвращаем ListItem");
                        return current;
                    }
                    parent = walker.GetParent(parent);
                    depth++;
                }
                Log("      -> ни один родитель не подошёл, идём выше");
            }
            current = walker.GetParent(current);
        }
        Log("  FindFileListItem: не нашли");
        return null;
    }

    private string? GetCurrentExplorerFolderCached(IntPtr explorerHwnd, IntPtr activeTabHwnd)
    {
        if (activeTabHwnd == _cachedTabHandle &&
            _cachedFolderPath != null &&
            DateTime.UtcNow - _cachedAt < CacheTtl)
        {
            Log($"  кэш попадание (tab=0x{activeTabHwnd.ToInt64():X}): '{_cachedFolderPath}'");
            return _cachedFolderPath;
        }

        Log($"  кэш промах (tab=0x{activeTabHwnd.ToInt64():X}, prev=0x{_cachedTabHandle.ToInt64():X})");
        string? folderPath = null;

        if (activeTabHwnd != IntPtr.Zero)
        {
            Log($"  пробуем COM через IShellBrowser (targetTab=0x{activeTabHwnd.ToInt64():X})");
            folderPath = GetFolderPathViaComByTabHwnd(activeTabHwnd);
            Log($"  COM via IShellBrowser -> '{folderPath}'");
        }

        if (folderPath == null && activeTabHwnd != explorerHwnd && activeTabHwnd != IntPtr.Zero)
        {
            Log($"  fallback: UIA от активной вкладки 0x{activeTabHwnd.ToInt64():X}");
            folderPath = GetFolderPathViaUIAFromTab(activeTabHwnd);
            Log($"  UIA from tab -> '{folderPath}'");
        }

        if (folderPath != null)
        {
            _cachedTabHandle = activeTabHwnd;
            _cachedFolderPath = folderPath;
            _cachedAt = DateTime.UtcNow;
        }
        return folderPath;
    }

    private static string? GetFolderPathViaComByTabHwnd(IntPtr targetTabHwnd)
    {
        var sidTopLevelBrowser = new Guid("4c96be40-915c-11cf-99d3-00aa004ae837");
        var iidShellBrowser = new Guid("000214e2-0000-0000-c000-000000000046");

        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null) { Log("    COM: Shell.Application не найден"); return null; }
            dynamic? shell = Activator.CreateInstance(shellType);
            if (shell == null) { Log("    COM: Activator вернул null"); return null; }

            var windows = shell.Windows();
            Log($"    COM: всего окон {windows.Count}");

            for (int i = 0; i < windows.Count; i++)
            {
                dynamic? window = windows.Item(i);
                if (window == null) continue;

                try
                {
                    IShellBrowser? shellBrowser = null;
                    try
                    {
                        var sp = (ComServiceProvider)window;
                        object sbObj = sp.QueryService(ref sidTopLevelBrowser, ref iidShellBrowser);
                        shellBrowser = sbObj as IShellBrowser;
                    }
                    catch (Exception ex)
                    {
                        Log($"    COM[{i}] QueryService failed: {ex.Message}");
                        continue;
                    }

                    if (shellBrowser == null)
                    {
                        Log($"    COM[{i}] shellBrowser == null");
                        continue;
                    }

                    IntPtr tabHwnd = shellBrowser.GetWindow();
                    Log($"    COM[{i}] tabHwnd=0x{tabHwnd.ToInt64():X}, target=0x{targetTabHwnd.ToInt64():X}");

                    if (tabHwnd == targetTabHwnd)
                    {
                        string url = window.LocationURL;
                        Log($"    COM[{i}] MATCH! LocationURL={url}");
                        if (url.StartsWith("file:///"))
                            return new Uri(url).LocalPath;
                        return window.Document?.Folder?.Self?.Path;
                    }
                }
                catch (Exception ex)
                {
                    Log($"    COM[{i}] err: {ex.Message}");
                    continue;
                }
            }

            Log("    COM: ни одно окно не совпало по HWND вкладки");
            return null;
        }
        catch (Exception ex)
        {
            Log($"    COM outer err: {ex.Message}");
            return null;
        }
    }

    private static string? GetFolderPathViaUIAFromTab(IntPtr tabHwnd)
    {
        try
        {
            var tabElement = AutomationElement.FromHandle(tabHwnd);
            if (tabElement == null) { Log("    UIA: FromHandle == null"); return null; }
            Log($"    UIA: tab element Name='{tabElement.Current.Name}', Class='{tabElement.Current.ClassName}'");

            var addressBar = tabElement.FindFirst(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, "1001"));

            if (addressBar == null) { Log("    UIA: адресная строка (AutomationId=1001) не найдена"); return null; }

                Log($"    UIA: addressBar Name='{addressBar.Current.Name}', Class='{addressBar.Current.ClassName}', " +
            $"Bounds={addressBar.Current.BoundingRectangle}");
            var valuePattern = addressBar.GetCurrentPattern(ValuePattern.Pattern) as ValuePattern;
            var path = valuePattern?.Current.Value;
            Log($"    UIA: ValuePattern.Value = '{path}'");
            return string.IsNullOrEmpty(path) ? null : path;
        }
        catch (Exception ex) { Log($"    UIA err: {ex.Message}"); return null; }
    }
}
