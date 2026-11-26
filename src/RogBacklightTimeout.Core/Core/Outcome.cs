namespace RogBacklightTimeout.Core;

/// <summary>The result of an operation the user asked for: whether it worked, and what to tell them.</summary>
internal readonly record struct Outcome(bool Ok, string Message)
{
    public static Outcome Success(string message) => new(true, message);

    public static Outcome Failure(string message) => new(false, message);
}
