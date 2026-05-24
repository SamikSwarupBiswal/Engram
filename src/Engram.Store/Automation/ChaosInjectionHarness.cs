using System;
using System.Collections.Generic;

namespace Engram.Store.Automation;

public enum ChaosEvent
{
    // Environmental
    NotificationPopup,
    SleepWake,
    BrowserCrash,
    DpiChange,
    MonitorDisconnect,
    NetworkDrop,
    AntivirusPopup,
    ModalStorm,
    FocusTheft,
    ClipboardOverwrite,

    // Behavioral
    UserImpatience,
    AbruptOverride,
    ConflictingManualAction,
    PartialCooperation,
    ContradictoryInstruction,
    MultitaskingCollision,
    SilentAbandonment
}

public class ChaosInjectionHarness
{
    private double _randomProbability;
    private bool _randomEnabled;
    private readonly Random _random = new();

    public event Action<ChaosEvent>? OnChaosInjected;

    public void InjectChaos(ChaosEvent chaosEvent)
    {
        OnChaosInjected?.Invoke(chaosEvent);
    }

    public void EnableRandomInjection(double probability)
    {
        _randomProbability = Math.Clamp(probability, 0.0, 1.0);
        _randomEnabled = true;
    }

    public void DisableRandomInjection()
    {
        _randomEnabled = false;
    }

    public void EvaluateAndInjectRandomly()
    {
        if (!_randomEnabled) return;

        if (_random.NextDouble() < _randomProbability)
        {
            var values = Enum.GetValues<ChaosEvent>();
            var chosenChaos = values[_random.Next(values.Length)];
            InjectChaos(chosenChaos);
        }
    }
}
