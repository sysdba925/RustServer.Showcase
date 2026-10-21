#!/usr/bin/env bash
# Safe Git synchronization helpers for the separately versioned repositories.

# Bootstrap steps source common.sh before this helper. Keep the helper usable on
# its own as well when invoked directly.
if ! declare -F bootstrap_die >/dev/null 2>&1; then
  # shellcheck source=common.sh
  source "$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)/common.sh"
fi

normalize_github_repository_url() {
  local repository_url="$1"
  local host
  local repository_path

  repository_url="${repository_url%/}"

  case "$repository_url" in
    https://github.com/*|http://github.com/*)
      host="github.com"
      repository_path="${repository_url#*://github.com/}"
      ;;
    git@github.com:*)
      host="github.com"
      repository_path="${repository_url#git@github.com:}"
      ;;
    ssh://git@github.com/*)
      host="github.com"
      repository_path="${repository_url#ssh://git@github.com/}"
      ;;
    *)
      return 1
      ;;
  esac

  repository_path="${repository_path%.git}"
  repository_path="${repository_path%/}"

  [[ "$repository_path" =~ ^[^/]+/[^/]+$ ]] || return 1

  printf '%s/%s\n' "$host" "$repository_path" | tr '[:upper:]' '[:lower:]'
}

repository_origin_matches() {
  local repository_dir="$1"
  local expected_repository_url="$2"
  local actual_repository_url
  local normalized_expected_url
  local normalized_actual_url

  actual_repository_url="$(git -C "$repository_dir" remote get-url origin 2>/dev/null)" || return 1
  normalized_expected_url="$(normalize_github_repository_url "$expected_repository_url")" || return 1
  normalized_actual_url="$(normalize_github_repository_url "$actual_repository_url")" || return 1

  [[ "$normalized_actual_url" == "$normalized_expected_url" ]]
}

require_repository_root() {
  local repository_dir="$1"
  local repository_name="$2"
  local expected_repository_url="$3"
  local repository_root
  local physical_repository_dir

  [[ -d "$repository_dir" ]] || bootstrap_die "$repository_name path is not a directory: $repository_dir"

  repository_root="$(git -C "$repository_dir" rev-parse --show-toplevel 2>/dev/null)" || \
    bootstrap_die "$repository_name exists but is not a Git working tree: $repository_dir"
  physical_repository_dir="$(cd -- "$repository_dir" && pwd -P)"
  repository_root="$(cd -- "$repository_root" && pwd -P)"

  [[ "$repository_root" == "$physical_repository_dir" ]] || \
    bootstrap_die "$repository_name path is not the root of its Git working tree: $repository_dir"

  repository_origin_matches "$repository_dir" "$expected_repository_url" || \
    bootstrap_die "$repository_name has an unexpected origin at $repository_dir; expected $expected_repository_url"
}

require_clean_repository_worktree() {
  local repository_dir="$1"
  local repository_name="$2"
  local status

  status="$(git -C "$repository_dir" status --porcelain=v1 --untracked-files=all)"
  [[ -z "$status" ]] || \
    bootstrap_die "$repository_name has local modifications or untracked files at $repository_dir; resolve them before bootstrap can update it"
}

