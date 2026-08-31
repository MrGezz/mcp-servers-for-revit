using RevitMCPCommandSet.Localization;

namespace RevitMCPCommandSet.Models.Common;

public class AIResult<T>
{
    /// <summary>
    ///     Whether the operation succeeded
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    ///     Human-readable outcome.
    ///
    ///     Every command in this project reports through this one property, which
    ///     makes it the single place optional localisation can be applied. The
    ///     stored value is always the ENGLISH text the code set; the getter
    ///     substitutes a translation only when one is configured AND present, so
    ///     behaviour with no catalogue is byte-identical to having none of this.
    /// </summary>
    public string Message
    {
        get => Strings.T(_message);
        set => _message = value;
    }

    private string _message;

    /// <summary>
    ///     Response data
    /// </summary>
    public T Response { get; set; }
}