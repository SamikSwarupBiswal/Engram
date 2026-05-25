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

        // 1. Hostile Divergence: security blocks or forbidden apps
        if (actual.Contains("consent") || actual.Contains("uac") || actual.Contains("admin") || actual.Contains("credential"))
        {
            return DivergenceInterpretation.Hostile;
        }

        // 2. Sovereignty Divergence: focus hijacked by human
        if (source == "workflow" && expected != actual)
        {
            return DivergenceInterpretation.Sovereignty;
        }
        if (source == "worldmodel" && expected.Contains("document") && actual == "")
        {
            return DivergenceInterpretation.Sovereignty;
        }

        // 3. Instability Divergence: transient timing issues (tabs, minor UI delay)
        if (expected.Contains("tabscount") && actual.Contains("tabscount = 0"))
        {
            return DivergenceInterpretation.Instability;
        }

        // 4. Propagation Divergence: external file or network offline
        if (source == "desktop" && expected.Contains("network") && actual.Contains("offline"))
        {
            return DivergenceInterpretation.Propagation;
        }

        // 5. Semantic Divergence: app view mismatches
        return DivergenceInterpretation.Semantic;
    }
}
