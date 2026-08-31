#!/usr/bin/env bash
#
# Branch protection for main and develop.
#
# Protection rules live in GitHub's settings, not in the repository, so they
# cannot be set by a workflow file. This script is the version-controlled
# record of what those settings should be — run it once per repository, and
# again whenever the required checks change.
#
#   gh auth login                     # once, if you have not already
#   ./deploy/github/protect-branches.sh aslam6161/WoodHeart_BE
#   ./deploy/github/protect-branches.sh aslam6161/WoodHeart_FE
#
# Optional second argument: the number of required approvals (default 0).
#
#   ./deploy/github/protect-branches.sh aslam6161/WoodHeart_BE 1
#
# WHY THE DEFAULT IS 0 AND NOT 1
# ------------------------------
# GitHub does not let you approve your own pull request. On a repository with
# one developer, requiring one approval means every pull request is permanently
# unmergeable — the rule does not enforce review, it just makes you disable the
# rule, and a protection rule you routinely switch off protects nothing.
#
# So the enforced part is what actually can be enforced today: no direct pushes
# to main or develop, no force-pushes, no deletion, and every status check
# green before merge. The review still happens — you read the diff on the pull
# request before merging it — it is simply not gated by a button GitHub will
# not let you press.
#
# The day a second person gets write access, re-run this with `1` and the gate
# becomes real.

set -euo pipefail

REPO="${1:-}"
APPROVALS="${2:-0}"

if [ -z "$REPO" ]; then
  echo "usage: $0 <owner/repo> [approvals]" >&2
  exit 1
fi

if ! gh auth status >/dev/null 2>&1; then
  echo "error: not logged in. Run 'gh auth login' first." >&2
  exit 1
fi

# These strings must match the `name:` of each job in the workflow files. If a
# job is renamed and this is not, the check silently stops being required.
BACKEND_CHECKS='"Build & Test","Dependency Audit","Branch Name","Docker Image Builds"'
FRONTEND_CHECKS='"Build & Test","Dependency Audit","Branch Name","Docker Image Builds"'

case "$REPO" in
  *WoodHeart_BE) CHECKS="$BACKEND_CHECKS" ;;
  *WoodHeart_FE) CHECKS="$FRONTEND_CHECKS" ;;
  *)
    echo "error: unrecognised repository '$REPO'." >&2
    echo "Expected a name ending in WoodHeart_BE or WoodHeart_FE." >&2
    exit 1
    ;;
esac

protect() {
  local branch="$1"
  local linear="$2"

  echo "→ ${REPO}  ${branch}"

  # strict:true rebases the requirement — a branch must be up to date with the
  # base before its checks count. Without it you can merge two individually
  # green branches into a red main.
  gh api \
    --method PUT \
    -H "Accept: application/vnd.github+json" \
    "repos/${REPO}/branches/${branch}/protection" \
    --input - <<JSON >/dev/null
{
  "required_status_checks": {
    "strict": true,
    "contexts": [${CHECKS}]
  },
  "enforce_admins": false,
  "required_pull_request_reviews": {
    "required_approving_review_count": ${APPROVALS},
    "dismiss_stale_reviews": true,
    "require_code_owner_reviews": false,
    "require_last_push_approval": false
  },
  "restrictions": null,
  "required_linear_history": ${linear},
  "allow_force_pushes": false,
  "allow_deletions": false,
  "block_creations": false,
  "required_conversation_resolution": true,
  "lock_branch": false,
  "allow_fork_syncing": false
}
JSON
}

# Linear history is off on BOTH branches, and that is a correction rather than
# an oversight.
#
# Requiring it on main sounded tidy -- one commit per release, so `git log main`
# reads as a list of releases. But it forbids merge commits, so a develop -> main
# release has to be squashed or rebased. Either one writes commits onto main that
# do not exist on develop, the two branches stop sharing a merge base, and every
# release after the first is fighting a history that no longer lines up.
#
# That is the well-known reason git-flow merges into main rather than rebasing
# onto it. A real merge commit on main is what keeps the next release honest.
protect main false
protect develop false

echo
echo "Protected main and develop on ${REPO}."
echo "  required approvals: ${APPROVALS}"
echo "  required checks:    ${CHECKS}"
echo
if [ "$APPROVALS" = "0" ]; then
  echo "Note: enforce_admins is off, so you can still merge an emergency fix."
  echo "      Approvals are 0 — see the comment at the top of this script."
fi
