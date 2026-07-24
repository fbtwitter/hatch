namespace Hatch.Models;

// Secret, QrSvg and Uri are only populated during enrolment; ListFactors never returns
// them, so a factor discovered later carries the id alone.
public sealed record MfaFactorInfo(string Id, string? Secret, string? QrSvg, string? Uri);
