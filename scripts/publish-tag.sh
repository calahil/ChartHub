#!/usr/bin/env bash

set -euo pipefail

repo_root=$(git rev-parse --show-toplevel 2>/dev/null || true)
if [[ -z "$repo_root" ]]; then
  echo "This script must be run inside a git repository."
  exit 1
fi

cd "$repo_root"

if ! git remote get-url origin >/dev/null 2>&1; then
  echo "Git remote 'origin' is not configured."
  exit 1
fi

current_branch=$(git rev-parse --abbrev-ref HEAD)
current_commit=$(git rev-parse --short HEAD)

echo "Repository: $repo_root"
echo "Branch:     $current_branch"
echo "Commit:     $current_commit"
echo
echo "Release tags must match one of:"
echo "  vX.Y.Z"
echo "  vX.Y.Z-rc.N"
echo

skip_confirmation=false
dry_run=false
tag_name=""

for arg in "$@"; do
  case "$arg" in
    --yes)
      skip_confirmation=true
      ;;
    --dry-run)
      dry_run=true
      ;;
    -*)
      echo "Unknown option: $arg"
      exit 1
      ;;
    *)
      if [[ -n "$tag_name" ]]; then
        echo "Only one tag argument is supported."
        exit 1
      fi
      tag_name="$arg"
      ;;
  esac
done

if [[ -z "$tag_name" ]]; then
  read -r -p "Enter git tag to publish: " tag_name
else
  echo "Requested tag: $tag_name"
fi

if [[ -z "$tag_name" ]]; then
  echo "Tag is required."
  exit 1
fi

if [[ ! "$tag_name" =~ ^v[0-9]+\.[0-9]+\.[0-9]+(-rc\.[0-9]+)?$ ]]; then
  echo "Invalid tag '$tag_name'. Expected vX.Y.Z or vX.Y.Z-rc.N."
  exit 1
fi

git fetch --tags origin

if git rev-parse -q --verify "refs/tags/$tag_name" >/dev/null 2>&1; then
  echo "Tag '$tag_name' already exists locally."
  exit 1
fi

if git ls-remote --tags origin "refs/tags/$tag_name" | grep -q .; then
  echo "Tag '$tag_name' already exists on origin."
  exit 1
fi

status_output=$(git status --short)
if [[ -n "$status_output" ]]; then
  echo "Working tree has uncommitted changes:"
  echo "$status_output"
  echo
  if [[ "$skip_confirmation" == true ]]; then
    echo "Continuing because --yes was provided."
  else
    read -r -p "Continue tagging the current commit anyway? [y/N]: " continue_dirty
    if [[ ! "$continue_dirty" =~ ^[Yy]$ ]]; then
      echo "Aborted."
      exit 1
    fi
  fi
fi

if [[ "$skip_confirmation" == true ]]; then
  echo "Creating and pushing tag '$tag_name' for commit $current_commit because --yes was provided."
else
  read -r -p "Create and push tag '$tag_name' for commit $current_commit? [y/N]: " confirm
  if [[ ! "$confirm" =~ ^[Yy]$ ]]; then
    echo "Aborted."
    exit 1
  fi
fi

if [[ "$dry_run" == true ]]; then
  echo "Dry run: would create annotated tag '$tag_name' on commit $current_commit and push it to origin."
  exit 0
fi

git tag -a "$tag_name" -m "Release $tag_name"
git push origin "$tag_name"

echo "Published tag '$tag_name' to origin."