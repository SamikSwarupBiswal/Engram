using System;
using System.Collections.Generic;

namespace Engram.Store.Automation;

public enum DivergenceInterpretation
{
    Benign,         // Minor mismatch, ignore or auto-correct (e.g. minor window title difference)
    Sovereignty,    // Human has hijacked active window/input focus
    Instability,    // Transient timing delay or UI rendering delay
    Propagation,    // Unresolved external file/download state
    Semantic,       // App state is wrong (e.g. not logged in)
    Hostile         // Unknown forbidden application or security blocking
}

public class EnvironmentalDivergenceInterpreter
{
    public DivergenceInterpretation Interpret(EnvironmentDivergence divergence, ExecutionContext context)
    {
        if (divergence == null) return DivergenceInterpretation.Benign;

        var source = divergence.Source.ToLowerInvariant();
        var expected = divergence.Expected.ToLowerInvariant();
        var actual = divergence.Actual.ToLowerInvariant();
        var phase = context.GetVariable<string>("WorkflowNarrativePhase") ?? "Research";

        // 1. Hostile Divergence: security blocks or forbidden apps (always hostile regardless of phase)
        if (actual.Contains("consent") || actual.Contains("uac") || actual.Contains("admin") || actual.Contains("credential"))
        {
            return DivergenceInterpretation.Hostile;
        }

        // Determine base classification
        DivergenceInterpretation baseInterpretation = DivergenceInterpretation.Semantic;

        if (source == "workflow" && expected != actual)
        {
            baseInterpretation = DivergenceInterpretation.Sovereignty;
        }
        else if (source == "worldmodel" && expected.Contains("document") && actual == "")
        {
            baseInterpretation = DivergenceInterpretation.Sovereignty;
        }
        else if (expected.Contains("tabscount") && actual.Contains("tabscount = 0"))
        {
            baseInterpretation = DivergenceInterpretation.Instability;
        }
        else if (source == "desktop" && expected.Contains("network") && actual.Contains("offline"))
        {
            baseInterpretation = DivergenceInterpretation.Propagation;
        }

        // Apply Phase-Relative Context Gating
        if (phase == "Research")
        {
            // Downgrade sovereignty/minor mismatches during research to allow background resilience
            if (baseInterpretation == DivergenceInterpretation.Sovereignty)
            {
                return DivergenceInterpretation.Instability;
            }
        }
        else if (phase == "Payment" || phase == "Mutation")
        {
            // Upgrade instability and semantic mismatches to sovereignty to trigger safe suspension during sensitive writes
            if (baseInterpretation == DivergenceInterpretation.Instability || baseInterpretation == DivergenceInterpretation.Semantic)
            {
                return DivergenceInterpretation.Sovereignty;
            }
        }
        else if (phase == "Recovery")
        {
            // Downgrade mismatches to instability to give recovery mechanisms room to attempt reconciliation without infinite loops
            if (baseInterpretation == DivergenceInterpretation.Sovereignty || baseInterpretation == DivergenceInterpretation.Semantic)
            {
                return DivergenceInterpretation.Instability;
            }
        }

        return baseInterpretation;
    }
}
