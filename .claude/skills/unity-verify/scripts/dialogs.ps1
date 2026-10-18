# Lists the visible windows of this project's Unity Editor (modal dialogs included) and optionally presses a button.
# Usage: dialogs.ps1 [-Click "Reload"]
# The Editor process id comes from Library/Pipeline/.unity-pipeline-port, so other open Editors are never touched.
param([string]$Click)

$root = (git rev-parse --show-toplevel)
$descriptor = Get-Content (Join-Path $root 'Library/Pipeline/.unity-pipeline-port') -Raw | ConvertFrom-Json
$processId = [int]$descriptor.pid

Add-Type @'
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class EditorWindows {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr p, EnumProc f, IntPtr l);
  [DllImport("user32.dll")] static extern int GetWindowThreadProcessId(IntPtr h, out int pid);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  const uint BM_CLICK = 0x00F5;
  static string Text(IntPtr h) { var t = new StringBuilder(4096); GetWindowText(h, t, 4096); return t.ToString(); }
  static string Class(IntPtr h) { var c = new StringBuilder(256); GetClassName(h, c, 256); return c.ToString(); }
  public static List<string> List(int pid, string click) {
    var lines = new List<string>();
    EnumWindows((h, l) => {
      int owner; GetWindowThreadProcessId(h, out owner);
      if (owner == pid && IsWindowVisible(h)) {
        // #32770 is the class of Win32 dialogs: that is where Unity's modal questions live.
        var isDialog = Class(h) == "#32770";
        lines.Add((isDialog ? "DIALOG " : "WINDOW ") + "'" + Text(h) + "'");
        if (isDialog) {
          EnumChildWindows(h, (child, l2) => {
            var text = Text(child);
            if (text.Length > 0) { lines.Add("   [" + Class(child) + "] '" + text + "'"); }
            if (click != null && Class(child) == "Button" && text == click) {
              SendMessage(child, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
              lines.Add("   -> pressed " + text);
            }
            return true; }, IntPtr.Zero);
        }
      }
      return true; }, IntPtr.Zero);
    return lines;
  }
}
'@

"Editor pid $processId ($($descriptor.projectPath), port $($descriptor.port))"
[EditorWindows]::List($processId, $(if ($Click) { $Click } else { $null }))
