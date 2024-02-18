﻿using System.Collections.Generic;
using GDPanelSystem.Core.Panels;
using Godot;

namespace GDPanelSystem.Core;

internal static class NodeUtils
{
    internal static void SetNodeChildAvailability(Node node, Dictionary<Control, UIPanelBaseCore.CachedControlInfo> cachedControlInfo, bool enabled)
    {
        ToggleFocusModeRecursive(node, enabled, cachedControlInfo);
    }

    private static void ToggleFocusModeRecursive(Node root, bool enable, Dictionary<Control, UIPanelBaseCore.CachedControlInfo> cachedNodeFocusMode, bool includeInternal = false)
    {
        if (!enable)
        {
            cachedNodeFocusMode.Clear();
            DisableFocusModeRecursive(root, cachedNodeFocusMode, includeInternal);
        }
        else
        {
            EnableFocusModeRecursive(root, cachedNodeFocusMode, includeInternal);
            cachedNodeFocusMode.Clear();
        }
    }

    private static void DisableFocusModeRecursive(Node root, Dictionary<Control, UIPanelBaseCore.CachedControlInfo> cachedControlInfo, bool includeInternal = false)
    {
        if (root is Control control)
        {
            var controlFocusMode = control.FocusMode;
            var controlMouseFilter = control.MouseFilter;
            // Only cache the control when it is in any form of focusable
            if (controlFocusMode != Control.FocusModeEnum.None || controlMouseFilter != Control.MouseFilterEnum.Ignore)
            {
                cachedControlInfo[control] = new(controlFocusMode, controlMouseFilter);
                control.FocusMode = Control.FocusModeEnum.None;
                control.MouseFilter = Control.MouseFilterEnum.Ignore;
            }
        }

        foreach (var child in root.GetChildren(includeInternal))
        {
            if (child is UIPanelBaseCore) continue;
            DisableFocusModeRecursive(child, cachedControlInfo, includeInternal);
        }
    }

    private static void EnableFocusModeRecursive(Node root, Dictionary<Control, UIPanelBaseCore.CachedControlInfo> cachedControlInfo, bool includeInternal = false)
    {
        if (root is Control control)
        {
            // Only enable if the node is present in the cache
            if (cachedControlInfo.Remove(control, out var cachedFocusMode))
            {
                control.FocusMode = cachedFocusMode.FocusMode;
                control.MouseFilter = cachedFocusMode.MouseFilter;
            }
        }

        foreach (var child in root.GetChildren(includeInternal))
        {
            if (child is UIPanelBaseCore) continue;
            EnableFocusModeRecursive(child, cachedControlInfo, includeInternal);
        }
    }
}