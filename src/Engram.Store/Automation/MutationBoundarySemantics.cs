using System;
using System.Collections.Generic;

namespace Engram.Store.Automation;

public class MutationBoundarySemantics
{
    public bool IsReversible { get; set; } = true;
    public bool IsRecoverable { get; set; } = true;
    public bool IsIrreversible { get; set; }
    public bool IsExternallyPropagated { get; set; }
    public List<string> CausalDependencies { get; set; } = new();
}
