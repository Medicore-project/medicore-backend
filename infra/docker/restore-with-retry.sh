#!/bin/sh
set -eu

project="$1"
attempt=1
max_attempts=3

while [ "$attempt" -le "$max_attempts" ]; do
    echo "NuGet restore for $project (attempt $attempt/$max_attempts)"
    if dotnet restore "$project" --disable-parallel; then
        exit 0
    fi

    if [ "$attempt" -eq "$max_attempts" ]; then
        echo "NuGet restore failed after $max_attempts attempts: $project" >&2
        exit 1
    fi

    # Successful downloads survive failed attempts in the BuildKit NuGet cache.
    sleep $((attempt * 10))
    attempt=$((attempt + 1))
done
