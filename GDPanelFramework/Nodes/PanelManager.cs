﻿using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using GDPanelFramework.Panels;
using GDPanelFramework.Panels.Tweener;
using GDPanelFramework.Utils.Pooling;
using Godot;
using GodotPanelFramework;

namespace GDPanelFramework;

/// <summary>
/// <see cref="PanelManager"/> is the core module of GDPanelFramework, it manages panel opening and closing as well as communicates between panel layers, and activates/deactivates them at appropriate times.<br/>
/// This module provides you access to public APIs that responsible for:<br/>
/// 1. Creating panels from PackedScene (<see cref="CreatePanel{TPanel}"/>) and initiating panel opening behavior.<br/>
/// 2. Exposes the system-wide <see cref="DefaultPanelTweener"/>.<br/>
/// 3. Configuring parent for the opening panels through <see cref="PushPanelParent"/> and <see cref="PopPanelParent"/>.<br/>
/// 4. Dispatches the <see cref="InputEvent"/>s to the active panels and lets you configures the system-wide <see cref="UICancelActionName"/>.
/// </summary>
public static partial class PanelManager
{
#pragma warning disable CA2255
    [ModuleInitializer]
    internal static void Initializer()
    {
        if (Engine.IsEditorHint()) return;
        GetCurrentPanelRoot();
    }
#pragma warning restore CA2255

    private record struct PanelRootInfo(Node? Owner, Control Root);

    private static readonly Dictionary<PackedScene, Stack<UIPanelBaseCore>> BufferedPanels = new();
    private static readonly Stack<UIPanelBaseCore> PanelStack = new();
    private static readonly Stack<PanelRootInfo> PanelParents = new();

    private static bool _panelRootInitialized;
    private static IPanelTweener _defaultPanelTweener = NonePanelTweener.Instance;

    private static Control GetCurrentPanelRoot()
    {
        if (_panelRootInitialized) return PanelParents.Peek().Root;

        PanelParents.Push(new(null, RootPanelContainer.PanelRoot));
        _panelRootInitialized = true;

        return PanelParents.Peek().Root;
    }


    private static void PushPanelToPanelStack<TPanel>(TPanel panelInstance, PreviousPanelVisual previousPreviousPanelVisual) where TPanel : UIPanelBaseCore
    {
        // Ensure the current panel is at the front most.
        var parent = GetCurrentPanelRoot();
        var oldParent = panelInstance.GetParent();
        if (oldParent == parent) parent.MoveToFront();
        else panelInstance.Reparent(parent);

        // Pushes a panel to new layer, disables gui handling for the previous panel. 
        if (PanelStack.TryPeek(out var topmostPanel))
        {
            topmostPanel.SetPanelActiveState(false, previousPreviousPanelVisual);
        }

        PanelStack.Push(panelInstance);
    }

    internal static void HandlePanelClose<TPanel>(TPanel closingPanel, PreviousPanelVisual previousPreviousPanelVisual, ClosePolicy closePolicy) where TPanel : UIPanelBaseCore
    {

        var topPanel = PanelStack.Peek();

        ExceptionUtils.ThrowIfClosingPanelIsNotTopPanel(closingPanel, topPanel);

        PanelStack.Pop();

        if (PanelStack.TryPeek(out topPanel))
        {
            topPanel.SetPanelActiveState(true, previousPreviousPanelVisual);
            topPanel.TryRestoreSelection();
        }

        if (closePolicy == ClosePolicy.Delete)
        {
            closingPanel.PanelCloseTweenFinishToken!.Value.Register(closingPanel.QueueFree);
            return;
        }

        var sourcePrefab = closingPanel.SourcePrefab!;

        if (!BufferedPanels.TryGetValue(sourcePrefab, out var cacheStack))
        {
            cacheStack = Pool.Get<Stack<UIPanelBaseCore>>(() => new());
            BufferedPanels.Add(sourcePrefab, cacheStack);
        }

        cacheStack.Push(closingPanel);
    }

    internal static bool ProcessInputEvent(InputEvent inputEvent)
    {
        if (!PanelStack.TryPeek(out var topPanel)) return false;

        var cachedWrapper = new CachedInputEvent(inputEvent);

        return topPanel.ProcessPanelInput(ref cachedWrapper);
    }

