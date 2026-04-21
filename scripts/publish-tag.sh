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

skip_confirmation=false
dry_run=false
delete_mode=false
tag_name=""

for arg in "$@"; do
  case "$arg" in
    --yes)
      skip_confirmation=true
      ;;
    --dry-run)
      dry_run=true
      ;;
    --delete)
      delete_mode=true
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

if [[ "$delete_mode" == true ]]; then
  echo "Mode: Delete tag"
else
  echo "Mode: Publish tag"
fi
echo
echo "Tags must match one of:"
echo "  vX.Y.Z"
echo "  vX.Y.Z-rc.N"
echo "  vX.Y.Z-dev.N"
echo

git fetch --tags origin

if [[ -z "$tag_name" ]]; then
  if [[ "$delete_mode" == true ]]; then
    # Build list of matching tags from remote
    mapfile -t available_tags < <(git ls-remote --tags origin 'refs/tags/v*' \
      | awk '{print $2}' | sed 's|refs/tags/||' | grep -v '\^{}' \
      | grep -E '^v[0-9]+\.[0-9]+\.[0-9]+(-rc\.[0-9]+|-dev\.[0-9]+)?$' \
      | sort -Vr)

    if [[ ${#available_tags[@]} -eq 0 ]]; then
      echo "No matching tags found on origin."
      exit 1
    fi

    while true; do
      echo "Tags on origin:"
      for i in "${!available_tags[@]}"; do
        printf "  %d) %s\n" "$((i+1))" "${available_tags[$i]}"
      done
      echo
      read -r -p "Enter number to delete (q to quit): " selection
      [[ "$selection" =~ ^[Qq]$ ]] && echo "Aborted." && exit 0

      if [[ "$selection" =~ ^[0-9]+$ ]] && (( selection >= 1 && selection <= ${#available_tags[@]} )); then
        tag_name="${available_tags[$((selection-1))]}"

        # Check local existence
        tag_exists_locally=false
        tag_exists_remote=false
        git rev-parse -q --verify "refs/tags/$tag_name" >/dev/null 2>&1 && tag_exists_locally=true
        git ls-remote --tags origin "refs/tags/$tag_name" | grep -q . && tag_exists_remote=true

        delete_action="Delete tag '$tag_name'"
        [[ "$tag_exists_locally" == true && "$tag_exists_remote" == true ]] && delete_action="$delete_action (locally and on origin)"
        [[ "$tag_exists_locally" == true && "$tag_exists_remote" == false ]] && delete_action="$delete_action (locally)"
        [[ "$tag_exists_locally" == false && "$tag_exists_remote" == true ]] && delete_action="$delete_action (on origin)"

        read -r -p "$delete_action? [y/N]: " confirm
        if [[ "$confirm" =~ ^[Yy]$ ]]; then
          [[ "$tag_exists_locally" == true ]] && git tag -d "$tag_name"
          [[ "$tag_exists_remote" == true ]] && git push origin --delete "$tag_name"
          echo "Deleted tag '$tag_name'."
          echo
          # Refresh list
          mapfile -t available_tags < <(git ls-remote --tags origin 'refs/tags/v*' \
            | awk '{print $2}' | sed 's|refs/tags/||' | grep -v '\^{}' \
            | grep -E '^v[0-9]+\.[0-9]+\.[0-9]+(-rc\.[0-9]+|-dev\.[0-9]+)?$' \
            | sort -Vr)
          [[ ${#available_tags[@]} -eq 0 ]] && echo "No more tags." && exit 0
        else
          echo "Skipped."
          echo
        fi
        tag_name=""
      else
        echo "Invalid selection."
        echo
      fi
    done
  else
    read -r -p "Enter git tag to publish: " tag_name
  fi
else
  echo "Requested tag: $tag_name"
fi

if [[ -z "$tag_name" ]]; then
  echo "Tag is required."
  exit 1
fi

if [[ ! "$tag_name" =~ ^v[0-9]+\.[0-9]+\.[0-9]+(-rc\.[0-9]+|-dev\.[0-9]+)?$ ]]; then
  echo "Invalid tag '$tag_name'. Expected vX.Y.Z, vX.Y.Z-rc.N, or vX.Y.Z-dev.N."
  exit 1
fi

tag_exists_locally=false
tag_exists_remote=false

if git rev-parse -q --verify "refs/tags/$tag_name" >/dev/null 2>&1; then
  tag_exists_locally=true
fi

if git ls-remote --tags origin "refs/tags/$tag_name" | grep -q .; then
  tag_exists_remote=true
fi

if [[ "$delete_mode" == true ]]; then
  if [[ "$tag_exists_locally" == false && "$tag_exists_remote" == false ]]; then
    echo "Tag '$tag_name' does not exist locally or on origin."
    exit 1
  fi
else
  if [[ "$tag_exists_locally" == true ]]; then
    echo "Tag '$tag_name' already exists locally."
    exit 1
  fi

  if [[ "$tag_exists_remote" == true ]]; then
    echo "Tag '$tag_name' already exists on origin."
    exit 1
  fi
fi

status_output=$(git status --short)
if [[ -n "$status_output" ]]; then
  echo "Working tree has uncommitted changes:"
  echo "$status_output"
  echo
  if [[ "$delete_mode" == false ]]; then
    if [[ "$skip_confirmation" == true ]]; then
      echo "Continuing because --yes was provided."
    else
      read -r -p "Continue tagging the current commit anyway? [y/N]: " continue_dirty
      if [[ ! "$continue_dirty" =~ ^[Yy]$ ]]; then
        echo "Aborted."
        exit 1
      fi
    fi
  else
    echo "(Dirty working tree warning does not apply to delete operations.)"
  fi
fi

if [[ "$delete_mode" == true ]]; then
  delete_action="Delete tag '$tag_name'"
  if [[ "$tag_exists_locally" == true ]]; then
    delete_action="$delete_action (locally"
  fi
  if [[ "$tag_exists_remote" == true ]]; then
    if [[ "$tag_exists_locally" == true ]]; then
      delete_action="$delete_action and on origin)"
    else
      delete_action="$delete_action (on origin)"
    fi
  else
    if [[ "$tag_exists_locally" == true ]]; then
      delete_action="$delete_action)"
    fi
  fi

  if [[ "$skip_confirmation" == true ]]; then
    echo "$delete_action because --yes was provided."
  else
    read -r -p "$delete_action? [y/N]: " confirm
    if [[ ! "$confirm" =~ ^[Yy]$ ]]; then
      echo "Aborted."
      exit 1
    fi
  fi
else
  if [[ "$skip_confirmation" == true ]]; then
    echo "Creating and pushing tag '$tag_name' for commit $current_commit because --yes was provided."
  else
    read -r -p "Create and push tag '$tag_name' for commit $current_commit? [y/N]: " confirm
    if [[ ! "$confirm" =~ ^[Yy]$ ]]; then
      echo "Aborted."
      exit 1
    fi
  fi
fi

if [[ "$dry_run" == true ]]; then
  if [[ "$delete_mode" == true ]]; then
    echo "Dry run: would delete tag '$tag_name'"
    [[ "$tag_exists_locally" == true ]] && echo "  - locally"
    [[ "$tag_exists_remote" == true ]] && echo "  - on origin"
  else
    echo "Dry run: would create annotated tag '$tag_name' on commit $current_commit and push it to origin."
  fi
  exit 0
fi

if [[ "$delete_mode" == true ]]; then
  [[ "$tag_exists_locally" == true ]] && git tag -d "$tag_name"
  [[ "$tag_exists_remote" == true ]] && git push origin --delete "$tag_name"
  echo "Deleted tag '$tag_name'."
else
  git tag -a "$tag_name" -m "Release $tag_name"
  git push origin "$tag_name"
  echo "Published tag '$tag_name' to origin."
fi