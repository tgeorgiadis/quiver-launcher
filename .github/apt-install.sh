#!/usr/bin/env bash
# Ubuntu's packages for a CI job: `.github/apt-install.sh libfoo-dev libbar`. Ubuntu's mirror can stall (an update
# once sat for all of its step's 8 minutes, and 62 MB once took 20): what comes from the mirror, the package lists and
# then the packages, has a time limit for each try, and a try that runs out starts again, up to three times, rather
# than holding the job. Nothing else is tried again: a package that doesn't exist, or one that won't install, fails
# at once as before. The install itself is from what was downloaded, so it doesn't wait on the mirror.
set -uo pipefail

# Within a try, a dead connection is given up on after 30 seconds and tried again.
apt=(apt-get -o Acquire::Retries=3 -o Acquire::http::Timeout=30 -o Acquire::https::Timeout=30)

# Runs `apt-get …` as root, called `what` in the log, with a time limit; again if it runs out, but not if it fails.
fetch() {
  local what=$1 limit=$2 status
  shift 2
  for try in 1 2 3; do
    # timeout as root, so it can stop apt-get itself rather than only sudo.
    sudo timeout --kill-after=15 "$limit" "${apt[@]}" "$@"
    status=$?
    # 124: the time ran out; 137: and apt-get had to be killed.
    if [ "$status" -ne 124 ] && [ "$status" -ne 137 ]; then return "$status"; fi
    echo "::warning::$what was still going after $limit (try $try of 3)"
  done
  echo "::error::$what stalled three times: Ubuntu's mirror is too slow just now. Run the job again."
  return 1
}

fetch "The package lists" 2m update || exit
fetch "The download" 4m install -y --download-only "$@" || exit
sudo "${apt[@]}" install -y "$@"
