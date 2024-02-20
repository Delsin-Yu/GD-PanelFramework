﻿using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using GDPanelSystem.Core.Panels.Tweener;
using GDPanelSystem.Utils.Pooling;
using Godot;

namespace GDPanelSystem.Core.Panels;

/// <summary>
/// The fundamental type for all panels, do not inherit this type.
/// </summary>
public abstract partial class _UIPanelBaseCore : Control
{
    internal enum PanelStatus
    {
        Uninitialized,
        Initialized,
        Opened,
        Closed
    }

    private Control? _cachedSelection;
    private bool _isShownInternal;
    private readonly Dictionary<Control, CachedControlInfo> _cachedChildrenControlInfos = new();
    private IPanelTweener? _panelTweener;
    internal CancellationTokenSource _panelCloseTokenSource = new();
    internal CancellationTokenSource _panelOpenTweenFinishTokenSource = new();
    internal CancellationTokenSource _panelCloseTweenFinishTokenSource = new();

    private readonly List<string> _registeredInputEventNames = [];
    private readonly Dictionary<string, RegisteredInputEvent> _registeredInputEvent = new();
    private readonly Dictionary<Action, Action<InputEvent>> _mappedCancelEvent = new();

    internal PackedScene? SourcePrefab { get; private set; }

    internal record struct CachedControlInfo(FocusModeEnum FocusMode, MouseFilterEnum MouseFilter);

    internal PanelStatus CurrentPanelStatus { get; set; } = PanelStatus.Uninitialized;

    internal virtual void InitializePanelInternal(PackedScene sourcePrefab)
    {
        SourcePrefab = sourcePrefab;
        SetPanelActiveState(false, LayerVisual.Hidden);
    }
    
    /// <summary>
    /// A <see cref="CancellationToken"/> that gets canceled when the <see cref="UIPanel.ClosePanel"/> / <see cref="UIPanelArg{TOpenArg,TCloseArg}.ClosePanel"/> calls.
    /// </summary>
    public CancellationToken? PanelCloseToken => _panelCloseTokenSource?.Token;

    /// <summary>
    /// A <see cref="CancellationToken"/> that gets canceled when the opening animation finishes.
    /// </summary>
    public CancellationToken? PanelOpenTweenFinishToken => _panelOpenTweenFinishTokenSource?.Token;

    /// <summary>
    /// A <see cref="CancellationToken"/> that gets canceled when the opening animation finishes.
    /// </summary>
    public CancellationToken? PanelCloseTweenFinishToken => _panelCloseTweenFinishTokenSource?.Token;

    /// <summary>
    /// The <see cref="IPanelTweener"/> assigned to this panel, assigning null will cause this panel fallbacks to the <see cref="PanelManager.DefaultPanelTweener"/>.
    /// </summary>
    protected IPanelTweener PanelTweener
    {
        get => _panelTweener ?? PanelManager.DefaultPanelTweener;
        set => _panelTweener = value;
    }

    internal SelectionCachingResult CacheCurrentSelection(ref Control? currentSelection)
    {
        _cachedSelection = null;
        currentSelection ??= GetViewport().GuiGetFocusOwner();
        if (currentSelection == null) return SelectionCachingResult.NoSelections;
        if (!IsAncestorOf(currentSelection)) return SelectionCachingResult.NotAChild;
        _cachedSelection = currentSelection;
        return SelectionCachingResult.Successful;
    }

    internal void TryRestoreSelection(ref bool success)
    {
        if (success) return;

        if (_cachedSelection is null) return;

        success = true;
        _cachedSelection.CallDeferred(Control.MethodName.GrabFocus);
        _cachedSelection = null;
    }

    internal void SetPanelActiveState(bool active, LayerVisual layerVisual)
    {
        if (!active)
        {
            Control? control = null;
            CacheCurrentSelection(ref control);

            if (layerVisual == LayerVisual.Hidden)
            {
                _isShownInternal = false;
                HidePanel();
            }

            SetPanelChildAvailability(false);
        }
        else
        {
            SetPanelChildAvailability(true);

            if (_isShownInternal) return;

            _isShownInternal = true;
            ShowPanel();
        }
    }

    internal void SetPanelChildAvailability(bool enabled) => NodeUtils.SetNodeChildAvailability(this, _cachedChildrenControlInfos, enabled);

