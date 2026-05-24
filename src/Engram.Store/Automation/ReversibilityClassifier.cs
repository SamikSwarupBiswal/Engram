using System;

namespace Engram.Store.Automation;

public enum ReversibilityType
{
    Reversible,
    Recoverable,
    Irreversible
}

public class ReversibilityClassifier
{
    private readonly ReversibilityEvaluator _evaluator;

    public ReversibilityClassifier(ReversibilityEvaluator? evaluator = null)
    {
        _evaluator = evaluator ?? new ReversibilityEvaluator();
    }

    public ReversibilityType ClassifyAction(AutomationAction action)
    {
        if (action == null) return ReversibilityType.Irreversible;

        var score = _evaluator.Evaluate(action);
        return score switch
        {
            ReversibilityScore.Reversible => ReversibilityType.Reversible,
            ReversibilityScore.Mostly or ReversibilityScore.Maybe => ReversibilityType.Recoverable,
            _ => ReversibilityType.Irreversible
        };
    }
}