require_origin_tracking_branch() {
  local repository_dir="$1"
  local repository_name="$2"
  local current_branch
  local upstream_branch

  current_branch="$(git -C "$repository_dir" symbolic-ref --quiet --short HEAD 2>/dev/null)" || \
    bootstrap_die "$repository_name is in detached HEAD state at $repository_dir; check out a tracking branch before rerunning bootstrap"

  upstream_branch="$(git -C "$repository_dir" rev-parse --abbrev-ref --symbolic-full-name '@{upstream}' 2>/dev/null)" || \
    bootstrap_die "$repository_name branch '$current_branch' has no upstream; configure an origin tracking branch before rerunning bootstrap"

  [[ "$upstream_branch" == origin/* ]] || \
    bootstrap_die "$repository_name branch '$current_branch' tracks '$upstream_branch', not origin; configure an origin tracking branch before rerunning bootstrap"

  printf '%s\n' "$upstream_branch"
}

sync_git_repository() {
  local repository_name="$1"
  local repository_dir="$2"
  local expected_repository_url="$3"
  local expected_branch="${4:-}"
  local allowed_existing_branch_pattern="${5:-}"
  local current_branch
  local upstream_branch
  local expected_upstream_branch
  local ahead_count
  local behind_count
  local branch_relation

  require_command git

  if [[ -n "$allowed_existing_branch_pattern" && -z "$expected_branch" ]]; then
    bootstrap_die "$repository_name defines an existing-branch policy without an initial clone branch"
  fi

  if [[ ! -e "$repository_dir" && ! -L "$repository_dir" ]]; then
    mkdir -p -- "$(dirname -- "$repository_dir")"
    bootstrap_info "Cloning $repository_name into $repository_dir"
    if [[ -n "$expected_branch" ]]; then
      git clone --branch "$expected_branch" "$expected_repository_url" "$repository_dir" || \
        bootstrap_die "Could not clone $repository_name branch '$expected_branch' into $repository_dir"
    else
      git clone "$expected_repository_url" "$repository_dir" || \
        bootstrap_die "Could not clone $repository_name into $repository_dir"
    fi
    require_repository_root "$repository_dir" "$repository_name" "$expected_repository_url"

    if [[ -n "$expected_branch" ]]; then
      current_branch="$(git -C "$repository_dir" symbolic-ref --quiet --short HEAD 2>/dev/null)" || \
        bootstrap_die "$repository_name clone is in detached HEAD state at $repository_dir"
      [[ "$current_branch" == "$expected_branch" ]] || \
        bootstrap_die "$repository_name clone checked out '$current_branch', expected '$expected_branch'"
      upstream_branch="$(require_origin_tracking_branch "$repository_dir" "$repository_name")"
      [[ "$upstream_branch" == "origin/$expected_branch" ]] || \
        bootstrap_die "$repository_name branch '$expected_branch' tracks '$upstream_branch', expected 'origin/$expected_branch'"
    fi

    bootstrap_info "$repository_name cloned successfully"
    return 0
  fi

  require_repository_root "$repository_dir" "$repository_name" "$expected_repository_url"
  require_clean_repository_worktree "$repository_dir" "$repository_name"
  upstream_branch="$(require_origin_tracking_branch "$repository_dir" "$repository_name")"

  if [[ -n "$expected_branch" ]]; then
    current_branch="$(git -C "$repository_dir" symbolic-ref --quiet --short HEAD 2>/dev/null)" || \
      bootstrap_die "$repository_name is in detached HEAD state at $repository_dir; check out '$expected_branch' before rerunning bootstrap"

    if [[ -n "$allowed_existing_branch_pattern" ]]; then
      [[ "$current_branch" =~ $allowed_existing_branch_pattern ]] || \
        bootstrap_die "$repository_name is checked out on unsupported branch '$current_branch' at $repository_dir; supported branches match '$allowed_existing_branch_pattern'"
      expected_upstream_branch="origin/$current_branch"
    else
      [[ "$current_branch" == "$expected_branch" ]] || \
        bootstrap_die "$repository_name is checked out on '$current_branch', expected '$expected_branch' at $repository_dir"
      expected_upstream_branch="origin/$expected_branch"
    fi

    [[ "$upstream_branch" == "$expected_upstream_branch" ]] || \
      bootstrap_die "$repository_name branch '$current_branch' tracks '$upstream_branch', expected '$expected_upstream_branch'"
  fi

  bootstrap_info "Fetching $repository_name from origin"
  git -C "$repository_dir" fetch --no-tags origin || \
    bootstrap_die "Could not fetch origin for $repository_name"

  git -C "$repository_dir" rev-parse --verify --quiet "${upstream_branch}^{commit}" >/dev/null || \
    bootstrap_die "$repository_name upstream '$upstream_branch' is not available after fetch"

  branch_relation="$(git -C "$repository_dir" rev-list --left-right --count "HEAD...$upstream_branch")" || \
    bootstrap_die "$repository_name cannot determine its relation to $upstream_branch"
  read -r ahead_count behind_count <<< "$branch_relation"

  if (( ahead_count > 0 && behind_count > 0 )); then
    bootstrap_die "$repository_name has diverged from $upstream_branch; resolve it manually before bootstrap can update it"
  fi

  if (( ahead_count > 0 )); then
    bootstrap_die "$repository_name is ahead of $upstream_branch; resolve the local commits manually before bootstrap can update it"
  fi

  if (( behind_count == 0 )); then
    bootstrap_skip "$repository_name is already current with $upstream_branch"
    return 0
  fi

  bootstrap_info "Fast-forwarding $repository_name from $upstream_branch"
  git -C "$repository_dir" merge --ff-only "$upstream_branch" || \
    bootstrap_die "$repository_name cannot fast-forward from $upstream_branch; local history was left unchanged"

  bootstrap_info "$repository_name is synchronized"
}
