#!/usr/bin/env bash
set -euo pipefail

repo_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"

exec dotnet run \
  --project "$repo_dir/tools/Frends.HIT.MomentumToRaindance.Local/Frends.HIT.MomentumToRaindance.Local.csproj" \
  -- \
  --env "$repo_dir/.env" \
  "$@"