    internal readonly struct CachedInputEvent
    {
        private readonly Dictionary<StringName, bool> _actionHasEvent;

        public CachedInputEvent(InputEvent @event)
        {
            Event = @event;
            _actionHasEvent = Pool.Get<Dictionary<StringName, bool>>(() => []);
            Phase = Event.IsPressed() ? InputActionPhase.Pressed : InputActionPhase.Released;
        }

        public InputActionPhase Phase { get; }
        public InputEvent Event { get; }

        public bool ActionHasEventCached(StringName action)
        {
            if (_actionHasEvent.TryGetValue(action, out var result)) return result;
            result = InputMap.ActionHasEvent(action, Event);
            _actionHasEvent.Add(action, result);
            return result;
        }

        public void Dispose()
        {
            _actionHasEvent.Clear();
            Pool.Collect(_actionHasEvent);
        }
    }

    /// <summary>
    /// Access the default system-wide <see cref="IPanelTweener"/>.
    /// </summary>
    public static IPanelTweener DefaultPanelTweener
    {
        get => _defaultPanelTweener;
        set => _defaultPanelTweener = value ?? NonePanelTweener.Instance;
    }

    /// <summary>
    /// Access the system-wide <see cref="InputEvent"/> name that is considered the UI Cancel Action.
    /// </summary>
    public static string UICancelActionName { get; set; } = GodotBuiltinActionNames.UICancel;

    /// <summary>
    /// Pushes a new <see cref="Control"/> as the parent for subsequent opening panels to the parent stack.
    /// </summary>
    /// <remarks>
    /// Pushing and popping actions must be parallel, that is, the topmost parent must be popped before you can pop the other parents.
    /// </remarks>
    /// <param name="owner">The owner that perform this action.</param>
    /// <param name="newRoot">The <see cref="Control"/> that is becoming the parent for subsequent opening panels.</param>
    public static void PushPanelParent(Node owner, Control newRoot)
    {
        PanelParents.Push(new(owner, newRoot));
    }

    /// <summary>
    /// Pops the topmost parent from the parent stack, which makes the next topmost <see cref="Control"/> become the parent for subsequent opening panels.
    /// </summary>
    /// <remarks>
    /// Pushing and popping actions must be parallel, that is, the topmost parent must be popped before you can pop the other parents.
    /// </remarks>
    /// <param name="requester">The owner that performs this action.</param>
    /// <exception cref="ArgumentNullException">The requester is null.</exception>
    /// <exception cref="InvalidOperationException">The requester is not the owner of the topmost parent of the parent stack.</exception>
    public static void PopPanelParent(Node requester)
    {
        ArgumentNullException.ThrowIfNull(requester);
        ExceptionUtils.ThrowIfUnauthorizedPanelRootOwner(requester, PanelParents.Peek().Owner);
        PanelParents.Pop();
    }

    /// <summary>
    /// Try create an instance of the <typeparamref name="TPanel"/> from the supplied <paramref name="packedPanel"/>.
    /// </summary>
    /// <param name="packedPanel">The <see cref="PackedScene"/> to create the panel from.</param>
    /// <param name="createPolicy">When set to <see cref="CreatePolicy.TryReuse"/>, the system will try reuse an existing cached instance if possible.</param>
    /// <param name="initializeCallback">A delegate that gets called on the instance for pre-initialization.</param>
    /// <typeparam name="TPanel">The panel type to create from the <see cref="PackedScene"/></typeparam>
    /// <returns>The instance of the specified panel.</returns>
    /// <exception cref="InvalidOperationException">Throws when the system is unable to cast the instance of the <paramref name="packedPanel"/> to desired <typeparamref name="TPanel"/> type.</exception>
    public static TPanel CreatePanel<TPanel>(this PackedScene packedPanel, CreatePolicy createPolicy = CreatePolicy.TryReuse,
        Action<TPanel>? initializeCallback = null) where TPanel : UIPanelBaseCore
    {
        TPanel panelInstance;

        if (createPolicy == CreatePolicy.TryReuse && BufferedPanels.TryGetValue(packedPanel, out var cacheStack))
        {
            panelInstance = (TPanel)cacheStack.Pop()!;
            initializeCallback?.Invoke(panelInstance);
            if (cacheStack.Count == 0)
            {
                Pool.Collect(cacheStack);
                BufferedPanels.Remove(packedPanel);
            }

            return panelInstance;
        }

        panelInstance = packedPanel.InstantiateOrNull<TPanel>();
        if (panelInstance is null)
        {
            throw new InvalidOperationException($"Unable to cast {packedPanel.ResourceName} to {typeof(TPanel)}!");
        }

        GetCurrentPanelRoot().AddChild(panelInstance);
        initializeCallback?.Invoke(panelInstance);
        panelInstance.InitializePanelInternal(packedPanel);
        return panelInstance;
    }
}