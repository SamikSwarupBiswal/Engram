using System;

namespace Engram.Store.Automation;

public enum UncertaintyLevel
{
    U1_Observational,   // Minor mismatch (e.g. OCR delay) -> Trigger retry/re-verify
    U2_StateAmbiguity,  // Internal app state unclear -> Suspend and ask user
    U3_Irreversible,    // Irreversible action state unclear -> Freeze and audit log
    U4_Propagation,     // External propagation unclear (e.g. mail sent status) -> Quarantine step
    U5_Constitutional   // Safety or boundary status unclear -> Full immediate halt
}
