#!/usr/bin/env bash
# Publishes every Lambda project under src/Lambdas into cdk.artifacts/<ProjectName>,
# which the CDK stack packages as the function code. No Docker required.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
artifacts_dir="$repo_root/cdk.artifacts"

rm -rf "$artifacts_dir"

for project_file in "$repo_root"/src/Lambdas/*/*.csproj; do
  project_name="$(basename "$project_file" .csproj)"
  echo "Publishing $project_name"
  dotnet publish "$project_file" \
    --configuration Release \
    --runtime linux-arm64 \
    --self-contained false \
    --output "$artifacts_dir/$project_name" \
    --nologo \
    --verbosity quiet
done

echo "Lambda packages written to $artifacts_dir"
