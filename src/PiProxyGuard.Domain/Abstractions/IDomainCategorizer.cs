using PiProxyGuard.Domain.Categorization;

namespace PiProxyGuard.Domain.Abstractions;

/// <summary>
/// Classifies a destination host into a coarse <see cref="DomainCategory"/>
/// using offline rules (known suffixes / keywords). Pure and deterministic,
/// so it is trivially unit-testable and needs no network access.
/// </summary>
public interface IDomainCategorizer
{
    /// <summary>Returns the category for a host, or <see cref="DomainCategory.Unknown"/>.</summary>
    DomainCategory Categorize(string host);
}
