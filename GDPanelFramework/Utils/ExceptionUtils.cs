﻿using System;
using System.Collections.Generic;
using GDPanelFramework.Panels;
using Godot;

namespace GDPanelFramework;

internal static class ExceptionUtils
{
    private const string PanelClosingOrderNotification = "When closing panels, it is mandatory to ensure the order of closing these panels is symmetrical to how they are opened.";

    internal static void ThrowIfUninitialized(this _UIPanelBaseCore panel)
    {
        if (panel.CurrentPanelStatus != _UIPanelBaseCore.PanelStatus.Uninitialized) return;
        throw new InvalidOperationException("Attempting to open an uninitialized panel, this is not supported, please use CreateOrGetPanel to properly get an initialized panel.");
    }

    internal static void ThrowIfNotOpened(this _UIPanelBaseCore panel)
    {
        if (panel.CurrentPanelStatus == _UIPanelBaseCore.PanelStatus.Opened) return;
        throw new InvalidOperationException("Attempting to close a not opened panel, this is not supported.");
    }

    public static void ThrowIfClosingPanelIsNotTopPanel<TPanel>(TPanel closingPanel, _UIPanelBaseCore topPanel) where TPanel : _UIPanelBaseCore
    {
        if (ReferenceEquals(closingPanel, topPanel)) return;
        throw new InvalidOperationException($"Attempting to close a panel that is not on top of the current panel layer, this is not supported. {PanelClosingOrderNotification}");
    }

    public static void ThrowIfUnauthorizedPanelRootOwner(Node requester, Node? owner)
    {
        if(ReferenceEquals(requester, owner)) return;
        throw new InvalidOperationException($"{requester.Name} is attempting to pop a panel root that's owned by {owner?.Name ?? nameof(PanelManager)}, this is not supported.");
    }
}