    internal bool ProcessPanelInput(ref readonly PanelManager.CachedInputEvent inputEvent)
    {
        var name = Name;
        var executionQueue = Pool.Get<Queue<RegisteredInputEvent>>(() => new());
        try
        {
            foreach (var inputEventName in CollectionsMarshal.AsSpan(_registeredInputEventNames))
            {
                if (!inputEvent.ActionHasEventCached(inputEventName)) continue;
                executionQueue.Enqueue(_registeredInputEvent[inputEventName]);
            }

            if (executionQueue.Count == 0) return false;

            var currentPhase = inputEvent.Phase;

            var called = false;

            while (executionQueue.TryDequeue(out var call))
            {
                var localCalled = call.Call(inputEvent.Event, currentPhase, name);
                if (localCalled) called = true;
            }

            return called;
        }
        finally
        {
            Pool.Collect(executionQueue);
        }
    }

    /// <summary>
    /// Using the <see cref="PanelTweener"/> to hide this panel.
    /// </summary>
    /// <param name="onFinish">Calls when then hiding animation completes.</param>
    protected void HidePanel(Action? onFinish = null)
    {
        PanelTweener.Hide(
            this,
            () =>
            {
                Visible = false;
                onFinish?.Invoke();
            }
        );
    }

    /// <summary>
    /// Using the <see cref="PanelTweener"/> to show this panel.
    /// </summary>
    /// <param name="onFinish">Calls when then showing animation completes.</param>
    protected void ShowPanel(Action? onFinish = null)
    {
        Visible = true;
        PanelTweener.Show(this, onFinish);
    }

    /// <summary>
    /// Register a <paramref name="callback"/> to the associated <paramref name="inputName"/> for this panel when it's active.
    /// </summary>
    /// <param name="inputName">The input name to associate to.</param>
    /// <param name="callback">The callback for receiving input command.</param>
    /// <param name="actionPhase">The action phase this callback focuses on.</param>
    protected void RegisterInput(string inputName, Action<InputEvent> callback, InputActionPhase actionPhase = InputActionPhase.Released)
    {
        ArgumentNullException.ThrowIfNull(inputName);
        ArgumentNullException.ThrowIfNull(callback);
        if (!_registeredInputEvent.TryGetValue(inputName, out var registeredInputEvent))
        {
            registeredInputEvent = Pool.Get<RegisteredInputEvent>(() => new());
            _registeredInputEvent.Add(inputName, registeredInputEvent);
            if (!_registeredInputEventNames.Contains(inputName)) _registeredInputEventNames.Add(inputName);
        }

        registeredInputEvent.RegisterCall(callback, actionPhase);
    }

    /// <summary>
    /// Remove a <paramref name="callback"/> to the associated <paramref name="inputName"/> for this panel.
    /// </summary>
    /// <param name="inputName">The input name to remove from.</param>
    /// <param name="callback">The callback to remove.</param>
    /// <param name="actionPhase">The action phase this callback focused on.</param>
    protected void RemoveInput(string inputName, Action<InputEvent> callback, InputActionPhase actionPhase = InputActionPhase.Released)
    {
        ArgumentNullException.ThrowIfNull(inputName);
        ArgumentNullException.ThrowIfNull(callback);
        if (!_registeredInputEvent.TryGetValue(inputName, out var registeredInputEvent)) return;
        registeredInputEvent.RemoveCall(callback, actionPhase);
        if (!registeredInputEvent.Empty) return;
        _registeredInputEvent.Remove(inputName);
        _registeredInputEventNames.Remove(inputName);
        Pool.Collect(registeredInputEvent);
    }


    /// <summary>
    /// Register a <paramref name="callback"/> to the associated <see cref="PanelManager.UICancelActionName"/> for this panel when it's active.
    /// </summary>
    /// <param name="callback">The callback for receiving input command.</param>
    /// <param name="actionPhase">The action phase this callback focuses on.</param>
    protected void RegisterCancelInput(Action callback, InputActionPhase actionPhase = InputActionPhase.Released)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (!_mappedCancelEvent.TryGetValue(callback, out var mappedCallback))
        {
            mappedCallback = _ => callback();
            _mappedCancelEvent.Add(callback, mappedCallback);
        }
        RegisterInput(PanelManager.UICancelActionName, mappedCallback, actionPhase);
    }

    /// <summary>
    /// Remove a <paramref name="callback"/> to the associated <see cref="PanelManager.UICancelActionName"/> for this panel.
    /// </summary>
    /// <param name="callback">The callback to remove.</param>
    /// <param name="actionPhase">The action phase this callback focused on.</param>
    protected void RemoveCancelInput(Action callback, InputActionPhase actionPhase = InputActionPhase.Released)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (!_mappedCancelEvent.Remove(callback, out var mappedCallback)) return;
        RemoveInput(PanelManager.UICancelActionName, mappedCallback, actionPhase);
    }
}