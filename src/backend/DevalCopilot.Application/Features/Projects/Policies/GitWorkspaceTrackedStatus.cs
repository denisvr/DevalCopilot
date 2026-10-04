using System.Text;
using DevalCopilot.Application.Features.Projects.Ports;

namespace DevalCopilot.Application.Features.Projects.Policies;

/// <summary>
/// What a porcelain state means for tracked-file delivery (ADR-0024), shared by the reader that attests sources and the manifest
/// derivation that re-checks them, so both always agree on which paths have a comparable baseline and current side. Only a
/// modification, an addition to the index (optionally edited afterwards) and a deletion are comparable against the captured HEAD;
/// anything else (a type change, an intent-to-add, an added-then-deleted path, a path that is also untracked, a duplicated entry)
/// has no comparison this host can state, and an unmerged state is its own fixed reason.
/// </summary>
public static class GitWorkspaceTrackedStatus
{
    /// <summary>The longest path, in UTF-8 bytes, that can be passed as one literal Git argument.</summary>
    public const int MaxPathBytes = 4096;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>The comparison a state allows. When <see cref="Refusal"/> is set no text may be delivered; otherwise
    /// <see cref="BaselineExpected"/> says whether HEAD has the file (false: an addition) and <see cref="CurrentExpected"/> whether
    /// the worktree has it (false: a deletion).</summary>
    public readonly record struct Expectation(GitWorkspaceTrackedOmission? Refusal, bool BaselineExpected, bool CurrentExpected);

    public static Expectation Classify(GitWorkspaceChangedPath state, bool alsoUntracked)
    {
        var index = state.IndexStatus;
        var worktree = state.WorkTreeStatus;
        if (index == "U" || worktree == "U" || (index == "A" && worktree == "A") || (index == "D" && worktree == "D"))
        {
            return Refused(GitWorkspaceTrackedOmission.Unmerged);
        }

        if (alsoUntracked)
        {
            return Refused(GitWorkspaceTrackedOmission.UnsupportedStatus);
        }

        return (index, worktree) switch
        {
            (" ", "M") or ("M", " ") or ("M", "M") => new Expectation(null, BaselineExpected: true, CurrentExpected: true),
            ("A", " ") or ("A", "M") => new Expectation(null, BaselineExpected: false, CurrentExpected: true),
            (" ", "D") or ("D", " ") or ("M", "D") => new Expectation(null, BaselineExpected: true, CurrentExpected: false),
            _ => Refused(GitWorkspaceTrackedOmission.UnsupportedStatus),
        };
    }

    /// <summary>Whether a path can be written into a host-built header exactly and passed as one literal Git argument: well-formed
    /// UTF-8 text (no unpaired surrogate, no replacement character standing for bytes this host could not decode) within
    /// <see cref="MaxPathBytes"/> bytes.</summary>
    public static bool IsEncodablePath(string path)
    {
        if (path.Length == 0 || path.Contains('�'))
        {
            return false;
        }

        try
        {
            return StrictUtf8.GetByteCount(path) <= MaxPathBytes;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    private static Expectation Refused(GitWorkspaceTrackedOmission omission) => new(omission, false, false);
}
